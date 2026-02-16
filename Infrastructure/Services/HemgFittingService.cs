// Services/HemgFittingService.cs
using System;
using System.Linq;

namespace BaselineMode.WPF.Infrastructure.Services
{
    /// <summary>
    /// HEMG (Hyper-Exponentially Modified Gaussian) Double-Sided Fitting Service.
    /// Improved version with peak detection, Gaussian core, and Levenberg-Marquardt optimizer.
    /// </summary>
    public class HemgFittingService
    {
        private const double MAX_EXP_ARG = 700.0;
        private const double SQRT_2 = 1.41421356237;
        private const double SQRT_2PI = 2.50662827463;

        /// <summary>
        /// Fit data with double-sided Hyper-EMG function (from raw thresholded data).
        /// </summary>
        public (double[] fitCurve, double[] parameters) HemgDoubleSidedFit(double[] thresholdedData)
        {
            try
            {
                var (_, centers, counts) = CreateHistogram(thresholdedData);
                return HemgDoubleSidedFit(centers, counts);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HEMG Fit Error: {ex.Message}");
                return (new double[16384], new double[7]);
            }
        }

        /// <summary>
        /// Fit data with double-sided Hyper-EMG function (from pre-computed histogram).
        /// </summary>
        public (double[] fitCurve, double[] parameters) HemgDoubleSidedFit(double[] binCenters, double[] counts)
        {
            if (binCenters == null || counts == null || binCenters.Length < 10)
                return (new double[binCenters?.Length ?? 0], new double[7]);

            try
            {
                // 1. Peak Detection to get better mu0 and sigma0
                var (mu0, sigma0, height0) = FindPeakParameters(binCenters, counts);

                // 2. Initial Tau and Eta
                double tauL0 = sigma0 * 1.0;
                double tauR0 = sigma0 * 2.0;

                // 3. Amplitude Guess (Probing model for normalization)
                double modelAtPeak = HyperEmgDouble(mu0, 1.0, mu0, sigma0, new[] { tauL0 }, new[] { 0.5 }, new[] { tauR0 }, new[] { 0.5 });
                double A0 = (modelAtPeak > 1e-15) ? height0 / modelAtPeak : height0;

                // [A, mu, sigma, tauL1, tauR1, etaL1, etaR1]
                double[] p0 = { A0, mu0, sigma0, tauL0, tauR0, 0.4, 0.4 };

                // 4. ROI Extraction (±5*sigma around mu)
                int startBin = 0, endBin = binCenters.Length - 1;
                double fitRange = sigma0 * 8;
                for (int i = 0; i < binCenters.Length; i++) if (binCenters[i] >= mu0 - fitRange) { startBin = i; break; }
                for (int i = binCenters.Length - 1; i >= 0; i--) if (binCenters[i] <= mu0 + fitRange) { endBin = i; break; }

                int roiLen = endBin - startBin + 1;
                if (roiLen < 5) return (new double[binCenters.Length], p0);

                double[] xRoi = new double[roiLen];
                double[] yRoi = new double[roiLen];
                for (int i = 0; i < roiLen; i++)
                {
                    xRoi[i] = binCenters[startBin + i];
                    yRoi[i] = counts[startBin + i];
                }

                // 5. Optimization using Levenberg-Marquardt
                double[] pFit = FitLevenbergMarquardt(p0, xRoi, yRoi);

                // 6. Generate result curve
                double[] fitCurve = new double[binCenters.Length];
                double[] tL = { pFit[3] }, eL = { pFit[5] }, tR = { pFit[4] }, eR = { pFit[6] };
                for (int i = 0; i < binCenters.Length; i++)
                {
                    fitCurve[i] = HyperEmgDouble(binCenters[i], pFit[0], pFit[1], pFit[2], tL, eL, tR, eR);
                }

                return (fitCurve, pFit);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HEMG Optimization Error: {ex.Message}");
                return (new double[binCenters.Length], new double[7]);
            }
        }

        private (double mu, double sigma, double height) FindPeakParameters(double[] x, double[] y)
        {
            // Simple Peak Search
            double maxHeight = -1;
            int maxIdx = 0;
            for (int i = 0; i < y.Length; i++)
            {
                if (y[i] > maxHeight) { maxHeight = y[i]; maxIdx = i; }
            }

            double mu = x[maxIdx];

            // Estimate FWHM by searching ± around peak
            double halfMax = maxHeight * 0.5;
            int leftIdx = maxIdx, rightIdx = maxIdx;
            while (leftIdx > 0 && y[leftIdx] > halfMax) leftIdx--;
            while (rightIdx < y.Length - 1 && y[rightIdx] > halfMax) rightIdx++;

            double fwhm = x[rightIdx] - x[leftIdx];
            if (fwhm <= 0) fwhm = (x.Max() - x.Min()) / 100.0;

            double sigma = fwhm / 2.355;
            if (sigma < 1e-3) sigma = 1.0;

            return (mu, sigma, maxHeight);
        }

        private double[] FitLevenbergMarquardt(double[] p0, double[] x, double[] y)
        {
            double[] p = (double[])p0.Clone();
            int n = p.Length;
            int m = x.Length;
            double lambda = 0.01;
            int maxIter = 50;

            double currentError = CalculateError(p, x, y);

            for (int iter = 0; iter < maxIter; iter++)
            {
                double[][] J = CalculateJacobian(p, x);
                double[][] JtJ = new double[n][];
                for (int i = 0; i < n; i++) JtJ[i] = new double[n];
                double[] JtRes = new double[n];

                for (int i = 0; i < m; i++)
                {
                    double res = y[i] - EvaluateModel(p, x[i]);
                    for (int j = 0; j < n; j++)
                    {
                        JtRes[j] += J[i][j] * res;
                        for (int k = 0; k < n; k++)
                        {
                            JtJ[j][k] += J[i][j] * J[i][k];
                        }
                    }
                }

                // Regularization (Damping)
                for (int i = 0; i < n; i++) JtJ[i][i] += lambda * (JtJ[i][i] + 1e-7);

                double[] delta = SolveLinearSystem(JtJ, JtRes);
                if (delta == null) { lambda *= 10; continue; }

                double[] pNew = new double[n];
                for (int i = 0; i < n; i++) pNew[i] = p[i] + delta[i];
                EnforceConstraints(pNew);

                double newError = CalculateError(pNew, x, y);
                if (newError < currentError)
                {
                    lambda /= 10.0;
                    currentError = newError;
                    Array.Copy(pNew, p, n);
                    if (Math.Abs(currentError - newError) < 1e-9) break;
                }
                else
                {
                    lambda *= 10.0;
                }
            }

            return p;
        }

        private void EnforceConstraints(double[] p)
        {
            p[0] = Math.Max(0, p[0]); // A
            p[2] = Math.Max(0.1, p[2]); // Sigma
            p[3] = Math.Max(0.01, p[3]); // TauL
            p[4] = Math.Max(0.01, p[4]); // TauR
            p[5] = Math.Clamp(p[5], 0.01, 0.98); // EtaL
            p[6] = Math.Clamp(p[6], 0.01, 0.98 - p[5]); // EtaR
        }

        private double CalculateError(double[] p, double[] x, double[] y)
        {
            double sum = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double diff = y[i] - EvaluateModel(p, x[i]);
                sum += diff * diff;
            }
            return sum;
        }

        private double EvaluateModel(double[] p, double x)
        {
            double[] tL = { p[3] }, eL = { p[5] }, tR = { p[4] }, eR = { p[6] };
            return HyperEmgDouble(x, p[0], p[1], p[2], tL, eL, tR, eR);
        }

        private double[][] CalculateJacobian(double[] p, double[] x)
        {
            int m = x.Length;
            int n = p.Length;
            double[][] J = new double[m][];
            double eps = 1e-6;

            for (int i = 0; i < m; i++)
            {
                J[i] = new double[n];
                for (int j = 0; j < n; j++)
                {
                    double original = p[j];
                    p[j] = original + eps;
                    double vplus = EvaluateModel(p, x[i]);
                    p[j] = original - eps;
                    double vminus = EvaluateModel(p, x[i]);
                    p[j] = original;
                    J[i][j] = (vplus - vminus) / (2 * eps);
                }
            }
            return J;
        }

        private double[]? SolveLinearSystem(double[][] A, double[] b)
        {
            int n = b.Length;
            // Simple Gaussian Elimination with pivoting
            double[][] M = new double[n][];
            for (int i = 0; i < n; i++)
            {
                M[i] = new double[n + 1];
                for (int j = 0; j < n; j++) M[i][j] = A[i][j];
                M[i][n] = b[i];
            }

            for (int i = 0; i < n; i++)
            {
                int pivot = i;
                for (int j = i + 1; j < n; j++)
                    if (Math.Abs(M[j][i]) > Math.Abs(M[pivot][i])) pivot = j;

                var temp = M[i]; M[i] = M[pivot]; M[pivot] = temp;
                double div = M[i][i];
                if (Math.Abs(div) < 1e-18) return null;

                for (int j = i; j <= n; j++) M[i][j] /= div;
                for (int k = 0; k < n; k++)
                {
                    if (k != i)
                    {
                        double factor = M[k][i];
                        for (int j = i; j <= n; j++) M[k][j] -= factor * M[i][j];
                    }
                }
            }

            double[] x = new double[n];
            for (int i = 0; i < n; i++) x[i] = M[i][n];
            return x;
        }

        private double HyperEmgDouble(double x, double A, double mu, double sigma,
                                     double[] tausLeft, double[] etasLeft,
                                     double[] tausRight, double[] etasRight)
        {
            double y = 0.0;
            double totalEta = 0;

            // Left tails
            for (int i = 0; i < tausLeft.Length; i++)
            {
                double tau = tausLeft[i];
                double eta = etasLeft[i];
                if (tau <= 0) continue;
                totalEta += eta;

                double z = (sigma * sigma / (2 * tau * tau)) - (mu - x) / tau;
                z = Math.Min(z, MAX_EXP_ARG);
                double arg = (sigma / tau - (mu - x) / sigma) / SQRT_2;
                y += eta * (1.0 / (2.0 * tau)) * Math.Exp(z) * Erfc(arg);
            }

            // Right tails
            for (int i = 0; i < tausRight.Length; i++)
            {
                double tau = tausRight[i];
                double eta = etasRight[i];
                if (tau <= 0) continue;
                totalEta += eta;

                double z = (sigma * sigma / (2 * tau * tau)) - (x - mu) / tau;
                z = Math.Min(z, MAX_EXP_ARG);
                double arg = (sigma / tau - (x - mu) / sigma) / SQRT_2;
                y += eta * (1.0 / (2.0 * tau)) * Math.Exp(z) * Erfc(arg);
            }

            // Add Gaussian Core component
            double etaGaussian = 1.0 - totalEta;
            if (etaGaussian > 0)
            {
                double gauss = Math.Exp(-0.5 * Math.Pow((x - mu) / sigma, 2)) / (sigma * SQRT_2PI);
                y += etaGaussian * gauss;
            }

            y *= A;
            return double.IsFinite(y) ? y : 0;
        }

        private double Erfc(double x) => 1.0 - Erf(x);

        private double Erf(double x)
        {
            const double a1 = 0.254829592, a2 = -0.284496736, a3 = 1.421413741, a4 = -1.453152027, a5 = 1.061405429, p = 0.3275911;
            int sign = x < 0 ? -1 : 1; x = Math.Abs(x);
            double t = 1.0 / (1.0 + p * x);
            double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
            return sign * y;
        }

        private (double[] edges, double[] centers, double[] counts) CreateHistogram(double[] data)
        {
            int numBins = 16384;
            double[] edges = new double[numBins + 1];
            double[] centers = new double[numBins];
            double[] counts = new double[numBins];
            for (int i = 0; i <= numBins; i++) edges[i] = i;
            for (int i = 0; i < numBins; i++) centers[i] = edges[i] + 0.5;
            foreach (double val in data)
            {
                int bin = (int)Math.Floor(val);
                if (bin >= 0 && bin < numBins) counts[bin]++;
            }
            return (edges, centers, counts);
        }
    }
}