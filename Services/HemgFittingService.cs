// Services/HemgFittingService.cs
using System;
using System.Linq;

namespace BaselineMode.WPF.Services
{
    /// <summary>
    /// HEMG (Hyper-Exponentially Modified Gaussian) Double-Sided Fitting Service
    /// Converts MATLAB HEMG_DS_fit to C# using Accord.NET for optimization
    /// </summary>
    public class HemgFittingService
    {
        private const double MAX_EXP_ARG = 700.0;  // Prevent overflow in exponential
        private const double SQRT_2 = 1.41421356237;

        /// <summary>
        /// Fit data with double-sided Hyper-EMG function (from raw thresholded data)
        /// Creates histogram internally - fitCurve length = 16384
        /// </summary>
        public (double[] fitCurve, double[] parameters) HemgDoubleSidedFit(double[] thresholdedData)
        {
            try
            {
                var (edges, centers, counts) = CreateHistogram(thresholdedData);
                return HemgDoubleSidedFit(centers, counts);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HEMG Fit Error: {ex.Message}");
                return (Array.Empty<double>(), Array.Empty<double>());
            }
        }

        /// <summary>
        /// Fit data with double-sided Hyper-EMG function (from pre-computed histogram)
        /// fitCurve length matches binCenters length
        /// </summary>
        public (double[] fitCurve, double[] parameters) HemgDoubleSidedFit(double[] binCenters, double[] counts)
        {
            try
            {
                // Calculate initial parameters from histogram
                double A0 = counts.Max();

                // Weighted mean from histogram
                double totalWeight = 0, weightedSum = 0;
                for (int i = 0; i < binCenters.Length; i++)
                {
                    totalWeight += counts[i];
                    weightedSum += counts[i] * binCenters[i];
                }
                double mu0 = totalWeight > 0 ? weightedSum / totalWeight : binCenters[binCenters.Length / 2];

                // Weighted sigma from histogram
                double weightedSqSum = 0;
                for (int i = 0; i < binCenters.Length; i++)
                {
                    double diff = binCenters[i] - mu0;
                    weightedSqSum += counts[i] * diff * diff;
                }
                double sigma0 = totalWeight > 0 ? Math.Sqrt(weightedSqSum / totalWeight) : 1.0;
                if (sigma0 < 0.01) sigma0 = 1.0;

                // tau must be in the SAME units as sigma (ADC channels)
                // Typical: tauL ~ 0.3*sigma to 2*sigma
                double tauL0 = sigma0 * 0.5;
                double tauR0 = sigma0 * 1.5;

                // Initial parameter vector: [A, mu, sigma, tauL1, tauR1, etaL1, etaR1]
                double[] p0 = new[] { A0, mu0, sigma0, tauL0, tauR0, 0.5, 0.5 };
                double[] lb = new[] { 0.0, binCenters[0], sigma0 * 0.1, sigma0 * 0.05, sigma0 * 0.05, 0.01, 0.01 };
                double[] ub = new[] { A0 * 3.0, binCenters[binCenters.Length - 1], sigma0 * 5, sigma0 * 10, sigma0 * 10, 1.0, 1.0 };

                // Pre-allocate tau/eta arrays to avoid allocation per evaluation
                double[] tauL = new double[1];
                double[] etaL = new double[1];
                double[] tauR = new double[1];
                double[] etaR = new double[1];

                // Only fit on bins near the peak (within ±5σ of mu) for performance & stability
                int startBin = 0, endBin = binCenters.Length;
                double fitWindow = sigma0 * 5;
                for (int i = 0; i < binCenters.Length; i++)
                {
                    if (binCenters[i] >= mu0 - fitWindow) { startBin = i; break; }
                }
                for (int i = binCenters.Length - 1; i >= 0; i--)
                {
                    if (binCenters[i] <= mu0 + fitWindow) { endBin = i + 1; break; }
                }

                // Define the objective function - sum of squared residuals (normalized)
                Func<double[], double> objective = (p) =>
                {
                    tauL[0] = p[3]; etaL[0] = p[5];
                    tauR[0] = p[4]; etaR[0] = p[6];
                    double residualSum = 0.0;
                    for (int i = startBin; i < endBin; i++)
                    {
                        if (counts[i] <= 0) continue;
                        double predicted = HyperEmgDouble(binCenters[i], p[0], p[1], p[2], 
                                                         tauL, etaL, tauR, etaR);
                        double residual = counts[i] - predicted;
                        residualSum += residual * residual;
                    }
                    return residualSum;
                };

                // Fit the curve using gradient descent
                double[] pFit = FitCurveGradientDescent(objective, p0, lb, ub, binCenters, counts);

                // Ensure parameters are within bounds
                for (int i = 0; i < pFit.Length; i++)
                {
                    pFit[i] = Math.Max(lb[i], Math.Min(ub[i], pFit[i]));
                }

                // Generate fitted curve on the SAME binCenters as input
                double[] fitCurve = new double[binCenters.Length];
                tauL[0] = pFit[3]; etaL[0] = pFit[5];
                tauR[0] = pFit[4]; etaR[0] = pFit[6];
                for (int i = 0; i < binCenters.Length; i++)
                {
                    fitCurve[i] = HyperEmgDouble(binCenters[i], pFit[0], pFit[1], pFit[2],
                                                 tauL, etaL, tauR, etaR);
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
        /// Fit curve using adaptive gradient descent optimization
        /// </summary>
        private double[] FitCurveGradientDescent(Func<double[], double> objective, double[] p0, 
                                                double[] lb, double[] ub, double[] centers, double[] counts)
        {
            double[] p = (double[])p0.Clone();
            int maxIterations = 500;
            double tolerance = 1e-8;

            // Adaptive step size per parameter (scaled to parameter magnitude)
            double[] stepSize = new double[p.Length];
            for (int j = 0; j < p.Length; j++)
            {
                stepSize[j] = Math.Max(Math.Abs(p[j]) * 0.001, 1e-6);
            }

            double bestError = objective(p);
            double[] bestP = (double[])p.Clone();

            for (int iter = 0; iter < maxIterations; iter++)
            {
                double currentError = bestError;

                // Numerical gradient with adaptive delta
                double[] gradient = new double[p.Length];

                for (int j = 0; j < p.Length; j++)
                {
                    double delta = Math.Max(Math.Abs(p[j]) * 1e-5, 1e-8);

                    double[] pPlus = (double[])p.Clone();
                    double[] pMinus = (double[])p.Clone();
                    pPlus[j] = Math.Min(p[j] + delta, ub[j]);
                    pMinus[j] = Math.Max(p[j] - delta, lb[j]);

                    // Central difference for better accuracy
                    double errorPlus = objective(pPlus);
                    double errorMinus = objective(pMinus);
                    gradient[j] = (errorPlus - errorMinus) / (pPlus[j] - pMinus[j]);
                }

                // Line search: try step, halve if error increases
                bool anyImproved = false;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    double[] pNew = new double[p.Length];
                    for (int j = 0; j < p.Length; j++)
                    {
                        double step = stepSize[j] / (1.0 + attempt);
                        pNew[j] = p[j] - step * Math.Sign(gradient[j]);
                        pNew[j] = Math.Max(lb[j], Math.Min(ub[j], pNew[j]));
                    }

                    double newError = objective(pNew);
                    if (newError < bestError)
                    {
                        bestError = newError;
                        Array.Copy(pNew, bestP, p.Length);
                        Array.Copy(pNew, p, p.Length);

                        // Increase step size slightly
                        for (int j = 0; j < p.Length; j++)
                            stepSize[j] *= 1.1;

                        anyImproved = true;
                        break;
                    }
                }

                if (!anyImproved)
                {
                    // Shrink step sizes
                    for (int j = 0; j < p.Length; j++)
                        stepSize[j] *= 0.5;

                    // Check if step sizes are too small
                    bool allTiny = true;
                    for (int j = 0; j < p.Length; j++)
                    {
                        if (stepSize[j] > tolerance * (Math.Abs(p[j]) + tolerance))
                        { allTiny = false; break; }
                    }
                    if (allTiny) break;
                }

                // Convergence check
                if (currentError > 0 && Math.Abs(bestError - currentError) / currentError < tolerance)
                    break;
            }

            return bestP;
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

                // erfc argument for left tail: (sigma/tau - (mu-x)/sigma) / √2
                double arg = (sigma / tau - (mu - x) / sigma) / SQRT_2;
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
                double z = (sigma2 / (2 * tau2)) - (x - mu) / tau;
                z = Math.Min(z, MAX_EXP_ARG);

                // erfc argument for right tail: (sigma/tau - (x-mu)/sigma) / √2
                double arg = (sigma / tau - (x - mu) / sigma) / SQRT_2;
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
