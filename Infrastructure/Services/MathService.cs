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
        // --- Constants ---
        private static readonly double SQRT_2 = Math.Sqrt(2 * Math.PI);
        private const double MIN_VALUE = 1e-9;
        private const double MAX_EXP_ARG = 700.0; // ขยายตามแบบที่ Plot ได้

        // --- Memory Pools ---
        private static readonly ArrayPool<double> _doublePool = ArrayPool<double>.Shared;
        private static readonly ArrayPool<double[]> _jaggedPool = ArrayPool<double[]>.Shared;

        private bool _disposed = false;

        // ==========================================
        // 1. KALMAN FILTER (นำกลับมาให้แล้ว)
        // ==========================================
        public class KalmanFilter(double A, double H, double Q, double R, double initial_P, double initial_x)
        {
            private double Q = Q;
            private double R = R;

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
                // Time update
                initial_x = A * initial_x;
                initial_P = A * initial_P * A + Q;
                // Measurement update
                double K = initial_P * H / (H * initial_P * H + R);
                initial_x += K * (input - H * initial_x);
                initial_P = (1 - K * H) * initial_P;
                return initial_x;
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
                double maxY = yData.Max();
                if (maxY <= MIN_VALUE) return FittingResult.Empty(xData.Length);

                double[] yNorm = _doublePool.Rent(yData.Length);
                try
                {
                    for (int i = 0; i < yData.Length; i++) yNorm[i] = yData[i] / maxY;

                    int maxIndex = Array.IndexOf(yData, maxY);
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
                        FWHM = finalFWHM,
                        Resolution = finalRes
                    };
                    CalculateFitStats(resFinal, xData, yData, fitCurve);
                    return resFinal;
                }
                finally { _doublePool.Return(yNorm); }
            }
            catch { return FittingResult.Empty(xData.Length); }
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
                // 1. สร้าง Instance ของ Service ที่ทำงานได้ (หรือจะ Inject เข้ามาก็ได้)
                var hemgService = new HemgFittingService();

                // 2. เรียกใช้งานฟังก์ชัน Fit (ส่ง x=binCenters, y=counts)
                // ผลลัพธ์: fitCurve และ parameters [A, mu, sigma, tauL, tauR, etaL, etaR]
                var (fitCurve, parameters) = hemgService.HemgDoubleSidedFit(xData, yData);

                // 3. ตรวจสอบผลลัพธ์
                if (fitCurve == null || fitCurve.Length == 0 || parameters == null || parameters.Length < 7)
                {
                    return FittingResult.Empty(xData.Length);
                }

                // 4. แปลงผลลัพธ์กลับเป็น FittingResult Object ของระบบหลัก
                var result = new FittingResult
                {
                    FitCurve = fitCurve,

                    // Map Parameters ตามลำดับ array ใน HemgFittingService
                    // p0=[A, mu, sigma, tauL, tauR, etaL, etaR]
                    A = parameters[0],
                    Peak = parameters[0], // ใช้ A เป็น Peak ไปก่อน (หรือจะคำนวณ max ของ curve ก็ได้)
                    Mu = parameters[1],
                    Sigma = parameters[2],
                    TauL1 = parameters[3],
                    TauR1 = parameters[4],
                    EtaL1 = parameters[5],
                    EtaR1 = parameters[6]
                };

                // 5. คำนวณค่าสถิติเพิ่มเติม (RMS, R-Squared)
                CalculateFitStats(result, xData, yData, fitCurve);

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HEMG Bridge Error: {ex.Message}");
                return FittingResult.Empty(xData.Length);
            }
        }

        // [FIX 4] ขยายขอบเขตให้รองรับข้อมูล ADC Channel
        private static void EnforceConstraints(double[] p)
        {
            if (p[0] < 0) p[0] = 0; // Amp
            // Sigma: ขยายจาก 50 เป็น 5000
            p[2] = Math.Clamp(p[2], 0.01, 5000.0);
            // Tau: ขยายจาก 5 เป็น 5000
            p[3] = Math.Clamp(p[3], 0.05, 5000.0);
            if (p.Length > 4) p[4] = Math.Clamp(p[4], 0.05, 5000.0);
            if (p.Length > 5) p[5] = Math.Clamp(p[5], 0.01, 1.0); // Eta
            if (p.Length > 6) p[6] = Math.Clamp(p[6], 0.01, 1.0);
        }

        // --- Model Functions ---
        private static double HyperEmgLeft(double[] p, double x)
        {
            double A = p[0], mu = p[1], sigma = p[2], tauL1 = p[3], tauL2 = p[4], etaL1 = p[5];
            double Term1 = HyperComponent(x, mu, sigma, tauL1, 1);
            double Term2 = HyperComponent(x, mu, sigma, tauL2, 1);
            return A * (etaL1 * Term1 + (1 - etaL1) * Term2);
        }

        private static double HyperEmgDouble(double[] p, double x)
        {
            double A = p[0], mu = p[1], sigma = p[2], tauL1 = p[3], tauR1 = p[4], etaL1 = p[5], etaR1 = p[6];
            double Left = HyperComponent(x, mu, sigma, tauL1, 1);
            double Right = HyperComponent(x, mu, sigma, tauR1, -1);
            double Gaussian = Math.Exp(-0.5 * Math.Pow((x - mu) / sigma, 2)) / (SQRT_2 * sigma);
            return A * (etaL1 * Left + etaR1 * Right + (1 - etaL1 - etaR1) * Gaussian);
        }

        private static double HyperComponent(double x, double mu, double sigma, double tau, int sign)
        {
            if (tau < 1e-9) return 0;
            double safeSigma = Math.Max(sigma, 1e-9);
            double arg1 = (safeSigma * safeSigma) / (2 * tau * tau);
            double arg2 = sign * (x - mu) / tau;
            double z = Math.Min(arg1 + arg2, MAX_EXP_ARG);
            double k = 1.0 / (2.0 * tau);
            double val = k * Math.Exp(z);
            double erfcArg = ((safeSigma * safeSigma / tau) + sign * (x - mu)) / (SQRT_2 * safeSigma);
            double res = val * Erfc(erfcArg);
            return (double.IsNaN(res) || double.IsInfinity(res)) ? 0 : res;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Erfc(double x) => 1.0 - Erf(x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Erf(double x)
        {
            const double a1 = 0.254829592, a2 = -0.284496736, a3 = 1.421413741, a4 = -1.453152027, a5 = 1.061405429, p = 0.3275911;
            int sign = x < 0 ? -1 : 1; x = Math.Abs(x);
            double t = 1.0 / (1.0 + p * x);
            double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
            return sign * y;
        }

        private static double CalcResiduals(double[] p, double[] x, double[] y, Func<double[], double, double> func, double[]? residuals)
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

        private static void CalcJacobian(double[] p, double[] x, Func<double[], double, double> func, double[][] J, int m, int n)
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

        private static void CalculateFitStats(FittingResult result, double[] xData, double[] yData, double[] fitCurve)
        {
            double ssr = 0;
            double ssTot = 0;
            double yMean = 0;

            if (yData.Length > 0) yMean = yData.Average();

            for (int i = 0; i < yData.Length; i++)
            {
                double diff = yData[i] - fitCurve[i];
                ssr += diff * diff;
                ssTot += Math.Pow(yData[i] - yMean, 2);
            }

            result.RMS = ssr;
            result.R_Squared = (ssTot > 1e-9) ? 1 - (ssr / ssTot) : 0;

            // คำนวณ FWHM/Resolution แบบคร่าวๆ (ถ้าจำเป็น)
            result.FWHM = result.Sigma * 2.355;
            if (Math.Abs(result.Mu) > 1e-9)
                result.Resolution = (result.FWHM / result.Mu) * 100.0;
        }

        private static double CalculateFWHM(double[] x, double[] y, double peak, double mu)
        {
            double halfMax = peak / 2.0; int peakIdx = -1; double minDist = double.MaxValue;
            for (int i = 0; i < x.Length; i++) { if (Math.Abs(x[i] - mu) < minDist) { minDist = Math.Abs(x[i] - mu); peakIdx = i; } }
            if (peakIdx == -1) return 0;
            int leftIdx = 0; for (int i = peakIdx; i >= 0; i--) { if (y[i] <= halfMax) { leftIdx = i; break; } }
            int rightIdx = x.Length - 1; for (int i = peakIdx; i < x.Length; i++) { if (y[i] <= halfMax) { rightIdx = i; break; } }
            if (leftIdx < rightIdx) return x[rightIdx] - x[leftIdx];
            return 2.355 * (x[rightIdx] - x[peakIdx]);
        }

        protected virtual void Dispose(bool disposing) { if (!_disposed) _disposed = true; }
        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    }
}