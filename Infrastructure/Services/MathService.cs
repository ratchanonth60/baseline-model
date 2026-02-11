using System;
using System.Buffers;
using System.Linq;
using System.Runtime.CompilerServices;
using BaselineMode.WPF.Core.Interfaces;
using BaselineMode.WPF.Core.Models;

namespace BaselineMode.WPF.Infrastructure.Services
{
    public class MathService : IMathService, IFittingService
    {
        // Pre-computed constants
        private static readonly double SQRT_2PI = Math.Sqrt(2 * Math.PI);
        private static readonly double SQRT_2 = Math.Sqrt(2);
        private const double MIN_VALUE = 1e-9;
        private const double MAX_EXP_ARG = 100;

        // SAFE: Reusable ArrayPool
        private static readonly ArrayPool<double> _doublePool = ArrayPool<double>.Shared;
        private static readonly ArrayPool<double[]> _jaggedPool = ArrayPool<double[]>.Shared;

        private bool _disposed = false;

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
                // Time update - prediction
                x = A * x;
                P = A * P * A + Q;

                // Measurement update - correction
                double K = P * H / (H * P * H + R);
                x += K * (input - H * x);
                P = (1 - K * H) * P;

                return x;
            }
        }

        /// <summary>
        /// Calculates basic statistics (Mean, Sigma, Peak) using Method of Moments.
        /// Optimized: Single-pass algorithm where possible
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public (double mean, double sigma, double peak) CalculateMoments(double[] xData, double[] yData)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MathService));

            if (xData == null)
                throw new ArgumentNullException(nameof(xData));

            if (yData == null)
                throw new ArgumentNullException(nameof(yData));

            if (xData.Length != yData.Length)
                throw new ArgumentException("xData and yData must have the same length");

            int length = xData.Length;
            if (length == 0) return (0, 0, 0);

            double peak = double.MinValue;
            double totalWeight = 0;
            double sumWeightedX = 0;

            // Single pass for peak, total weight, and weighted sum
            for (int i = 0; i < length; i++)
            {
                double y = yData[i];
                if (y > peak) peak = y;
                totalWeight += y;
                sumWeightedX += xData[i] * y;
            }

            if (totalWeight < MIN_VALUE)
                return (0, 0, peak);

            double invTotalWeight = 1.0 / totalWeight;
            double mean = sumWeightedX * invTotalWeight;

            // Second pass for variance (unavoidable for accuracy)
            double sumWeightedSqDiff = 0;
            for (int i = 0; i < length; i++)
            {
                double diff = xData[i] - mean;
                sumWeightedSqDiff += diff * diff * yData[i];
            }

            double variance = sumWeightedSqDiff * invTotalWeight;
            double sigma = Math.Sqrt(Math.Max(variance, 0)); // Ensure non-negative

            return (mean, sigma, peak);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public double CalculateRMS(double[] xData, double[] yData, double mean)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MathService));

            if (xData == null || yData == null)
                throw new ArgumentNullException(xData == null ? nameof(xData) : nameof(yData));

            if (xData.Length != yData.Length)
                throw new ArgumentException("xData and yData must have the same length");

            double sumSquaredDifferences = 0;
            double totalWeight = 0;
            int length = xData.Length;

            for (int i = 0; i < length; i++)
            {
                double y = yData[i];
                totalWeight += y;
                double diff = xData[i] - mean;
                sumSquaredDifferences += diff * diff * y;
            }

            return totalWeight < MIN_VALUE ? 0 : Math.Sqrt(sumSquaredDifferences / totalWeight);
        }
        public static double CalculateLorentzianValue(double x, double A, double x0, double gamma)
        {
            if (gamma == 0)
                return (x == x0 ? A : 0.0);
            double numberator = A * gamma * gamma;
            double denominator = Math.Pow(x - x0, 2) + Math.Pow(gamma, 2);
            return numberator / denominator;
        }

        /// <summary>
        /// Performs Lorentzian curve fitting with optimized calculations.
        /// Uses pre-computed constants and vectorized operations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public FittingResult LorentzianFit(double[] xData, double[] yData)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MathService));

            if (xData == null || yData == null || xData.Length == 0)
                return FittingResult.Empty(0);

            // Step 1: Find peak
            int peakIdx = 0;
            double maxCount = yData[0];
            for (int i = 1; i < yData.Length; i++)
            {
                if (yData[i] > maxCount)
                {
                    maxCount = yData[i];
                    peakIdx = i;
                }
            }

            if (maxCount <= 0)
                return FittingResult.Empty(xData.Length);

            double peakPosition = xData[peakIdx];

            // Step 2: Estimate gamma (half-width at half-maximum) from the data
            double halfMax = maxCount / 2.0;

            // Search left from peak for half-max crossing
            double leftX = xData[0];
            for (int i = peakIdx; i >= 0; i--)
            {
                if (yData[i] <= halfMax)
                {
                    // Linear interpolation for more accurate crossing point
                    if (i < peakIdx)
                    {
                        double t = (halfMax - yData[i]) / (yData[i + 1] - yData[i]);
                        leftX = xData[i] + t * (xData[i + 1] - xData[i]);
                    }
                    else
                    {
                        leftX = xData[i];
                    }
                    break;
                }
            }

            // Search right from peak for half-max crossing
            double rightX = xData[^1];
            for (int i = peakIdx; i < xData.Length; i++)
            {
                if (yData[i] <= halfMax)
                {
                    // Linear interpolation
                    if (i > peakIdx)
                    {
                        double t = (halfMax - yData[i]) / (yData[i - 1] - yData[i]);
                        rightX = xData[i] + t * (xData[i - 1] - xData[i]);
                    }
                    else
                    {
                        rightX = xData[i];
                    }
                    break;
                }
            }

            double fwhm = rightX - leftX;
            double gamma = Math.Max(fwhm / 2.0, MIN_VALUE); // gamma = HWHM

            // Step 3: Refine center (mu) using weighted mean with Lorentzian weighting
            double totalWeight = 0;
            double weightedSumX = 0;
            double threshold = maxCount * 0.1;

            for (int i = 0; i < yData.Length; i++)
            {
                if (yData[i] >= threshold)
                {
                    double y = yData[i];
                    double x = xData[i];
                    double diff = x - peakPosition;
                    double weight = y / (1 + (diff * diff) / (gamma * gamma));
                    totalWeight += weight;
                    weightedSumX += x * weight;
                }
            }

            double mu = totalWeight > 0 ? weightedSumX / totalWeight : peakPosition;

            // Step 4: Generate Lorentzian fit curve
            // L(x) = A * gamma^2 / ((x - x0)^2 + gamma^2)
            // At x = x0: L = A, so A = maxCount
            double A = maxCount;
            double[] fitCurve = new double[xData.Length];

            for (int i = 0; i < xData.Length; i++)
            {
                fitCurve[i] = CalculateLorentzianValue(xData[i], A, mu, gamma);
            }

            // Step 5: Calculate statistics
            double sigma = gamma; // For Lorentzian, use gamma as the width parameter
            double rms = CalculateRMS(xData, fitCurve, mu);
            double resolution = (Math.Abs(mu) > 1e-9) ? (fwhm / mu * 100.0) : 0;

            // R-Squared
            double yMean = yData.Average();
            double ssTot = yData.Sum(y => Math.Pow(y - yMean, 2));
            double ssRes = yData.Zip(fitCurve, (y, f) => Math.Pow(y - f, 2)).Sum();
            double rSquared = (ssTot > 1e-9) ? 1 - (ssRes / ssTot) : 0;

            var result = new FittingResult(fitCurve, mu, sigma, maxCount, rms)
            {
                FWHM = fwhm,
                Resolution = resolution,
                R_Squared = rSquared
            };
            return result;
        }

        /// <summary>
        /// Performs Gaussian curve fitting with optimized calculations.
        /// Uses pre-computed constants and vectorized operations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public FittingResult GaussianFit(double[] xData, double[] yData)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MathService));

            if (xData == null || yData == null || xData.Length == 0)
                return FittingResult.Empty(0);

            // Step 1: Find peak
            int peakIdx = 0;
            double maxCount = yData[0];
            for (int i = 1; i < yData.Length; i++)
            {
                if (yData[i] > maxCount)
                {
                    maxCount = yData[i];
                    peakIdx = i;
                }
            }

            if (maxCount <= 0)
                return FittingResult.Empty(xData.Length);

            double peakPosition = xData[peakIdx];

            // Step 2: Calculate weighted mean and sigma around peak
            double totalWeight = 0;
            double weightedSumX = 0;
            double weightedSumX2 = 0;

            // Use data within ±10% of peak height
            double threshold = maxCount * 0.1;

            for (int i = 0; i < yData.Length; i++)
            {
                if (yData[i] >= threshold)
                {
                    double weight = yData[i];
                    totalWeight += weight;
                    weightedSumX += weight * xData[i];
                    weightedSumX2 += weight * xData[i] * xData[i];
                }
            }

            if (totalWeight <= 0)
                return FittingResult.Empty(xData.Length);

            // Calculate parameters
            double mu = weightedSumX / totalWeight;
            double variance = (weightedSumX2 / totalWeight) - (mu * mu);
            double sigma = Math.Sqrt(Math.Max(variance, MIN_VALUE));

            // Step 3: Generate Gaussian fit
            double[] fitCurve = new double[xData.Length];
            double coeff = 1.0 / (sigma * SQRT_2PI);
            double invSigma2 = 1.0 / (sigma * sigma);

            for (int i = 0; i < xData.Length; i++)
            {
                double diff = xData[i] - mu;
                double exponent = -0.5 * diff * diff * invSigma2;
                exponent = Math.Max(exponent, -MAX_EXP_ARG);
                double gaussianValue = coeff * Math.Exp(exponent);

                // Scale to match peak height
                // Note: The previous implementation scaled it correctly
                fitCurve[i] = gaussianValue * (maxCount / (coeff * Math.Exp(0)));
            }

            // Calculate Stats
            double rms = CalculateRMS(xData, fitCurve, mu);
            double fwhm = 2.355 * sigma;
            double resolution = (Math.Abs(mu) > 1e-9) ? (fwhm / mu * 100.0) : 0;

            // Calculate R-Squared
            double yMean = yData.Average();
            double ssTot = yData.Sum(y => Math.Pow(y - yMean, 2));
            double ssRes = yData.Zip(fitCurve, (y, f) => Math.Pow(y - f, 2)).Sum();
            double rSquared = (ssTot > 1e-9) ? 1 - (ssRes / ssTot) : 0;

            var result = new FittingResult(fitCurve, mu, sigma, maxCount, rms)
            {
                FWHM = fwhm,
                Resolution = resolution,
                R_Squared = rSquared
            };
            return result;
        }

        // Helper to forward to internal implementation with optional raw data
        public FittingResult HyperEMGFit(double[] xData, double[] yData)
        {
            return HemgSingleSidedFit(xData, yData, null);
        }

        public FittingResult HyperEMGFit(double[] xData, double[] yData, double[] rawData) => HemgSingleSidedFit(xData, yData, rawData);

        // Implementation regarding IFittingService (Standard signature)
        public FittingResult HemgSingleSidedFit(double[] xData, double[] yData) => HemgSingleSidedFit(xData, yData, null);

        // IMPLEMENTATION OF IFittingService.Algorithm
        public FittingAlgorithm Algorithm { get; set; } = FittingAlgorithm.LevenbergMarquardt;

        // ...

        // Extended signature with rawData
        public FittingResult HemgSingleSidedFit(double[] xData, double[] yData, double[]? rawData)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MathService));
            }

            if (xData == null || yData == null || xData.Length == 0)
                return FittingResult.Empty(0);

            try
            {
                var hemgService = new HemgFittingService
                {
                    Algorithm = this.Algorithm // Pass selected algorithm
                };
                var (fitCurve, p) = hemgService.HemgSingleSidedFitHistogram(xData, yData, rawData);

                if (fitCurve == null || p == null || p.Length < 6)
                    return FittingResult.Empty(xData.Length);

                // p: [A, mu, sigma, tauL1, tauL2, etaL1]
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
            catch (Exception)
            {
                return FittingResult.Empty(xData.Length);
            }
        }

        public FittingResult HyperEMGDoubleSidedFit(double[] xData, double[] yData)
        {
            return HemgDoubleSidedFit(xData, yData, null);
        }

        public FittingResult HyperEMGDoubleSidedFit(double[] xData, double[] yData, double[] rawData) => HemgDoubleSidedFit(xData, yData, rawData);

        // Implementation regarding IFittingService (Standard signature)
        public FittingResult HemgDoubleSidedFit(double[] xData, double[] yData)
        {
            return HemgDoubleSidedFit(xData, yData, null);
        }

        // Extended signature with rawData
        public FittingResult HemgDoubleSidedFit(double[] xData, double[] yData, double[]? rawData)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MathService));
            }

            if (xData == null || yData == null || xData.Length == 0)
                return FittingResult.Empty(0);

            try
            {
                var hemgService = new HemgFittingService
                {
                    Algorithm = this.Algorithm // Pass selected algorithm
                };
                var (fitCurve, p) = hemgService.HemgDoubleSidedFitHistogram(xData, yData, rawData);

                if (fitCurve == null || p == null || p.Length < 7)
                    return FittingResult.Empty(xData.Length);

                // p: [A, mu, sigma, tauL1, tauR1, etaL1, etaR1]
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
            catch (Exception)
            {
                return FittingResult.Empty(xData.Length);
            }
        }

        private void CalculateFitStats(FittingResult result, double[] xData, double[] yData, double[] fitCurve)
        {
            // RMS
            result.RMS = CalculateRMS(xData, fitCurve, result.Mu);

            // R-Squared
            double yMean = yData.Average();
            double ssTot = yData.Sum(y => Math.Pow(y - yMean, 2));
            double ssRes = yData.Zip(fitCurve, (y, f) => Math.Pow(y - f, 2)).Sum();
            result.R_Squared = (ssTot > 1e-9) ? 1 - (ssRes / ssTot) : 0;

            // FWHM (Numerical search on fitCurve)
            result.FWHM = CalculateFWHM(xData, fitCurve, result.Peak, result.Mu);

            // Resolution
            result.Resolution = (Math.Abs(result.Mu) > 1e-9) ? (result.FWHM / result.Mu * 100.0) : 0;
        }

        private static double CalculateFWHM(double[] x, double[] y, double peak, double mu)
        {
            double halfMax = peak / 2.0;
            int peakIdx = -1;

            // Find peak index closest to mu
            double minDist = double.MaxValue;
            for (int i = 0; i < x.Length; i++)
            {
                if (Math.Abs(x[i] - mu) < minDist)
                {
                    minDist = Math.Abs(x[i] - mu);
                    peakIdx = i;
                }
            }

            if (peakIdx == -1) return 0;

            // Search Left
            int leftIdx = 0;
            for (int i = peakIdx; i >= 0; i--)
            {
                if (y[i] <= halfMax)
                {
                    leftIdx = i;
                    break;
                }
            }

            // Search Right
            int rightIdx = x.Length - 1;
            for (int i = peakIdx; i < x.Length; i++)
            {
                if (y[i] <= halfMax)
                {
                    rightIdx = i;
                    break;
                }
            }

            if (leftIdx < rightIdx)
            {
                return x[rightIdx] - x[leftIdx];
            }
            return 2.355 * (x[rightIdx] - x[peakIdx]); // Fallback
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static double Erfc(double x)
        {
            const double p = 0.3275911;
            const double a1 = 0.254829592;
            const double a2 = -0.284496736;
            const double a3 = 1.421413741;
            const double a4 = -1.453152027;
            const double a5 = 1.061405429;

            bool isNegative = x < 0;
            double absX = isNegative ? -x : x;
            double t = 1.0 / (1.0 + p * absX);
            double poly = t * (a1 + t * (a2 + t * (a3 + t * (a4 + t * a5))));
            double val = poly * Math.Exp(-absX * absX);

            return isNegative ? 2.0 - val : val;
        }

        private static double[] SolveLinearSystem(double[][] A, double[] b, int n)
        {
            if (n == 3)
                return SolveLinearSystem3x3(A, b);

            double[][] M = _jaggedPool.Rent(n);
            try
            {
                for (int i = 0; i < n; i++)
                {
                    M[i] = _doublePool.Rent(n + 1);
                    Array.Copy(A[i], M[i], n);
                    M[i][n] = b[i];
                }

                for (int k = 0; k < n; k++)
                {
                    int max = k;
                    for (int i = k + 1; i < n; i++)
                        if (Math.Abs(M[i][k]) > Math.Abs(M[max][k])) max = i;

                    (M[max], M[k]) = (M[k], M[max]);
                    if (Math.Abs(M[k][k]) < MIN_VALUE)
                        throw new Exception("Singular matrix");

                    double invPivot = 1.0 / M[k][k];
                    for (int i = k + 1; i < n; i++)
                    {
                        double factor = M[i][k] * invPivot;
                        for (int j = k; j <= n; j++)
                            M[i][j] -= factor * M[k][j];
                    }
                }

                double[] x = new double[n];
                for (int i = n - 1; i >= 0; i--)
                {
                    double sum = 0;
                    for (int j = i + 1; j < n; j++)
                        sum += M[i][j] * x[j];
                    x[i] = (M[i][n] - sum) / M[i][i];
                }
                return x;
            }
            finally
            {
                for (int i = 0; i < n; i++)
                {
                    if (M[i] != null)
                        _doublePool.Return(M[i]);
                }
                _jaggedPool.Return(M);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double[] SolveLinearSystem3x3(double[][] A, double[] b)
        {
            double det = A[0][0] * (A[1][1] * A[2][2] - A[1][2] * A[2][1])
                       - A[0][1] * (A[1][0] * A[2][2] - A[1][2] * A[2][0])
                       + A[0][2] * (A[1][0] * A[2][1] - A[1][1] * A[2][0]);

            if (Math.Abs(det) < MIN_VALUE)
                throw new Exception("Singular matrix");

            double invDet = 1.0 / det;
            double[] x =
            [
                (b[0] * (A[1][1] * A[2][2] - A[1][2] * A[2][1])
                      - A[0][1] * (b[1] * A[2][2] - A[1][2] * b[2])
                      + A[0][2] * (b[1] * A[2][1] - A[1][1] * b[2])) * invDet,
                (A[0][0] * (b[1] * A[2][2] - A[1][2] * b[2])
                      - b[0] * (A[1][0] * A[2][2] - A[1][2] * A[2][0])
                      + A[0][2] * (A[1][0] * b[2] - b[1] * A[2][0])) * invDet,
                (A[0][0] * (A[1][1] * b[2] - b[1] * A[2][1])
                      - A[0][1] * (A[1][0] * b[2] - b[1] * A[2][0])
                      + b[0] * (A[1][0] * A[2][1] - A[1][1] * A[2][0])) * invDet,
            ];
            return x;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                // ArrayPools are static, nothing to resolve
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}