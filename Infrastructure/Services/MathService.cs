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
    public class MathService : IMathService, IFittingService
    {
        // Pre-computed constants
        private static readonly double SQRT_2 = Math.Sqrt(2 * Math.PI);
        private const double MIN_VALUE = 1e-9;
        private const double MAX_EXP_ARG = 100;

        // SAFE: Reusable ArrayPool
        private static readonly ArrayPool<double> _doublePool = ArrayPool<double>.Shared;
        private static readonly ArrayPool<double[]> _jaggedPool = ArrayPool<double[]>.Shared;

        private bool _disposed = false;

        // ==========================================
        // 1. KALMAN FILTER
        // ==========================================
        public class KalmanFilter(double A, double H, double Q, double R, double initial_P, double initial_x)
        {
            private readonly double A = A;
            private readonly double H = H;
            private double Q = Q;
            private double R = R;
            private double P = initial_P;
            private double x = initial_x;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetR(double R) => this.R = R;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public double GetR() => this.R;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetQ(double Q) => this.Q = Q;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public double GetQ() => this.Q;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public double Output(double input)
            {
                x = A * x;
                P = A * P * A + Q;
                double K = P * H / (H * P * H + R);
                x += K * (input - H * x);
                P = (1 - K * H) * P;
                return x;
            }
        }

        // ==========================================
        // 2. STATISTICS HELPERS
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
        // 3. FITTING ALGORITHMS (Gaussian & Lorentzian)
        // ==========================================

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public FittingResult GaussianFit(double[] xData, double[] yData)
        {
            return PerformSimpleFit(xData, yData, isGaussian: true);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public FittingResult LorentzianFit(double[] xData, double[] yData)
        {
            return PerformSimpleFit(xData, yData, isGaussian: false);
        }

        private FittingResult PerformSimpleFit(double[] xData, double[] yData, bool isGaussian)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MathService));
            if (xData == null || yData == null || xData.Length < 3) return FittingResult.Empty(0);

            try
            {
                // --- 1. PREPARE DATA & NORMALIZE (Fix for Scaling Issue) ---
                double maxY = yData.Max();
                if (maxY <= MIN_VALUE) return FittingResult.Empty(xData.Length);

                // สร้าง Array ชั่วคราวสำหรับ yData ที่ Normalize แล้ว (0.0 - 1.0)
                // เพื่อให้ Solver คำนวณได้ง่ายขึ้น
                double[] yNorm = _doublePool.Rent(yData.Length);
                try
                {
                    for (int i = 0; i < yData.Length; i++) yNorm[i] = yData[i] / maxY;

                    // --- 2. Initial Guess on Normalized Data ---
                    int maxIndex = Array.IndexOf(yData, maxY);
                    double muGuess = xData[maxIndex];

                    // FWHM Estimation (บนข้อมูลดิบหรือ Norm ก็ได้ ผลเท่ากันเพราะดูที่ width)
                    double halfMax = maxY / 2.0;
                    int leftIdx = maxIndex;
                    int rightIdx = maxIndex;

                    // Scan Left
                    for (int i = maxIndex; i >= 0; i--)
                    {
                        if (yData[i] <= halfMax) { leftIdx = i; break; }
                    }
                    // Scan Right
                    for (int i = maxIndex; i < yData.Length; i++)
                    {
                        if (yData[i] <= halfMax) { rightIdx = i; break; }
                    }

                    double fwhmGuess = xData[rightIdx] - xData[leftIdx];
                    if (fwhmGuess <= 0 || leftIdx == rightIdx)
                        fwhmGuess = (xData[^1] - xData[0]) * 0.1; // Fallback 10% width

                    double sigmaGuess = isGaussian ? fwhmGuess / 2.355 : fwhmGuess / 2.0;

                    // Initial Guess: Amp=1.0 (เพราะ Normalize แล้ว), Mu, Sigma
                    var initialGuess = Vector<double>.Build.Dense([1.0, muGuess, sigmaGuess]);

                    // --- 3. Define Model (Normalized) ---
                    Vector<double> Model(Vector<double> p, Vector<double> x)
                    {
                        var res = Vector<double>.Build.Dense(x.Count);
                        double A = p[0]; // ค่านี้ควรจะวิ่งเข้าหา 1.0
                        double mu = p[1];
                        double width = Math.Max(p[2], MIN_VALUE);

                        if (isGaussian)
                        {
                            double denom = 2 * width * width;
                            for (int i = 0; i < x.Count; i++)
                            {
                                double exponent = -Math.Pow(x[i] - mu, 2) / denom;
                                // ป้องกัน Underflow
                                if (exponent < -MAX_EXP_ARG) res[i] = 0;
                                else res[i] = A * Math.Exp(exponent);
                            }
                        }
                        else // Lorentzian
                        {
                            double wSq = width * width;
                            double num = A * wSq;
                            for (int i = 0; i < x.Count; i++)
                                res[i] = num / (Math.Pow(x[i] - mu, 2) + wSq);
                        }
                        return res;
                    }

                    // --- 4. Solve ---
                    var solver = new LevenbergMarquardtMinimizer();
                    var yObs = Vector<double>.Build.Dense(yData.Length, i => yNorm[i]);
                    var obj = ObjectiveFunction.NonlinearModel(Model, Vector<double>.Build.Dense(xData), yObs); // ใช้ yObs
                    var result = solver.FindMinimum(obj, initialGuess);

                    // --- 5. De-Normalize & Finalize ---
                    var pFinal = result.MinimizingPoint;

                    // Validation: Ensure all parameters are finite
                    if (pFinal.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
                        return FittingResult.Empty(xData.Length);

                    // คืนค่า Amplitude กลับสู่สเกลจริง (คูณด้วย maxY)
                    double finalAmpNorm = pFinal[0];
                    double finalAmpReal = finalAmpNorm * maxY;
                    double finalMean = pFinal[1];
                    double finalSigma = pFinal[2];

                    // สร้างเส้นกราฟจริง (Scale กลับแล้ว)
                    double[] fitCurve = new double[xData.Length];

                    if (isGaussian)
                    {
                        double safeSigma = Math.Max(Math.Abs(finalSigma), MIN_VALUE);
                        double denom = 2 * safeSigma * safeSigma;
                        for (int i = 0; i < xData.Length; i++)
                        {
                            double exponent = -Math.Pow(xData[i] - finalMean, 2) / denom;
                            if (exponent < -MAX_EXP_ARG) fitCurve[i] = 0;
                            else fitCurve[i] = finalAmpReal * Math.Exp(exponent);

                            // Final safety check
                            if (double.IsNaN(fitCurve[i]) || double.IsInfinity(fitCurve[i])) fitCurve[i] = 0;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < xData.Length; i++)
                        {
                            fitCurve[i] = CalculateLorentzianValue(xData[i], finalAmpReal, finalMean, finalSigma);
                            if (double.IsNaN(fitCurve[i]) || double.IsInfinity(fitCurve[i])) fitCurve[i] = 0;
                        }
                    }

                    // Stats
                    double finalFWHM = isGaussian ? 2.355 * Math.Abs(finalSigma) : 2.0 * Math.Abs(finalSigma);
                    double finalRes = (Math.Abs(finalMean) > MIN_VALUE) ? (finalFWHM / finalMean * 100.0) : 0;

                    // Calculate Error (Manual RMS/SSR calculation to avoid Property issues)
                    double ssr = 0;
                    double yMean = yData.Average();
                    double ssTot = 0;

                    for (int i = 0; i < yData.Length; i++)
                    {
                        double diff = yData[i] - fitCurve[i];
                        ssr += diff * diff;
                        ssTot += Math.Pow(yData[i] - yMean, 2);
                    }

                    double rms = ssr;
                    double r2 = (ssTot > MIN_VALUE) ? 1 - (ssr / ssTot) : 0;

                    return new FittingResult(fitCurve, finalMean, finalSigma, finalAmpReal, rms)
                    {
                        FWHM = finalFWHM,
                        Resolution = finalRes,
                        R_Squared = r2
                    };
                }
                finally
                {
                    _doublePool.Return(yNorm);
                }
            }
            catch
            {
                return FittingResult.Empty(xData.Length);
            }
        }

        // ==========================================
        // 4. HYPER EMG (Using Manual Solver)
        // ==========================================
        public FittingResult HyperEMGFit(double[] xData, double[] yData) => HemgSingleSidedFit(xData, yData, null);
        public FittingResult HyperEMGFit(double[] xData, double[] yData, double[] rawData) => HemgSingleSidedFit(xData, yData, rawData);
        public FittingResult HemgSingleSidedFit(double[] xData, double[] yData) => HemgSingleSidedFit(xData, yData, null);
        public FittingAlgorithm Algorithm { get; set; } = FittingAlgorithm.LevenbergMarquardt;

        public FittingResult HemgSingleSidedFit(double[] xData, double[] yData, double[]? rawData)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MathService));
            if (xData == null || yData == null || xData.Length == 0) return FittingResult.Empty(0);

            try
            {
                (double[]? fitCurve, double[]? p) = HemgSingleSidedFitHistogram(xData, yData, rawData);
                if (fitCurve == null || p == null || p.Length < 6) return FittingResult.Empty(xData.Length);

                var result = new FittingResult
                {
                    FitCurve = fitCurve,
                    Peak = p[0],
                    Mu = p[1],
                    Sigma = p[2],
                    TauL1 = p[3],
                    TauL2 = p[4],
                    EtaL1 = p[5],
                    A = p[0]
                };
                CalculateFitStats(result, xData, yData, fitCurve);
                return result;
            }
            catch { return FittingResult.Empty(xData.Length); }
        }

        public FittingResult HyperEMGDoubleSidedFit(double[] xData, double[] yData) => HemgDoubleSidedFit(xData, yData, null);
        public FittingResult HyperEMGDoubleSidedFit(double[] xData, double[] yData, double[] rawData) => HemgDoubleSidedFit(xData, yData, rawData);
        public FittingResult HemgDoubleSidedFit(double[] xData, double[] yData) => HemgDoubleSidedFit(xData, yData, null);

        public FittingResult HemgDoubleSidedFit(double[] xData, double[] yData, double[]? rawData)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MathService));
            if (xData == null || yData == null || xData.Length == 0) return FittingResult.Empty(0);

            try
            {
                (double[]? fitCurve, double[]? p) = HemgDoubleSidedFitHistogram(xData, yData, rawData);
                if (fitCurve == null || p == null || p.Length < 7) return FittingResult.Empty(xData.Length);

                var result = new FittingResult
                {
                    FitCurve = fitCurve,
                    Peak = p[0],
                    Mu = p[1],
                    Sigma = p[2],
                    TauL1 = p[3],
                    TauR1 = p[4],
                    EtaL1 = p[5],
                    EtaR1 = p[6],
                    A = p[0]
                };
                CalculateFitStats(result, xData, yData, fitCurve);
                return result;
            }
            catch { return FittingResult.Empty(xData.Length); }
        }

        private void CalculateFitStats(FittingResult result, double[] xData, double[] yData, double[] fitCurve)
        {
            // Manual Error Calculation for HEMG too
            double ssr = 0;
            double ssTot = 0;
            double yMean = yData.Average();

            for (int i = 0; i < yData.Length; i++)
            {
                ssr += Math.Pow(yData[i] - fitCurve[i], 2);
                ssTot += Math.Pow(yData[i] - yMean, 2);
            }

            result.RMS = ssr; // Or Sqrt(ssr/n) if preferred
            result.R_Squared = (ssTot > MIN_VALUE) ? 1 - (ssr / ssTot) : 0;
            result.FWHM = CalculateFWHM(xData, fitCurve, result.Peak, result.Mu);
            result.Resolution = (Math.Abs(result.Mu) > MIN_VALUE) ? (result.FWHM / result.Mu * 100.0) : 0;
        }

        private static double CalculateFWHM(double[] x, double[] y, double peak, double mu)
        {
            double halfMax = peak / 2.0;
            int peakIdx = -1;
            double minDist = double.MaxValue;
            for (int i = 0; i < x.Length; i++)
            {
                if (Math.Abs(x[i] - mu) < minDist) { minDist = Math.Abs(x[i] - mu); peakIdx = i; }
            }
            if (peakIdx == -1) return 0;

            int leftIdx = 0;
            for (int i = peakIdx; i >= 0; i--) { if (y[i] <= halfMax) { leftIdx = i; break; } }
            int rightIdx = x.Length - 1;
            for (int i = peakIdx; i < x.Length; i++) { if (y[i] <= halfMax) { rightIdx = i; break; } }

            if (leftIdx < rightIdx) return x[rightIdx] - x[leftIdx];
            return 2.355 * (x[rightIdx] - x[peakIdx]);
        }

        // --- HEMG Implementation Helpers ---

        private (double[]? fitCurve, double[]? p) HemgSingleSidedFitHistogram(double[] xData, double[] yData, double[]? rawData)
        {
            var (mean, sigma, peak) = CalculateMoments(xData, yData);
            if (peak <= 0) peak = yData.Max();
            if (sigma <= 0) sigma = (xData[^1] - xData[0]) / 10.0;

            double[] initialP = [peak, mean, sigma, 0.5 * sigma, 0.5 * sigma, 0.5];
            double[] pFinal = FitCurveLevenbergMarquardtManual(initialP, xData, yData, HyperEmgLeft);

            if (pFinal == null) return (null, null);
            double[] fitCurve = new double[xData.Length];
            for (int i = 0; i < xData.Length; i++)
            {
                fitCurve[i] = HyperEmgLeft(pFinal, xData[i]);
                if (double.IsNaN(fitCurve[i]) || double.IsInfinity(fitCurve[i])) fitCurve[i] = 0;
            }
            return (fitCurve, pFinal);
        }

        private (double[]? fitCurve, double[]? p) HemgDoubleSidedFitHistogram(double[] xData, double[] yData, double[]? rawData)
        {
            var (mean, sigma, peak) = CalculateMoments(xData, yData);
            if (peak <= 0) peak = yData.Max();
            if (sigma <= 0) sigma = (xData[^1] - xData[0]) / 10.0;

            double[] initialP = [peak, mean, sigma, 0.5 * sigma, 0.5 * sigma, 0.5, 0.5];
            double[] pFinal = FitCurveLevenbergMarquardtManual(initialP, xData, yData, HyperEmgDouble);

            if (pFinal == null) return (null, null);
            double[] fitCurve = new double[xData.Length];
            for (int i = 0; i < xData.Length; i++)
            {
                fitCurve[i] = HyperEmgDouble(pFinal, xData[i]);
                if (double.IsNaN(fitCurve[i]) || double.IsInfinity(fitCurve[i])) fitCurve[i] = 0;
            }
            return (fitCurve, pFinal);
        }

        // ==========================================
        // 5. MANUAL LEVENBERG-MARQUARDT (Optimized)
        // ==========================================
        private double[] FitCurveLevenbergMarquardtManual(double[] initialP, double[] x, double[] y, Func<double[], double, double> modelFunc)
        {
            int n = initialP.Length;
            int m = x.Length;
            double[] p = (double[])initialP.Clone();
            double lambda = 0.001;

            double[] residuals = _doublePool.Rent(m);
            double[][] J = _jaggedPool.Rent(m);
            for (int i = 0; i < m; i++) J[i] = _doublePool.Rent(n);
            double[][] JtJ = _jaggedPool.Rent(n);
            for (int i = 0; i < n; i++) JtJ[i] = _doublePool.Rent(n);
            double[] JtRes = _doublePool.Rent(n);
            double[] pNew = _doublePool.Rent(n);

            try
            {
                double currentError = CalcResiduals(p, x, y, modelFunc, residuals);

                for (int iter = 0; iter < 50; iter++)
                {
                    CalcJacobian(p, x, modelFunc, J, m, n);

                    // Build Normal Equations
                    for (int i = 0; i < n; i++)
                    {
                        JtRes[i] = 0;
                        Array.Clear(JtJ[i], 0, n);

                        for (int k = 0; k < m; k++)
                        {
                            double jki = J[k][i];
                            JtRes[i] += jki * residuals[k];
                            for (int j = 0; j <= i; j++) JtJ[i][j] += jki * J[k][j];
                        }
                    }
                    for (int i = 0; i < n; i++) for (int j = i + 1; j < n; j++) JtJ[i][j] = JtJ[j][i];

                    for (int i = 0; i < n; i++) JtJ[i][i] *= (1.0 + lambda);

                    double[] delta = SolveLinearSystem(JtJ, JtRes, n);
                    for (int i = 0; i < n; i++) pNew[i] = p[i] + delta[i];
                    EnforceConstraints(pNew);

                    double newError = CalcResiduals(pNew, x, y, modelFunc, null);

                    if (newError < currentError)
                    {
                        lambda /= 10.0;
                        currentError = newError;
                        Array.Copy(pNew, p, n);
                        if (lambda < 1e-7) lambda = 1e-7;
                    }
                    else
                    {
                        lambda *= 10.0;
                        if (lambda > 1e7) break;
                    }
                }
            }
            finally
            {
                _doublePool.Return(residuals);
                for (int i = 0; i < m; i++) _doublePool.Return(J[i]);
                _jaggedPool.Return(J);
                for (int i = 0; i < n; i++) _doublePool.Return(JtJ[i]);
                _jaggedPool.Return(JtJ);
                _doublePool.Return(JtRes);
                _doublePool.Return(pNew);
            }
            return p;
        }

        private double CalcResiduals(double[] p, double[] x, double[] y, Func<double[], double, double> func, double[]? residuals)
        {
            double sumSq = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double diff = y[i] - func(p, x[i]);
                if (residuals != null) residuals[i] = diff;
                sumSq += diff * diff;
            }
            return sumSq;
        }

        private void CalcJacobian(double[] p, double[] x, Func<double[], double, double> func, double[][] J, int m, int n)
        {
            double eps = 1e-5;
            double[] pPerturbed = _doublePool.Rent(n);
            Array.Copy(p, pPerturbed, n);

            try
            {
                for (int j = 0; j < n; j++)
                {
                    double originalVal = p[j];
                    pPerturbed[j] = originalVal + eps;
                    for (int i = 0; i < m; i++)
                        J[i][j] = (func(pPerturbed, x[i]) - func(p, x[i])) / eps;
                    pPerturbed[j] = originalVal;
                }
            }
            finally { _doublePool.Return(pPerturbed); }
        }

        private void EnforceConstraints(double[] p)
        {
            if (p.Length > 0 && p[0] < 0) p[0] = 0;
            if (p.Length > 2 && p[2] < 1e-6) p[2] = 1e-6;
            if (p.Length > 3 && p[3] < 1e-6) p[3] = 1e-6;
            if (p.Length > 4 && p[4] < 1e-6) p[4] = 1e-6;
            if (p.Length > 5) p[5] = Math.Clamp(p[5], 0.0, 1.0);
            if (p.Length > 6) p[6] = Math.Clamp(p[6], 0.0, 1.0);
        }

        // --- Model Functions ---
        private static double HyperEmgLeft(double[] p, double x)
        {
            double A = p[0], mu = p[1], sigma = p[2], tauL1 = p[3], tauL2 = p[4], etaL1 = p[5];
            double Term1 = HyperComponent(x, mu, sigma, tauL1, -1);
            double Term2 = HyperComponent(x, mu, sigma, tauL2, -1);
            return A * (etaL1 * Term1 + (1 - etaL1) * Term2);
        }

        private static double HyperEmgDouble(double[] p, double x)
        {
            double A = p[0], mu = p[1], sigma = p[2], tauL1 = p[3], tauR1 = p[4], etaL1 = p[5], etaR1 = p[6];
            double Left = HyperComponent(x, mu, sigma, tauL1, -1);
            double Right = HyperComponent(x, mu, sigma, tauR1, 1);
            double Gaussian = Math.Exp(-0.5 * Math.Pow((x - mu) / sigma, 2)) / (SQRT_2 * sigma);
            return A * (etaL1 * Left + etaR1 * Right + (1 - etaL1 - etaR1) * Gaussian);
        }

        private static double HyperComponent(double x, double mu, double sigma, double tau, int sign)
        {
            if (tau < 1e-9) return 0;
            double safeSigma = Math.Max(sigma, 1e-9); // Prevent div by 0

            double arg1 = (safeSigma * safeSigma) / (2 * tau * tau);
            double arg2 = sign * (x - mu) / tau;
            double expArg = Math.Min(arg1 + arg2, MAX_EXP_ARG); // Clamp

            double k = 1.0 / (2.0 * tau);
            double val = k * Math.Exp(expArg);
            double erfcArg = ((safeSigma * safeSigma / tau) + sign * (x - mu)) / (SQRT_2 * safeSigma);

            double res = val * Erfc(erfcArg);
            return (double.IsNaN(res) || double.IsInfinity(res)) ? 0 : res;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Erfc(double x)
        {
            bool isNegative = x < 0;
            double absX = isNegative ? -x : x;
            double t = 1.0 / (1.0 + 0.3275911 * absX);
            double poly = t * (0.254829592 + t * (-0.284496736 + t * (1.421413741 + t * (-1.453152027 + t * 1.061405429))));
            double val = poly * Math.Exp(-absX * absX);
            return isNegative ? 2.0 - val : val;
        }

        // --- Linear Algebra for Manual Solver ---
        private static double[] SolveLinearSystem(double[][] A, double[] b, int n)
        {
            if (n == 3) return SolveLinearSystem3x3(A, b);
            double[][] M = _jaggedPool.Rent(n);
            try
            {
                for (int i = 0; i < n; i++) { M[i] = _doublePool.Rent(n + 1); Array.Copy(A[i], M[i], n); M[i][n] = b[i]; }
                for (int k = 0; k < n; k++)
                {
                    int max = k;
                    for (int i = k + 1; i < n; i++) if (Math.Abs(M[i][k]) > Math.Abs(M[max][k])) max = i;
                    (M[max], M[k]) = (M[k], M[max]);
                    if (Math.Abs(M[k][k]) < MIN_VALUE) throw new Exception("Singular matrix");

                    double invPivot = 1.0 / M[k][k];
                    for (int i = k + 1; i < n; i++)
                    {
                        double factor = M[i][k] * invPivot;
                        for (int j = k; j <= n; j++) M[i][j] -= factor * M[k][j];
                        M[i][n] -= factor * M[k][n];
                    }
                }
                double[] x = new double[n];
                for (int i = n - 1; i >= 0; i--)
                {
                    double sum = 0;
                    for (int j = i + 1; j < n; j++) sum += M[i][j] * x[j];
                    x[i] = (M[i][n] - sum) / M[i][i];
                }
                return x;
            }
            finally
            {
                for (int i = 0; i < n; i++) if (M[i] != null) _doublePool.Return(M[i]);
                _jaggedPool.Return(M);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double[] SolveLinearSystem3x3(double[][] A, double[] b)
        {
            double det = A[0][0] * (A[1][1] * A[2][2] - A[1][2] * A[2][1]) - A[0][1] * (A[1][0] * A[2][2] - A[1][2] * A[2][0]) + A[0][2] * (A[1][0] * A[2][1] - A[1][1] * A[2][0]);
            if (Math.Abs(det) < MIN_VALUE) throw new Exception("Singular matrix");
            double invDet = 1.0 / det;
            return [
                (b[0] * (A[1][1] * A[2][2] - A[1][2] * A[2][1]) - A[0][1] * (b[1] * A[2][2] - A[1][2] * b[2]) + A[0][2] * (b[1] * A[2][1] - A[1][1] * b[2])) * invDet,
                (A[0][0] * (b[1] * A[2][2] - A[1][2] * b[2]) - b[0] * (A[1][0] * A[2][2] - A[1][2] * A[2][0]) + A[0][2] * (A[1][0] * b[2] - b[1] * A[2][0])) * invDet,
                (A[0][0] * (A[1][1] * b[2] - b[1] * A[2][1]) - A[0][1] * (A[1][0] * b[2] - b[1] * A[2][0]) + b[0] * (A[1][0] * A[2][1] - A[1][1] * A[2][0])) * invDet
            ];
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed) _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}