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

        // ✅ SAFE: Reusable ArrayPool
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
        /// ✅ SAFE MEMORY: Uses ArrayPool for all temporary allocations
        /// </summary>
        public (double[] fitCurve, double mu, double sigma, double peak, double rms) GaussianFit(
            double[] xData, double[] yData)
        {
            var (mu_guess, sigma_guess, peak_guess) = CalculateMoments(xData, yData);

            if (peak_guess <= 0 || sigma_guess <= 0)
            {
                return (new double[xData.Length], mu_guess, sigma_guess, peak_guess, 0);
            }

            double[] p = { peak_guess, mu_guess, sigma_guess };
            int maxIter = 100;
            double lambda = 0.001;
            double tolerance = 1e-6;
            int length = xData.Length;

            // ✅ SAFE: Rent from ArrayPool instead of direct allocation
            double[] residuals = _doublePool.Rent(length);
            double[] JtR = _doublePool.Rent(3);
            double[][] J = _jaggedPool.Rent(length);
            double[][] JtJ = _jaggedPool.Rent(3);

            try
            {
                // Initialize jagged arrays
                for (int i = 0; i < length; i++)
                    J[i] = _doublePool.Rent(3);

                for (int i = 0; i < 3; i++)
                    JtJ[i] = _doublePool.Rent(3);

                for (int iter = 0; iter < maxIter; iter++)
                {
                    // Clear JtJ and JtR for reuse
                    Array.Clear(JtR, 0, 3);
                    for (int i = 0; i < 3; i++)
                        Array.Clear(JtJ[i], 0, 3);

                    double A = p[0];
                    double mu = p[1];
                    double sigma = p[2];
                    double sigma2 = sigma * sigma;
                    double sigma3 = sigma2 * sigma;

                    // Calculate residuals and Jacobian
                    for (int i = 0; i < length; i++)
                    {
                        double x = xData[i];
                        double diff = x - mu;
                        double expTerm = Math.Exp(-0.5 * diff * diff / sigma2);
                        double f = A * expTerm;

                        residuals[i] = yData[i] - f;

                        J[i][0] = expTerm;
                        J[i][1] = f * diff / sigma2;
                        J[i][2] = f * diff * diff / sigma3;
                    }

                    // Compute JtJ and JtR in one pass
                    for (int i = 0; i < length; i++)
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            double Jik = J[i][k];
                            JtR[k] += Jik * residuals[i];
                            for (int j = 0; j < 3; j++)
                            {
                                JtJ[k][j] += Jik * J[i][j];
                            }
                        }
                    }

                    // Add damping
                    for (int k = 0; k < 3; k++)
                        JtJ[k][k] += lambda;

                    try
                    {
                        double[] delta = SolveLinearSystem3x3(JtJ, JtR);

                        p[0] += delta[0];
                        p[1] += delta[1];
                        p[2] += delta[2];

                        if (Math.Abs(delta[0]) < tolerance &&
                            Math.Abs(delta[1]) < tolerance &&
                            Math.Abs(delta[2]) < tolerance)
                            break;
                    }
                    catch
                    {
                        break;
                    }
                }

                // Generate fit curve
                double[] fitCurve = new double[length];
                double sigma2Final = p[2] * p[2];
                for (int i = 0; i < length; i++)
                {
                    double diff = xData[i] - p[1];
                    fitCurve[i] = p[0] * Math.Exp(-0.5 * diff * diff / sigma2Final);
                }

                double finalRMS = CalculateRMS(xData, fitCurve, p[1]);
                return (fitCurve, p[1], p[2], p[0], finalRMS);
            }
            finally
            {
                // ✅ CRITICAL: Always return all rented arrays
                _doublePool.Return(residuals);
                _doublePool.Return(JtR);

                for (int i = 0; i < length; i++)
                {
                    if (J[i] != null)
                        _doublePool.Return(J[i]);
                }
                _jaggedPool.Return(J);

                for (int i = 0; i < 3; i++)
                {
                    if (JtJ[i] != null)
                        _doublePool.Return(JtJ[i]);
                }
                _jaggedPool.Return(JtJ);
            }
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
        /// ✅ SAFE MEMORY: Uses ArrayPool for all temporary allocations
        /// </summary>
        public (double[] fitCurve, double mu, double sigma, double peak, double rms) HyperEMGFit(
            double[] xData, double[] yData)
        {
            var (mu_guess, sigma_guess, peak_guess) = CalculateMoments(xData, yData);

            if (peak_guess <= 0 || sigma_guess <= 0)
            {
                return (new double[xData.Length], mu_guess, sigma_guess, peak_guess, 0);
            }

            double A_guess = peak_guess * sigma_guess * SQRT_2PI;
            double tau_guess = sigma_guess * 0.8;
            double[] p = { A_guess, mu_guess, sigma_guess, tau_guess };

            int maxIter = 100;
            double lambda = 0.001;
            double tolerance = 1e-6;
            int length = xData.Length;
            const int nParams = 4;

            // ✅ SAFE: Rent from ArrayPool
            double[] residuals = _doublePool.Rent(length);
            double[] JtR = _doublePool.Rent(nParams);
            double[] p_step = _doublePool.Rent(nParams);
            double[][] J = _jaggedPool.Rent(length);
            double[][] JtJ = _jaggedPool.Rent(nParams);

            try
            {
                // Initialize jagged arrays
                for (int i = 0; i < length; i++)
                    J[i] = _doublePool.Rent(nParams);

                for (int i = 0; i < nParams; i++)
                    JtJ[i] = _doublePool.Rent(nParams);

                const double epsilon = 1e-5;

                for (int iter = 0; iter < maxIter; iter++)
                {
                    Array.Clear(JtR, 0, nParams);
                    for (int i = 0; i < nParams; i++)
                        Array.Clear(JtJ[i], 0, nParams);

                    for (int i = 0; i < length; i++)
                    {
                        double x = xData[i];
                        double val = EMG(x, p[0], p[1], p[2], p[3]);
                        residuals[i] = yData[i] - val;

                        // Numerical Jacobian
                        for (int k = 0; k < nParams; k++)
                        {
                            Array.Copy(p, p_step, nParams);
                            p_step[k] += epsilon;
                            double val_step = EMG(x, p_step[0], p_step[1], p_step[2], p_step[3]);
                            J[i][k] = (val_step - val) / epsilon;
                        }
                    }

                    // Compute JtJ and JtR
                    for (int i = 0; i < length; i++)
                    {
                        for (int k = 0; k < nParams; k++)
                        {
                            double Jik = J[i][k];
                            JtR[k] += Jik * residuals[i];
                            for (int j = 0; j < nParams; j++)
                            {
                                JtJ[k][j] += Jik * J[i][j];
                            }
                        }
                    }

                    // Add damping
                    for (int k = 0; k < nParams; k++)
                        JtJ[k][k] += lambda;

                    try
                    {
                        double[] delta = SolveLinearSystem(JtJ, JtR, nParams);

                        for (int k = 0; k < nParams; k++)
                            p[k] += delta[k];

                        // Constraints
                        if (p[0] < MIN_VALUE) p[0] = MIN_VALUE;
                        if (p[2] < MIN_VALUE) p[2] = MIN_VALUE;
                        if (p[3] < MIN_VALUE) p[3] = MIN_VALUE;

                        if (Math.Abs(delta[0]) < tolerance && Math.Abs(delta[1]) < tolerance)
                            break;
                    }
                    catch { break; }
                }

                // Generate fit curve
                double[] fitCurve = new double[length];
                double maxVal = 0;
                for (int i = 0; i < length; i++)
                {
                    fitCurve[i] = EMG(xData[i], p[0], p[1], p[2], p[3]);
                    if (fitCurve[i] > maxVal) maxVal = fitCurve[i];
                }

                double finalRMS = CalculateRMS(xData, fitCurve, p[1]);
                return (fitCurve, p[1], p[2], maxVal, finalRMS);
            }
            finally
            {
                // ✅ CRITICAL: Always return all rented arrays
                _doublePool.Return(residuals);
                _doublePool.Return(JtR);
                _doublePool.Return(p_step);

                for (int i = 0; i < length; i++)
                {
                    if (J[i] != null)
                        _doublePool.Return(J[i]);
                }
                _jaggedPool.Return(J);

                for (int i = 0; i < nParams; i++)
                {
                    if (JtJ[i] != null)
                        _doublePool.Return(JtJ[i]);
                }
                _jaggedPool.Return(JtJ);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double EMG(double x, double A, double mu, double sigma, double tau)
        {
            if (tau <= MIN_VALUE) tau = MIN_VALUE;
            if (sigma <= MIN_VALUE) sigma = MIN_VALUE;

            double invTau = 1.0 / tau;
            double sigma2 = sigma * sigma;
            double expArg = (sigma2 * 0.5 * invTau * invTau) - ((x - mu) * invTau);

            if (expArg > MAX_EXP_ARG) expArg = MAX_EXP_ARG;

            double erfcArg = (sigma2 - (tau * (x - mu))) / (SQRT_2 * sigma * tau);
            return (A * 0.5 * invTau) * Math.Exp(expArg) * Erfc(erfcArg);
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
            double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);

            return sign == 1 ? y : 2.0 - y;
        }

        /// <summary>
        /// Generic solver for larger systems using Gaussian elimination
        /// ✅ SAFE MEMORY: Uses ArrayPool for temporary allocations
        /// </summary>
        private double[] SolveLinearSystem(double[][] A, double[] b, int n)
        {
            // Use optimized 3x3 solver if applicable
            if (n == 3)
                return SolveLinearSystem3x3(A, b);

            // ✅ SAFE: Rent working matrix from pool
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
                // ✅ CRITICAL: Return all rented arrays
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