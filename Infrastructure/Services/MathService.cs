using System;
using System.Buffers;
using System.Linq;
using System.Runtime.CompilerServices;
using BaselineMode.WPF.Core.Interfaces;
using BaselineMode.WPF.Core.Models;
using BaselineMode.WPF.Core.Models.Baseline;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Optimization;

namespace BaselineMode.WPF.Infrastructure.Services
{
    public class MathService(IHemgFittingService hemgFittingService, ILoggerService loggerService) : IMathService, IFittingService
    {
        // --- Constants ---
        private const double MIN_VALUE = 1e-9;
        private const double MAX_EXP_ARG = 700.0;

        private static readonly ArrayPool<double> _doublePool = ArrayPool<double>.Shared;
        private readonly IHemgFittingService _hemgFittingService = hemgFittingService ?? throw new ArgumentNullException(nameof(hemgFittingService));
        private readonly ILoggerService _logger = loggerService ?? throw new ArgumentNullException(nameof(loggerService));
        private bool _disposed;

        // ==========================================
        // 1. KALMAN FILTER
        // ==========================================
        public class KalmanFilter(double A, double H, double Q, double R, double initialP, double initialX)
        {
            private double _q = Q;
            private double _r = R;
            private double _x = initialX;
            private double _p = initialP;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetR(double value) => _r = value;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public double GetR() => _r;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetQ(double value) => _q = value;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public double GetQ() => _q;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public double Output(double input)
            {
                // Time update
                _x = A * _x;
                _p = A * _p * A + _q;
                // Measurement update
                double K = _p * H / (H * _p * H + _r);
                _x += K * (input - H * _x);
                _p = (1 - K * H) * _p;
                return _x;
            }
        }

        // ==========================================
        // 2. STATISTICS CALCULATIONS
        // ==========================================
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public (double mean, double sigma, double peak) CalculateMoments(double[] xData, double[] yData)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MathService));
            if (xData == null || yData == null || xData.Length != yData.Length) return (0, 0, 0);

            int length = xData.Length;
            if (length == 0) return (0, 0, 0);

            double peak = double.MinValue;
            double totalWeight = 0;
            double sumWeightedX = 0;

            for (int i = 0; i < length; i++)
            {
                double y = yData[i];
                if (y > peak) peak = y;
                totalWeight += y;
                sumWeightedX += xData[i] * y;
            }

            if (totalWeight < MIN_VALUE) return (0, 0, peak);

            double invTotalWeight = 1.0 / totalWeight;
            double mean = sumWeightedX * invTotalWeight;

            double sumWeightedSqDiff = 0;
            for (int i = 0; i < length; i++)
            {
                double diff = xData[i] - mean;
                sumWeightedSqDiff += diff * diff * yData[i];
            }

            double variance = sumWeightedSqDiff * invTotalWeight;
            double sigma = Math.Sqrt(Math.Max(variance, 0));

            return (mean, sigma, peak);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double CalculateRMS(double[] xData, double[] yData, double mean)
        {
            if (xData == null || yData == null) return 0;
            double sumSq = 0;
            double totalW = 0;
            for (int i = 0; i < xData.Length; i++)
            {
                totalW += yData[i];
                double diff = xData[i] - mean;
                sumSq += diff * diff * yData[i];
            }
            return totalW < MIN_VALUE ? 0 : Math.Sqrt(sumSq / totalW);
        }

        public static double CalculateLorentzianValue(double x, double A, double x0, double gamma)
        {
            if (Math.Abs(gamma) < MIN_VALUE) return (Math.Abs(x - x0) < MIN_VALUE ? A : 0.0);
            double gammaSq = gamma * gamma;
            return (A * gammaSq) / (Math.Pow(x - x0, 2) + gammaSq);
        }

        // ==========================================
        // 3. FITTING ALGORITHMS (Levenberg-Marquardt for All)
        // ==========================================
        public FittingAlgorithm Algorithm { get; set; } = FittingAlgorithm.LevenbergMarquardt;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public FittingResult GaussianFit(double[] xData, double[] yData) => PerformSimpleFit(xData, yData, isGaussian: true);

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public FittingResult LorentzianFit(double[] xData, double[] yData) => PerformSimpleFit(xData, yData, isGaussian: false);

        // --- Generic Simple Fit (Gaussian/Lorentzian) ---
        private FittingResult PerformSimpleFit(double[] xData, double[] yData, bool isGaussian)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MathService));
            if (xData == null || yData == null || xData.Length < 3) return FittingResult.Empty(0);

            try
            {
                int len = yData.Length;
                int maxIndex = 0;
                double maxY = yData[0];
                for (int i = 1; i < len; i++)
                {
                    if (yData[i] > maxY) { maxY = yData[i]; maxIndex = i; }
                }
                if (maxY <= MIN_VALUE) return FittingResult.Empty(xData.Length);

                double[] yNorm = _doublePool.Rent(len);
                try
                {
                    double scale = 1.0 / maxY;
                    for (int i = 0; i < len; i++) yNorm[i] = yData[i] * scale;
                    double muGuess = xData[maxIndex];

                    // Simple FWHM estimate
                    double halfMax = maxY / 2.0;
                    int leftIdx = maxIndex, rightIdx = maxIndex;
                    for (int i = maxIndex; i >= 0; i--) if (yData[i] <= halfMax) { leftIdx = i; break; }
                    for (int i = maxIndex; i < yData.Length; i++) if (yData[i] <= halfMax) { rightIdx = i; break; }

                    double fwhmGuess = xData[rightIdx] - xData[leftIdx];
                    if (fwhmGuess <= 0) fwhmGuess = (xData[^1] - xData[0]) * 0.1;

                    double sigmaGuess = isGaussian ? fwhmGuess / 2.355 : fwhmGuess / 2.0;
                    var initialGuess = Vector<double>.Build.Dense([1.0, muGuess, sigmaGuess]);

                    Vector<double> Model(Vector<double> p, Vector<double> x)
                    {
                        var res = Vector<double>.Build.Dense(x.Count);
                        double A = p[0]; double mu = p[1]; double width = Math.Max(p[2], MIN_VALUE);

                        if (isGaussian)
                        {
                            double denom = 2 * width * width;
                            for (int i = 0; i < x.Count; i++)
                            {
                                double exponent = -Math.Pow(x[i] - mu, 2) / denom;
                                if (exponent < -MAX_EXP_ARG) res[i] = 0; else res[i] = A * Math.Exp(exponent);
                            }
                        }
                        else
                        {
                            double wSq = width * width; double num = A * wSq;
                            for (int i = 0; i < x.Count; i++) res[i] = num / (Math.Pow(x[i] - mu, 2) + wSq);
                        }
                        return res;
                    }

                    var solver = new LevenbergMarquardtMinimizer();
                    var yObs = Vector<double>.Build.Dense(yData.Length, i => yNorm[i]);
                    var obj = ObjectiveFunction.NonlinearModel(Model, Vector<double>.Build.Dense(xData), yObs);
                    var result = solver.FindMinimum(obj, initialGuess);
                    var pFinal = result.MinimizingPoint;

                    if (pFinal.Any(v => double.IsNaN(v) || double.IsInfinity(v))) return FittingResult.Empty(xData.Length);

                    double finalAmpReal = pFinal[0] * maxY;
                    double finalMean = pFinal[1];
                    double finalSigma = pFinal[2];
                    double[] fitCurve = new double[xData.Length];

                    if (isGaussian)
                    {
                        double safeSigma = Math.Max(Math.Abs(finalSigma), MIN_VALUE);
                        double denom = 2 * safeSigma * safeSigma;
                        for (int i = 0; i < xData.Length; i++)
                        {
                            double exponent = -Math.Pow(xData[i] - finalMean, 2) / denom;
                            if (exponent < -MAX_EXP_ARG) fitCurve[i] = 0; else fitCurve[i] = finalAmpReal * Math.Exp(exponent);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < xData.Length; i++) fitCurve[i] = CalculateLorentzianValue(xData[i], finalAmpReal, finalMean, finalSigma);
                    }

                    double finalFWHM = isGaussian ? 2.355 * Math.Abs(finalSigma) : 2.0 * Math.Abs(finalSigma);
                    double finalRes = (Math.Abs(finalMean) > MIN_VALUE) ? (finalFWHM / finalMean * 100.0) : 0;

                    var resFinal = new FittingResult(fitCurve, finalMean, finalSigma, finalAmpReal, 0)
                    {
                        IsValid = true,
                        FWHM = finalFWHM,
                        Resolution = finalRes
                    };
                    CalculateFitStats(resFinal, xData, yData, fitCurve);
                    return resFinal;
                }
                finally { _doublePool.Return(yNorm); }
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "Gaussian/Lorentzian fit failed");
                return FittingResult.Empty(xData.Length);
            }
        }

        // ==========================================
        // 4. HYPER EMG FITTING (Updated Logic)
        // ==========================================
        public FittingResult HyperEMGFit(double[] xData, double[] yData) => HemgSingleSidedFit(xData, yData, null);
        public FittingResult HyperEMGFit(double[] xData, double[] yData, double[] rawData) => HemgSingleSidedFit(xData, yData, rawData);
        public FittingResult HemgSingleSidedFit(double[] xData, double[] yData) => HemgSingleSidedFit(xData, yData, null);

        // --- Single Sided ---
        public FittingResult HemgSingleSidedFit(double[] xData, double[] yData, double[]? rawData)
        {
            return HemgDoubleSidedFit(xData, yData, rawData);
        }

        // --- Double Sided ---
        public FittingResult HyperEMGDoubleSidedFit(double[] xData, double[] yData) => HemgDoubleSidedFit(xData, yData, null);
        public FittingResult HyperEMGDoubleSidedFit(double[] xData, double[] yData, double[] rawData) => HemgDoubleSidedFit(xData, yData, rawData);
        public FittingResult HemgDoubleSidedFit(double[] xData, double[] yData) => HemgDoubleSidedFit(xData, yData, null);

        public FittingResult HemgDoubleSidedFit(double[] xData, double[] yData, double[]? rawData)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MathService));
            if (xData == null || yData == null || xData.Length == 0) return FittingResult.Empty(0);

            try
            {
                // เรียก HEMG Fit (ส่ง x=binCenters, y=counts) ผลลัพธ์: fitCurve และ parameters [A, mu, sigma, tauL, tauR, etaL, etaR]
                var (fitCurve, parameters) = _hemgFittingService.HemgDoubleSidedFit(xData, yData);

                // ตรวจสอบผลลัพธ์
                if (fitCurve == null || fitCurve.Length == 0 || parameters == null || parameters.Length < 7)
                    return FittingResult.Empty(xData.Length);

                // แปลงผลลัพธ์กลับเป็น FittingResult Object ของระบบหลัก (Map Parameters ตามลำดับ array ใน HemgFittingService)
                var result = new FittingResult
                {
                    IsValid = true,
                    FitCurve = fitCurve,
                    A = parameters[0],
                    Peak = parameters[0], // ใช้ A เป็น Peak (หรือจะคำนวณ max ของ curve ก็ได้)
                    Mu = parameters[1],
                    Sigma = parameters[2],
                    TauL1 = parameters[3],
                    TauR1 = parameters[4],
                    EtaL1 = parameters[5],
                    EtaR1 = parameters[6]
                };
                // คำนวณค่าสถิติเพิ่มเติม (RMS, R-Squared)
                CalculateFitStats(result, xData, yData, fitCurve);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "HEMG Bridge Error");
                return FittingResult.Empty(xData.Length);
            }
        }

        private static void CalculateFitStats(FittingResult result, double[] xData, double[] yData, double[] fitCurve)
        {
            int n = yData.Length;
            if (n == 0) return;

            double yMean = yData.Average();
            double ssr = 0;
            double ssTot = 0;

            for (int i = 0; i < n; i++)
            {
                double diff = yData[i] - fitCurve[i];
                ssr += diff * diff;
                ssTot += Math.Pow(yData[i] - yMean, 2);
            }

            result.RMS = Math.Sqrt(ssr / n);
            result.R_Squared = ssTot > MIN_VALUE ? 1 - (ssr / ssTot) : 0;

            if (Math.Abs(result.FWHM) < MIN_VALUE)
                result.FWHM = result.Sigma * 2.355;
            if (Math.Abs(result.Mu) > MIN_VALUE && Math.Abs(result.Resolution) < MIN_VALUE)
                result.Resolution = (result.FWHM / result.Mu) * 100.0;
        }

        protected virtual void Dispose(bool disposing) { if (!_disposed) _disposed = true; }
        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    }
}