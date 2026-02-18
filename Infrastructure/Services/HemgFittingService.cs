using System;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using BaselineMode.WPF.Core.Interfaces;

namespace BaselineMode.WPF.Infrastructure.Services
{
    public class HemgFittingService(ILoggerService loggerService) : IHemgFittingService
    {
        // Constants
        private const double MAX_EXP_ARG = 700.0;
        private const double SQRT_2 = 1.414213562373095;
        private const double SQRT_2PI = 2.506628274631000;
        private const double INV_SQRT_2 = 0.7071067811865475;
        private const double INV_SQRT_2PI = 0.3989422804014327;

        private readonly ILoggerService _logger = loggerService ?? throw new ArgumentNullException(nameof(loggerService));

        public double CalculateMaxHeight(double[] p)
        {
            if (p == null || p.Length < 7) return 0;
            // p = [A, mu, sigma, tauL, tauR, etaL, etaR]
            // Call Kernel at peak (x = mu) to find real height
            return HyperEmgKernel(p[1], p[0], p[1], p[2], p[3], p[4], p[5], p[6]);
        }

        public (double[] fitCurve, double[] parameters) HemgDoubleSidedFit(double[] thresholdedData)
        {
            // Default config for backward compatibility or overload
            return HemgDoubleSidedFit(thresholdedData, new BaselineMode.WPF.Core.Models.Baseline.HemgFitConfig());
        }

        public (double[] fitCurve, double[] parameters) HemgDoubleSidedFit(double[] thresholdedData, BaselineMode.WPF.Core.Models.Baseline.HemgFitConfig config)
        {
            try
            {
                var (_, centers, counts) = CreateHistogram(thresholdedData, config.HistogramBinCount);
                return HemgDoubleSidedFit(centers, counts, null, null, config);
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "HEMG Fit Error (Raw Data)");
                return (new double[config.HistogramBinCount], new double[7]);
            }
        }

        public double[] GenerateHemgCurve(double[] x, double[] p)
        {
            if (x == null || p == null || p.Length < 7) return [];
            double[] y = new double[x.Length];
            Parallel.For(0, x.Length, i =>
            {
                y[i] = HyperEmgKernel(x[i], p[0], p[1], p[2], p[3], p[4], p[5], p[6]);
            });
            return y;
        }

        public (double[] fitCurve, double[] parameters) HemgDoubleSidedFit(double[] binCenters, double[] counts)
        {
            return HemgDoubleSidedFit(binCenters, counts, null, null, null);
        }

        public (double[] fitCurve, double[] parameters) HemgDoubleSidedFit(double[] binCenters, double[] counts, double[]? initialGuess = null, bool[]? fixedParams = null, BaselineMode.WPF.Core.Models.Baseline.HemgFitConfig? config = null)
        {
            if (binCenters == null || counts == null || binCenters.Length < 10)
                return (new double[binCenters?.Length ?? 0], new double[7]);

            config ??= new BaselineMode.WPF.Core.Models.Baseline.HemgFitConfig();

            try
            {
                double[] p0;
                if (initialGuess != null && initialGuess.Length >= 7)
                {
                    p0 = (double[])initialGuess.Clone();
                }
                else
                {
                    // 1. Peak Detection
                    var (mu0, sigma0, height0) = FindPeakParameters(binCenters, counts);

                    // 2. Initial Guess
                    double tauL0 = sigma0 * 0.5;
                    double tauR0 = sigma0 * 0.5;
                    double modelAtPeak = HyperEmgKernel(mu0, 1.0, mu0, sigma0, tauL0, tauR0, 0.05, 0.05);
                    double A0 = (modelAtPeak > 1e-15) ? height0 / modelAtPeak : height0;
                    p0 = [A0, mu0, sigma0, tauL0, tauR0, 0.05, 0.05];
                }

                // 3. ROI Extraction
                int startBin = 0, endBin = binCenters.Length - 1;
                double fitRange = p0[2] * 8;
                double minX = p0[1] - fitRange;
                double maxX = p0[1] + fitRange;

                for (int i = 0; i < binCenters.Length; i++) { if (binCenters[i] >= minX) { startBin = i; break; } }
                for (int i = binCenters.Length - 1; i >= 0; i--) { if (binCenters[i] <= maxX) { endBin = i; break; } }

                int roiLen = endBin - startBin + 1;
                if (roiLen < 5) return (GenerateHemgCurve(binCenters, p0), p0);

                double[] xRoi = new double[roiLen];
                double[] yRoi = new double[roiLen];
                Array.Copy(binCenters, startBin, xRoi, 0, roiLen);
                Array.Copy(counts, startBin, yRoi, 0, roiLen);

                // 4. Optimization (Fast Levenberg-Marquardt with Locks)
                double[] pFit = FitLevenbergMarquardtParallel(p0, xRoi, yRoi, fixedParams, config);

                // 5. Generate Curve
                double[] fitCurve = GenerateHemgCurve(binCenters, pFit);

                return (fitCurve, pFit);
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, "HEMG Optimization Error");
                return (new double[binCenters.Length], new double[7]);
            }
        }

        private static double[] FitLevenbergMarquardtParallel(double[] p0, double[] x, double[] y, bool[]? fixedParams, BaselineMode.WPF.Core.Models.Baseline.HemgFitConfig config)
        {
            int n = p0.Length; // 7 parameters
            int m = x.Length;
            double[] p = (double[])p0.Clone();
            bool[] locks = fixedParams ?? new bool[n];

            // Pre-allocate buffers
            double[] J_flat = new double[m * n];
            double[] residuals = new double[m];
            double[] pNew = new double[n];
            double[] JtJ = new double[n * n];
            double[] JtRes = new double[n];
            double[] delta = new double[n];

            double lambda = config.Lambda;
            int maxIter = config.MaxIterations;

            double currentError = CalculateErrorParallel(p, x, y, residuals);

            for (int iter = 0; iter < maxIter; iter++)
            {
                // 1. Calculate Jacobian (Parallel) with Locks
                CalculateJacobianParallel(p, x, y, residuals, J_flat, locks);

                // 2. Compute JtJ and JtRes (Matrix Multiplication)
                Array.Clear(JtJ, 0, JtJ.Length);
                Array.Clear(JtRes, 0, JtRes.Length);

                for (int i = 0; i < m; i++)
                {
                    double res = residuals[i];
                    int rowOffset = i * n;

                    for (int j = 0; j < n; j++)
                    {
                        if (locks[j]) continue;
                        double valJ = J_flat[rowOffset + j];
                        JtRes[j] += valJ * res;

                        for (int k = j; k < n; k++)
                        {
                            if (locks[k]) continue;
                            JtJ[j * n + k] += valJ * J_flat[rowOffset + k];
                        }
                    }
                }

                for (int j = 0; j < n; j++)
                    for (int k = 0; k < j; k++)
                        JtJ[j * n + k] = JtJ[k * n + j];

                double[] diagBackup = new double[n];
                for (int i = 0; i < n; i++)
                {
                    if (locks[i])
                    {
                        JtJ[i * n + i] = 1.0;
                        JtRes[i] = 0.0;
                    }
                    else
                    {
                        diagBackup[i] = JtJ[i * n + i];
                        JtJ[i * n + i] += lambda * (diagBackup[i] + 1e-5);
                    }
                }

                if (SolveLinearSystemGaussian(JtJ, JtRes, delta, n))
                {
                    for (int i = 0; i < n; i++) pNew[i] = !locks[i] ? p[i] + delta[i] : p[i];
                    EnforceConstraints(pNew);
                    for (int i = 0; i < n; i++) if (locks[i]) pNew[i] = p[i];

                    double newError = CalculateErrorParallel(pNew, x, y, null);

                    if (newError < currentError)
                    {
                        lambda /= 10.0;
                        currentError = newError;
                        Array.Copy(pNew, p, n);
                        if (Math.Abs(currentError - newError) < 1e-6 * currentError) break;
                    }
                    else
                    {
                        lambda *= 10.0;
                    }
                }
                else
                {
                    lambda *= 10.0;
                }

                if (lambda > 1e10) break;
            }

            return p;
        }

        private static void CalculateJacobianParallel(double[] p, double[] x, double[] y, double[] preCalcResiduals, double[] J_flat, bool[] locks)
        {
            // Implementation unchanged ...
            int n = p.Length;
            int m = x.Length;
            double eps = 1e-5;

            Parallel.For(0, m, i =>
            {
                double xi = x[i];
                double modelBase = y[i] - preCalcResiduals[i];

                if (!locks[0]) J_flat[i * n + 0] = (HyperEmgKernel(xi, p[0] + eps, p[1], p[2], p[3], p[4], p[5], p[6]) - modelBase) / eps; else J_flat[i * n + 0] = 0;
                if (!locks[1]) J_flat[i * n + 1] = (HyperEmgKernel(xi, p[0], p[1] + eps, p[2], p[3], p[4], p[5], p[6]) - modelBase) / eps; else J_flat[i * n + 1] = 0;
                if (!locks[2]) J_flat[i * n + 2] = (HyperEmgKernel(xi, p[0], p[1], p[2] + eps, p[3], p[4], p[5], p[6]) - modelBase) / eps; else J_flat[i * n + 2] = 0;
                if (!locks[3]) J_flat[i * n + 3] = (HyperEmgKernel(xi, p[0], p[1], p[2], p[3] + eps, p[4], p[5], p[6]) - modelBase) / eps; else J_flat[i * n + 3] = 0;
                if (!locks[4]) J_flat[i * n + 4] = (HyperEmgKernel(xi, p[0], p[1], p[2], p[3], p[4] + eps, p[5], p[6]) - modelBase) / eps; else J_flat[i * n + 4] = 0;
                if (!locks[5]) J_flat[i * n + 5] = (HyperEmgKernel(xi, p[0], p[1], p[2], p[3], p[4], p[5] + eps, p[6]) - modelBase) / eps; else J_flat[i * n + 5] = 0;
                if (!locks[6]) J_flat[i * n + 6] = (HyperEmgKernel(xi, p[0], p[1], p[2], p[3], p[4], p[5], p[6] + eps) - modelBase) / eps; else J_flat[i * n + 6] = 0;
            });
        }

        private static double CalculateErrorParallel(double[] p, double[] x, double[] y, double[]? residualsOut)
        {
            double sumError = 0;
            object lockObj = new object();

            // ใช้ Partitioner เพื่อลด Overhead ของ Parallel loop
            var partitioner = Partitioner.Create(0, x.Length);

            Parallel.ForEach(partitioner, (range) =>
            {
                double localSum = 0;
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    double model = HyperEmgKernel(x[i], p[0], p[1], p[2], p[3], p[4], p[5], p[6]);
                    double diff = y[i] - model;
                    localSum += diff * diff;
                    if (residualsOut != null) residualsOut[i] = diff;
                }
                lock (lockObj) sumError += localSum;
            });

            return sumError;
        }

        // Optimized Gaussian Elimination (No allocation, Flat arrays)
        private static bool SolveLinearSystemGaussian(double[] A, double[] b, double[] x, int n)
        {
            // Copy A/b to temp buffers because Solver destroys them
            // Since n is small (7), stackalloc is risky in C# w/o unsafe, using pooled array or just new is fine since its once per iter.
            // But let's assume we copy to a working buffer inside A (destructive) if we didn't need A later. 
            // Here we need A for next damping trial? No, we rebuilt A.
            // Let's create a local copy to be safe.

            double[] M = (double[])A.Clone(); // n*n
            Array.Copy(b, x, n); // Copy b into x, x will become the solution

            for (int k = 0; k < n; k++)
            {
                // Pivot
                int pivot = k;
                double maxVal = Math.Abs(M[k * n + k]);
                for (int i = k + 1; i < n; i++)
                {
                    if (Math.Abs(M[i * n + k]) > maxVal)
                    {
                        maxVal = Math.Abs(M[i * n + k]);
                        pivot = i;
                    }
                }

                // Swap rows
                if (pivot != k)
                {
                    for (int j = k; j < n; j++)
                    {
                        double tmp = M[k * n + j]; M[k * n + j] = M[pivot * n + j]; M[pivot * n + j] = tmp;
                    }
                    double tmpB = x[k]; x[k] = x[pivot]; x[pivot] = tmpB;
                }

                if (Math.Abs(M[k * n + k]) < 1e-20) return false; // Singular

                for (int i = k + 1; i < n; i++)
                {
                    double factor = M[i * n + k] / M[k * n + k];
                    x[i] -= factor * x[k];
                    for (int j = k; j < n; j++) M[i * n + j] -= factor * M[k * n + j];
                }
            }

            // Back substitution
            for (int i = n - 1; i >= 0; i--)
            {
                double sum = 0;
                for (int j = i + 1; j < n; j++) sum += M[i * n + j] * x[j];
                x[i] = (x[i] - sum) / M[i * n + i];
            }
            return true;
        }

        // The Critical Hot Path: Hardcoded for 1 Left, 1 Right
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double HyperEmgKernel(double x, double A, double mu, double sigma,
                                      double tauL, double tauR, double etaL, double etaR)
        {
            double totalY = 0.0;
            double etaSum = 0.0;
            double diff = x - mu;
            double sigmaSq = sigma * sigma;

            // Left Tail
            if (tauL > 1e-9)
            {
                etaSum += etaL;
                double invTau = 1.0 / tauL;
                // z = (sigma^2 / (2*tau^2)) - (mu - x) / tau
                // Note: (mu - x) = -diff
                double z = (sigmaSq * 0.5 * invTau * invTau) + (diff * invTau);
                if (z < MAX_EXP_ARG)
                {
                    // arg = (sigma/tau - (mu-x)/sigma) / sqrt(2)
                    double arg = (sigma * invTau + diff / sigma) * INV_SQRT_2;
                    // eta * (1/2tau) * exp(z) * erfc(arg)
                    totalY += etaL * (0.5 * invTau) * Math.Exp(z) * ErfcFast(arg);
                }
            }

            // Right Tail
            if (tauR > 1e-9)
            {
                etaSum += etaR;
                double invTau = 1.0 / tauR;
                // z = (sigma^2 / (2*tau^2)) - (x - mu) / tau
                double z = (sigmaSq * 0.5 * invTau * invTau) - (diff * invTau);
                if (z < MAX_EXP_ARG)
                {
                    double arg = (sigma * invTau - diff / sigma) * INV_SQRT_2;
                    totalY += etaR * (0.5 * invTau) * Math.Exp(z) * ErfcFast(arg);
                }
            }

            // Gaussian Core
            double etaG = 1.0 - etaSum;
            if (etaG > 0)
            {
                // exp(-0.5 * ((x-mu)/sigma)^2) / (sigma * sqrt(2pi))
                double r = diff / sigma;
                totalY += etaG * Math.Exp(-0.5 * r * r) / (sigma * SQRT_2PI);
            }

            return totalY * A;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double ErfcFast(double x)
        {
            // Complementary Error Function
            // return 1.0 - Erf(x);
            // Abramowitz and Stegun approximation 
            // |error| <= 1.5e-7
            if (x < 0) return 2.0 - ErfcFast(-x);

            double t = 1.0 / (1.0 + 0.3275911 * x);
            double poly = ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t;
            return poly * Math.Exp(-x * x);
        }

        private static void EnforceConstraints(double[] p)
        {
            if (p[0] < 0) p[0] = 1e-3; // A
            if (p[2] < 1e-3) p[2] = 1e-3; // Sigma
            if (p[3] < 1e-3) p[3] = 1e-3; // TauL
            if (p[4] < 1e-3) p[4] = 1e-3; // TauR
            p[5] = Math.Clamp(p[5], 1e-6, 0.95); // EtaL - allow very small tail
            double remaining = 0.99 - p[5];
            p[6] = Math.Clamp(p[6], 1e-6, remaining); // EtaR
        }

        // Helper to keep peak detection functional
        private static (double mu, double sigma, double height) FindPeakParameters(double[] x, double[] y)
        {
            double maxHeight = -1;
            int maxIdx = 0;
            for (int i = 0; i < y.Length; i++) { if (y[i] > maxHeight) { maxHeight = y[i]; maxIdx = i; } }
            double mu = x[maxIdx];
            double halfMax = maxHeight * 0.5;
            int leftIdx = maxIdx; while (leftIdx > 0 && y[leftIdx] > halfMax) leftIdx--;
            int rightIdx = maxIdx; while (rightIdx < y.Length - 1 && y[rightIdx] > halfMax) rightIdx++;
            double fwhm = x[rightIdx] - x[leftIdx];
            if (fwhm <= 0) fwhm = (x.Max() - x.Min()) / 100.0;
            return (mu, fwhm / 2.355, maxHeight);
        }

        private static (double[] edges, double[] centers, double[] counts) CreateHistogram(double[] data, int numBins)
        {
            // Standard histogram logic with dynamic bins
            double[] edges = new double[numBins + 1];
            double[] centers = new double[numBins];
            double[] counts = new double[numBins];
            for (int i = 0; i <= numBins; i++) edges[i] = i;
            for (int i = 0; i < numBins; i++) centers[i] = edges[i] + 0.5;

            int maxBinIdx = numBins - 1;
            foreach (double val in data)
            {
                int bin = (int)val;
                // Safety clamp
                if (bin >= numBins) bin = maxBinIdx;
                if (bin < 0) bin = 0;

                counts[bin]++;
            }
            return (edges, centers, counts);
        }
    }
}