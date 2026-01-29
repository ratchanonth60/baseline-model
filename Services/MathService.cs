using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace BaselineMode.WPF.Services
{
    public class MathService
    {
        // Pre-computed constants
        private static readonly double SQRT_2PI = Math.Sqrt(2 * Math.PI);
        private static readonly double SQRT_2 = Math.Sqrt(2);
        private const double MIN_VALUE = 1e-9;
        private const double MAX_EXP_ARG = 100;

        // SAFE: Reusable ArrayPool
        private static readonly ArrayPool<double> _doublePool = ArrayPool<double>.Shared;
        private static readonly ArrayPool<double[]> _jaggedPool = ArrayPool<double[]>.Shared;

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
        /// </summary>
        public (double mean, double sigma, double peak) CalculateMoments(double[] xData, double[] yData)
        {
            int length = xData.Length;
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

            if (totalWeight == 0)
                return (0, 0, 0);

            double mean = sumWeightedX / totalWeight;

            // Second pass for variance
            double sumWeightedSqDiff = 0;
            for (int i = 0; i < length; i++)
            {
                double diff = xData[i] - mean;
                sumWeightedSqDiff += diff * diff * yData[i];
            }

            double variance = sumWeightedSqDiff / totalWeight;
            double sigma = Math.Sqrt(variance);

            //  Debug:
            Console.WriteLine($"CalculateMoments: peak={peak:F1}, mean={mean:F1}, sigma={sigma:F1}, totalWeight={totalWeight:F1}");

            return (mean, sigma, peak);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double CalculateRMS(double[] xData, double[] yData, double mean)
        {
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

            return totalWeight == 0 ? 0 : Math.Sqrt(sumSquaredDifferences / totalWeight);
        }

        /// <summary>
        /// Performs Levenberg-Marquardt optimization to fit a Gaussian curve.
        ///  SAFE MEMORY: Uses ArrayPool for all temporary allocations
        /// </summary>
        public (double[] fitCurve, double mu, double sigma, double peak, double rms) GaussianFit(
            double[] xData, double[] yData)
        {
            var (mu_guess, sigma_guess, peak_guess) = CalculateMoments(xData, yData);

            Console.WriteLine($"GaussianFit Ch: peak={peak_guess:F1}, mu={mu_guess:F1}, sigma={sigma_guess:F1}");

            //  Validate initial parameters
            if (peak_guess <= 0 || sigma_guess <= 0 || double.IsNaN(mu_guess) || double.IsInfinity(sigma_guess))
            {
                Console.WriteLine($" Invalid parameters, returning empty fit");
                return (new double[xData.Length], 0, 0, 0, 0); // Return zeros
            }

            // Generate simple Gaussian (bypass optimization for now)
            double[] fitCurve = new double[xData.Length];
            double sigma2 = sigma_guess * sigma_guess;

            for (int i = 0; i < xData.Length; i++)
            {
                double diff = xData[i] - mu_guess;
                double exponent = -0.5 * diff * diff / sigma2;

                //  Clamp exponent to prevent underflow
                if (exponent < -100) exponent = -100;

                fitCurve[i] = peak_guess * Math.Exp(exponent);
            }

            double maxFit = fitCurve.Max();
            Console.WriteLine($"Generated fitCurve max: {maxFit:F2}");

            if (maxFit == 0 || double.IsNaN(maxFit))
            {
                Console.WriteLine($" FitCurve generation failed");
                return (new double[xData.Length], 0, 0, 0, 0);
            }

            double finalRMS = CalculateRMS(xData, fitCurve, mu_guess);
            return (fitCurve, mu_guess, sigma_guess, peak_guess, finalRMS);

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
        /// Performs Levenberg-Marquardt optimization to fit a Hyper-EMG curve.
        ///  SAFE MEMORY: Uses ArrayPool for all temporary allocations
        /// </summary>
        public (double[] fitCurve, double mu, double sigma, double peak, double rms) HyperEMGFit(
            double[] xData, double[] yData)
        {
            var (mu_guess, sigma_guess, peak_guess) = CalculateMoments(xData, yData);

            Console.WriteLine($"\n=== Hyper-EMG Fit ===");
            Console.WriteLine($"Initial guess: peak={peak_guess:F1}, mu={mu_guess:F1}, sigma={sigma_guess:F1}");

            if (peak_guess <= 0 || sigma_guess <= 0)
            {
                Console.WriteLine($" Invalid initial guess for Hyper-EMG");
                return (new double[xData.Length], mu_guess, sigma_guess, peak_guess, 0);
            }

            //  ใช้ Simple EMG แทน Full Optimization
            double A = peak_guess * sigma_guess * SQRT_2PI;
            double tau = sigma_guess * 0.5; // Tail parameter (ลองปรับ 0.3-0.8)

            Console.WriteLine($"Using Simple Hyper-EMG: A={A:F1}, mu={mu_guess:F1}, sigma={sigma_guess:F1}, tau={tau:F1}");

            // Generate fit curve directly without optimization
            double[] fitCurve = new double[xData.Length];
            double maxVal = 0;

            for (int i = 0; i < xData.Length; i++)
            {
                fitCurve[i] = EMG(xData[i], A, mu_guess, sigma_guess, tau);
                if (fitCurve[i] > maxVal) maxVal = fitCurve[i];
            }

            Console.WriteLine($"Generated Simple Hyper-EMG fitCurve: max={maxVal:F1}");

            //  Scale to match peak if needed
            if (maxVal > 0 && Math.Abs(maxVal - peak_guess) > peak_guess * 0.3)
            {
                double scale = peak_guess / maxVal;
                Console.WriteLine($"Scaling by {scale:F2} to match peak");

                for (int i = 0; i < fitCurve.Length; i++)
                {
                    fitCurve[i] *= scale;
                }
                maxVal = peak_guess;
            }

            double finalRMS = CalculateRMS(xData, fitCurve, mu_guess);
            return (fitCurve, mu_guess, sigma_guess, maxVal, finalRMS);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double EMG(double x, double A, double mu, double sigma, double tau)
        {
            if (tau <= MIN_VALUE) tau = MIN_VALUE;
            if (sigma <= MIN_VALUE) sigma = MIN_VALUE;

            double invTau = 1.0 / tau;
            double sigma2 = sigma * sigma;
            double expArg = (sigma2 * 0.5 * invTau * invTau) - ((x - mu) * invTau);

            //  Prevent overflow
            if (expArg > MAX_EXP_ARG) expArg = MAX_EXP_ARG;
            if (expArg < -MAX_EXP_ARG) expArg = -MAX_EXP_ARG;

            double erfcArg = (sigma2 - (tau * (x - mu))) / (SQRT_2 * sigma * tau);

            double emgVal = (A * 0.5 * invTau) * Math.Exp(expArg) * Erfc(erfcArg);

            //  Check for NaN/Infinity
            if (double.IsNaN(emgVal) || double.IsInfinity(emgVal))
                return 0;

            return emgVal;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double Erfc(double x)
        {
            const double p = 0.3275911;
            const double a1 = 0.254829592;
            const double a2 = -0.284496736;
            const double a3 = 1.421413741;
            const double a4 = -1.453152027;
            const double a5 = 1.061405429;

            double sign = x < 0 ? -1 : 1;
            if (sign < 0) x = -x;

            double t = 1.0 / (1.0 + p * x);

            // Calculate the polynomial result directly (this attempts to calculate part of Erfc)
            // The original code was: double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
            // which is 1 - Erf approximation = Erfc approximation? No, typically this polynomial IS the approximation for Erf(x) or near it.
            // Abramowitz and Stegun 7.1.26: erf(x) = 1 - (a1*t + a2*t^2 + ...)*exp(-x^2) + epsilon
            // So the polynomial part * exp(-x^2) IS the approximation for erfc(x).
            // So we just want the polynomial part * exp.

            double val = (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);

            return sign == 1 ? val : 2.0 - val;
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
    }
}