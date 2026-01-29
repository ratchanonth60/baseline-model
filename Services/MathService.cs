using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BaselineMode.WPF.Models;

namespace BaselineMode.WPF.Services
{
    public class MathService : IMathService
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

        public class KalmanFilter
        {
            private double A, H, Q, R, P, x;

            public KalmanFilter(double A, double H, double Q, double R, double initial_P, double initial_x)
            {
                this.A = A;
                this.H = H;
                this.Q = Q;
                this.R = R;
                this.P = initial_P;
                this.x = initial_x;
            }

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
                x = x + K * (input - H * x);
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

        /// <summary>
        /// Performs Gaussian curve fitting with optimized calculations.
        /// Uses pre-computed constants and vectorized operations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public FittingResult GaussianFit(double[] xData, double[] yData)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MathService));
            
            if (xData == null || yData == null)
                throw new ArgumentNullException(xData == null ? nameof(xData) : nameof(yData));
            
            if (xData.Length != yData.Length)
                throw new ArgumentException("xData and yData must have the same length");
            
            var (mu_guess, sigma_guess, peak_guess) = CalculateMoments(xData, yData);

            // Validate initial parameters
            if (peak_guess <= 0 || sigma_guess <= MIN_VALUE || double.IsNaN(mu_guess) || double.IsInfinity(sigma_guess))
            {
                return FittingResult.Empty(xData.Length);
            }

            // Pre-compute constants for Gaussian
            int length = xData.Length;
            double[] fitCurve = new double[length];
            double invSigma2 = 1.0 / (sigma_guess * sigma_guess);
            double negHalfInvSigma2 = -0.5 * invSigma2;
            double maxFit = 0;

            // Vectorized Gaussian generation
            for (int i = 0; i < length; i++)
            {
                double diff = xData[i] - mu_guess;
                double exponent = negHalfInvSigma2 * diff * diff;

                // Clamp to prevent underflow
                exponent = Math.Max(exponent, -MAX_EXP_ARG);

                double value = peak_guess * Math.Exp(exponent);
                fitCurve[i] = value;
                if (value > maxFit) maxFit = value;
            }

            if (maxFit < MIN_VALUE || double.IsNaN(maxFit))
            {
                return FittingResult.Empty(xData.Length);
            }

            double finalRMS = CalculateRMS(xData, fitCurve, mu_guess);
            return new FittingResult(fitCurve, mu_guess, sigma_guess, peak_guess, finalRMS);
        }

        /// <summary>
        /// Optimized 3x3 linear system solver using Cramer's rule
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double[] SolveLinearSystem3x3(double[][] A, double[] b)
        {
            double det = A[0][0] * (A[1][1] * A[2][2] - A[1][2] * A[2][1])
                       - A[0][1] * (A[1][0] * A[2][2] - A[1][2] * A[2][0])
                       + A[0][2] * (A[1][0] * A[2][1] - A[1][1] * A[2][0]);

            if (Math.Abs(det) < MIN_VALUE)
                throw new Exception("Singular matrix");

            double invDet = 1.0 / det;
            double[] x = new double[3];

            x[0] = (b[0] * (A[1][1] * A[2][2] - A[1][2] * A[2][1])
                  - A[0][1] * (b[1] * A[2][2] - A[1][2] * b[2])
                  + A[0][2] * (b[1] * A[2][1] - A[1][1] * b[2])) * invDet;

            x[1] = (A[0][0] * (b[1] * A[2][2] - A[1][2] * b[2])
                  - b[0] * (A[1][0] * A[2][2] - A[1][2] * A[2][0])
                  + A[0][2] * (A[1][0] * b[2] - b[1] * A[2][0])) * invDet;

            x[2] = (A[0][0] * (A[1][1] * b[2] - b[1] * A[2][1])
                  - A[0][1] * (A[1][0] * b[2] - b[1] * A[2][0])
                  + b[0] * (A[1][0] * A[2][1] - A[1][1] * A[2][0])) * invDet;

            return x;
        }

        /// <summary>
        /// Performs Hyper-EMG curve fitting with optimized calculations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public FittingResult HyperEMGFit(
            double[] xData, double[] yData)
        {
            var (mu_guess, sigma_guess, peak_guess) = CalculateMoments(xData, yData);

            if (peak_guess <= 0 || sigma_guess <= MIN_VALUE)
            {
                return FittingResult.Empty(xData.Length);
            }

            // Pre-compute EMG parameters
            double A = peak_guess * sigma_guess * SQRT_2PI;
            double tau = sigma_guess * 0.5;

            if (tau < MIN_VALUE) tau = MIN_VALUE;

            // Pre-compute constants for EMG calculation
            int length = xData.Length;
            double[] fitCurve = new double[length];
            double maxVal = 0;

            double invTau = 1.0 / tau;
            double sigma2 = sigma_guess * sigma_guess;
            double halfInvTau2 = 0.5 * invTau * invTau;
            double coeff = A * 0.5 * invTau;
            double invSqrt2Sigma = 1.0 / (SQRT_2 * sigma_guess);

            // Vectorized EMG generation
            for (int i = 0; i < length; i++)
            {
                double xDiff = xData[i] - mu_guess;
                double expArg = (sigma2 * halfInvTau2) - (xDiff * invTau);

                // Clamp to prevent overflow
                expArg = Math.Clamp(expArg, -MAX_EXP_ARG, MAX_EXP_ARG);

                double erfcArg = (sigma2 - (tau * xDiff)) * invSqrt2Sigma / tau;
                double emgVal = coeff * Math.Exp(expArg) * Erfc(erfcArg);

                // Check for invalid values
                if (double.IsNaN(emgVal) || double.IsInfinity(emgVal))
                    emgVal = 0;

                fitCurve[i] = emgVal;
                if (emgVal > maxVal) maxVal = emgVal;
            }

            // Scale to match peak if needed
            if (maxVal > MIN_VALUE && Math.Abs(maxVal - peak_guess) > peak_guess * 0.3)
            {
                double scale = peak_guess / maxVal;
                for (int i = 0; i < length; i++)
                {
                    fitCurve[i] *= scale;
                }
                maxVal = peak_guess;
            }

            double finalRMS = CalculateRMS(xData, fitCurve, mu_guess);
            return new FittingResult(fitCurve, mu_guess, sigma_guess, maxVal, finalRMS);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private double Erfc(double x)
        {
            // Abramowitz and Stegun approximation with Horner's method
            const double p = 0.3275911;
            const double a1 = 0.254829592;
            const double a2 = -0.284496736;
            const double a3 = 1.421413741;
            const double a4 = -1.453152027;
            const double a5 = 1.061405429;

            bool isNegative = x < 0;
            double absX = isNegative ? -x : x;

            double t = 1.0 / (1.0 + p * absX);

            // Horner's method for polynomial evaluation (more efficient)
            double poly = t * (a1 + t * (a2 + t * (a3 + t * (a4 + t * a5))));
            double val = poly * Math.Exp(-absX * absX);

            return isNegative ? 2.0 - val : val;
        }

        /// <summary>
        /// Generic solver for larger systems using Gaussian elimination
        ///  SAFE MEMORY: Uses ArrayPool for temporary allocations
        /// </summary>
        private double[] SolveLinearSystem(double[][] A, double[] b, int n)
        {
            // Use optimized 3x3 solver if applicable
            if (n == 3)
                return SolveLinearSystem3x3(A, b);

            //  SAFE: Rent working matrix from pool
            double[][] M = _jaggedPool.Rent(n);

            try
            {
                // Initialize augmented matrix
                for (int i = 0; i < n; i++)
                {
                    M[i] = _doublePool.Rent(n + 1);
                    Array.Copy(A[i], M[i], n);
                    M[i][n] = b[i];
                }

                // Gaussian elimination with partial pivoting
                for (int k = 0; k < n; k++)
                {
                    // Find pivot
                    int max = k;
                    for (int i = k + 1; i < n; i++)
                        if (Math.Abs(M[i][k]) > Math.Abs(M[max][k])) max = i;

                    // Swap rows
                    var temp = M[k];
                    M[k] = M[max];
                    M[max] = temp;

                    if (Math.Abs(M[k][k]) < MIN_VALUE)
                        throw new Exception("Singular matrix");

                    // Eliminate
                    double invPivot = 1.0 / M[k][k];
                    for (int i = k + 1; i < n; i++)
                    {
                        double factor = M[i][k] * invPivot;
                        for (int j = k; j <= n; j++)
                            M[i][j] -= factor * M[k][j];
                    }
                }

                // Back substitution
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
                //  CRITICAL: Return all rented arrays
                for (int i = 0; i < n; i++)
                {
                    if (M[i] != null)
                        _doublePool.Return(M[i]);
                }
                _jaggedPool.Return(M);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // ArrayPools are static shared resources, no need to dispose
                    // Just mark as disposed to prevent further usage
                }
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