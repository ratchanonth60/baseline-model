// Services/HemgFittingService.cs
using System;
using System.Linq;

namespace BaselineMode.WPF.Services
{
    /// <summary>
    /// HEMG (Hyper-Exponentially Modified Gaussian) Double-Sided Fitting Service
    /// Converts MATLAB HEMG_DS_fit to C# using native optimization
    /// </summary>
    public class HemgFittingService
    {
        private const double MAX_EXP_ARG = 700.0;  // Prevent overflow in exponential
        private const double SQRT_2 = 1.41421356237;

        /// <summary>
        /// Fit data with double-sided Hyper-EMG function
        /// </summary>
        /// <param name="thresholdedData">1D array of thresholded data (e.g., from histogram)</param>
        /// <returns>Tuple of (fitCurve, parameters) where parameters = [A, mu, sigma, tauL1, tauR1, etaL1, etaR1]</returns>
        public (double[] fitCurve, double[] parameters) HemgDoubleSidedFit(double[] thresholdedData)
        {
            try
            {
                // Create histogram bins from 0 to 16384
                var (edges, centers, counts) = CreateHistogram(thresholdedData);

                // Calculate initial parameters
                double A0 = counts.Max();
                double mu0 = thresholdedData.Average();
                double sigma0 = CalculateStdDev(thresholdedData, mu0);

                // Initial parameter vector: [A, mu, sigma, tauL1, tauR1, etaL1, etaR1]
                double[] p0 = new[] { A0, mu0, sigma0, 0.5, 1.5, 0.5, 0.5 };
                double[] lb = new[] { 0, 0, 0.01, 0.05, 0.05, 0.0, 0.0 };
                double[] ub = new[] { double.PositiveInfinity, thresholdedData.Max(), 50, 5.0, 5.0, 1.0, 1.0 };

                // Define the objective function - sum of squared residuals
                Func<double[], double> objective = (p) =>
                {
                    double residualSum = 0.0;
                    for (int i = 0; i < centers.Length; i++)
                    {
                        double predicted = HyperEmgDouble(centers[i], p[0], p[1], p[2], 
                                                         new[] { p[3] }, new[] { p[5] }, 
                                                         new[] { p[4] }, new[] { p[6] });
                        double residual = counts[i] - predicted;
                        residualSum += residual * residual;
                    }
                    return residualSum;
                };

                // Fit the curve using gradient descent (fallback if Accord's advanced methods unavailable)
                double[] pFit = FitCurveGradientDescent(objective, p0, lb, ub, centers, counts);

                // Ensure parameters are within bounds
                for (int i = 0; i < pFit.Length; i++)
                {
                    pFit[i] = Math.Max(lb[i], Math.Min(ub[i], pFit[i]));
                }

                // Generate fitted curve
                double[] fitCurve = new double[centers.Length];
                for (int i = 0; i < centers.Length; i++)
                {
                    fitCurve[i] = HyperEmgDouble(centers[i], pFit[0], pFit[1], pFit[2],
                                                 new[] { pFit[3] }, new[] { pFit[5] },
                                                 new[] { pFit[4] }, new[] { pFit[6] });
                    if (!double.IsFinite(fitCurve[i]))
                        fitCurve[i] = 0.0;
                }

                return (fitCurve, pFit);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HEMG Fit Error: {ex.Message}");
                return (Array.Empty<double>(), Array.Empty<double>());
            }
        }

        /// <summary>
        /// Fit curve using gradient descent optimization
        /// </summary>
        private double[] FitCurveGradientDescent(Func<double[], double> objective, double[] p0, 
                                                double[] lb, double[] ub, double[] centers, double[] counts)
        {
            double[] p = (double[])p0.Clone();
            double learningRate = 0.01;
            int maxIterations = 200;
            double tolerance = 1e-6;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                double currentError = objective(p);

                // Numerical gradient calculation
                double[] gradient = new double[p.Length];
                double delta = 1e-7;

                for (int j = 0; j < p.Length; j++)
                {
                    double[] pPlus = (double[])p.Clone();
                    pPlus[j] += delta;

                    double errorPlus = objective(pPlus);
                    gradient[j] = (errorPlus - currentError) / delta;
                }

                // Update parameters
                bool updated = false;
                for (int j = 0; j < p.Length; j++)
                {
                    double newP = p[j] - learningRate * gradient[j];
                    newP = Math.Max(lb[j], Math.Min(ub[j], newP));

                    if (Math.Abs(newP - p[j]) > tolerance * Math.Abs(p[j]) + tolerance)
                        updated = true;

                    p[j] = newP;
                }

                if (!updated) break;

                // Check convergence
                double newError = objective(p);
                if (Math.Abs(newError - currentError) < tolerance)
                    break;
            }

            return p;
        }

        /// <summary>
        /// Hyper-EMG Double-Sided function
        /// Computes the PDF value for a given x and parameters
        /// </summary>
        private double HyperEmgDouble(double x, double A, double mu, double sigma,
                                     double[] tausLeft, double[] etasLeft,
                                     double[] tausRight, double[] etasRight)
        {
            double y = 0.0;

            // Left tails (x < mu)
            for (int i = 0; i < tausLeft.Length; i++)
            {
                double tau = tausLeft[i];
                double eta = etasLeft[i];

                if (tau <= 0) continue;

                double sigma2 = sigma * sigma;
                double tau2 = tau * tau;
                double z = (sigma2 / (2 * tau2)) - (mu - x) / tau;
                z = Math.Min(z, MAX_EXP_ARG);

                double arg = (sigma2 / tau - (mu - x)) / (SQRT_2 * sigma);
                double erfc_val = Erfc(arg);

                y += eta * (1.0 / (2.0 * tau)) * Math.Exp(z) * erfc_val;
            }

            // Right tails (x >= mu)
            for (int i = 0; i < tausRight.Length; i++)
            {
                double tau = tausRight[i];
                double eta = etasRight[i];

                if (tau <= 0) continue;

                double sigma2 = sigma * sigma;
                double tau2 = tau * tau;
                double z = (sigma2 / (2 * tau2)) + (x - mu) / tau;
                z = Math.Min(z, MAX_EXP_ARG);

                double arg = (sigma2 / tau + (x - mu)) / (SQRT_2 * sigma);
                double erfc_val = Erfc(arg);

                y += eta * (1.0 / (2.0 * tau)) * Math.Exp(z) * erfc_val;
            }

            y = A * y;

            // Ensure no NaN or Infinity values
            if (!double.IsFinite(y))
                y = 0.0;

            return y;
        }

        /// <summary>
        /// Complementary error function (erfc) approximation
        /// erfc(x) = 1 - erf(x)
        /// </summary>
        private double Erfc(double x)
        {
            return 1.0 - Erf(x);
        }

        /// <summary>
        /// Error function (erf) approximation using Abramowitz and Stegun formula
        /// </summary>
        private double Erf(double x)
        {
            const double a1 = 0.254829592;
            const double a2 = -0.284496736;
            const double a3 = 1.421413741;
            const double a4 = -1.453152027;
            const double a5 = 1.061405429;
            const double p = 0.3275911;

            int sign = x < 0 ? -1 : 1;
            x = Math.Abs(x);

            double t = 1.0 / (1.0 + p * x);
            double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);

            return sign * y;
        }

        /// <summary>
        /// Create histogram from data with 16384 bins from 0 to 16384
        /// </summary>
        private (double[] edges, double[] centers, double[] counts) CreateHistogram(double[] data)
        {
            int numBins = 16384;
            double[] edges = new double[numBins + 1];
            double[] centers = new double[numBins];
            int[] counts_int = new int[numBins];

            // Create bin edges from 0 to 16384
            for (int i = 0; i <= numBins; i++)
            {
                edges[i] = i;
            }

            // Calculate bin centers
            for (int i = 0; i < numBins; i++)
            {
                centers[i] = edges[i] + (edges[i + 1] - edges[i]) / 2.0;
            }

            // Bin the data
            foreach (double value in data)
            {
                int bin = (int)Math.Floor(value);
                if (bin >= 0 && bin < numBins)
                {
                    counts_int[bin]++;
                }
            }

            // Convert to double array
            double[] counts = new double[numBins];
            for (int i = 0; i < numBins; i++)
            {
                counts[i] = counts_int[i];
            }

            return (edges, centers, counts);
        }

        /// <summary>
        /// Calculate standard deviation
        /// </summary>
        private double CalculateStdDev(double[] data, double mean)
        {
            if (data.Length <= 1) return 0;

            double sumSquaredDiff = 0;
            foreach (double value in data)
            {
                double diff = value - mean;
                sumSquaredDiff += diff * diff;
            }

            return Math.Sqrt(sumSquaredDiff / (data.Length - 1));
        }
    }
}
