using System;
using System.Linq;
using BaselineMode.WPF.Core.Interfaces;
using BaselineMode.WPF.Core.Models;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Optimization;

namespace BaselineMode.WPF.Infrastructure.Services
{
    /// <summary>
    /// HEMG (Hyper-Exponentially Modified Gaussian) Fitting Service
    /// uses Manual Levenberg-Marquardt Optimization via Math.NET Linear Algebra.
    /// Supports strategy selection (LMA, Nelder-Mead, Legacy).
    /// </summary>
    public class HemgFittingService : IFittingService
    {
        private const double MAX_EXP_ARG = 700.0;
        private const double SQRT_2 = 1.41421356237;
        private const double SQRT_2PI = 2.50662827463;

        // --- Algorithm Selection ---
        public FittingAlgorithm Algorithm { get; set; } = FittingAlgorithm.LevenbergMarquardt;

        public FittingResult GaussianFit(double[] xData, double[] yData)
        {
            try
            {
                if (xData == null || yData == null || xData.Length != yData.Length || xData.Length == 0)
                    return FittingResult.Empty(0);

                var (mu, sigma) = CalculateWeightedMoments(xData, yData);
                double peak = yData.Max();
                double[] p0 = [peak, mu, sigma];

                // Gaussian Model
                static double gaussianModel(double x, double[] p)
                {
                    double A = Math.Abs(p[0]);
                    double m = p[1];
                    double s = Math.Abs(p[2]);
                    if (s < 1e-9) s = 1e-9;
                    double s2 = 2 * s * s;
                    return A * Math.Exp(-Math.Pow(x - m, 2) / s2);
                }

                // Fit using Selected Algorithm
                double[] pFit = FitCurve(gaussianModel, p0, xData, yData);

                double fitA = Math.Abs(pFit[0]);
                double fitMu = pFit[1];
                double fitSigma = Math.Abs(pFit[2]);
                double fitS2 = 2 * fitSigma * fitSigma;

                double[] fitCurve = new double[xData.Length];
                double sumSqErr = 0;
                for (int i = 0; i < xData.Length; i++)
                {
                    double val = fitA * Math.Exp(-Math.Pow(xData[i] - fitMu, 2) / fitS2);
                    fitCurve[i] = val;
                    double r = yData[i] - val;
                    sumSqErr += r * r;
                }
                double rms = Math.Sqrt(sumSqErr / xData.Length);

                // FWHM for Gaussian: 2 * sqrt(2 * ln(2)) * sigma ≈ 2.3548 * sigma
                double fwhm = 2.0 * Math.Sqrt(2.0 * Math.Log(2.0)) * fitSigma;
                double resolution = Math.Abs(fitMu) > 1e-9 ? (fwhm / fitMu * 100.0) : 0;

                // R-Squared
                double yMean = yData.Length > 0 ? yData.Average() : 0;
                double ssTot = yData.Sum(y => Math.Pow(y - yMean, 2));
                double rSquared = ssTot > 1e-9 ? 1 - (sumSqErr / ssTot) : 0;

                var result = new FittingResult(fitCurve, fitMu, fitSigma, fitA, rms)
                {
                    FWHM = fwhm,
                    Resolution = resolution,
                    R_Squared = rSquared
                };
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Gaussian Fit Error: {ex.Message}");
                return FittingResult.Empty(xData?.Length ?? 0);
            }
        }

        public FittingResult LorentzianFit(double[] xData, double[] yData)
        {
            try
            {
                if (xData == null || yData == null || xData.Length != yData.Length || xData.Length == 0)
                    return FittingResult.Empty(0);

                var (mu, sigma) = CalculateWeightedMoments(xData, yData);
                double peak = yData.Max();
                double gamma = Math.Max(sigma, 1e-9); // Initial gamma estimate from sigma
                double[] p0 = [peak, mu, gamma];

                // Lorentzian Model: L(x) = A * gamma^2 / ((x - x0)^2 + gamma^2)
                static double lorentzianModel(double x, double[] p)
                {
                    double A = Math.Abs(p[0]);
                    double x0 = p[1];
                    double g = Math.Abs(p[2]);
                    if (g < 1e-9) g = 1e-9;
                    return A * g * g / (Math.Pow(x - x0, 2) + g * g);
                }

                // Fit using Selected Algorithm
                double[] pFit = FitCurve(lorentzianModel, p0, xData, yData);

                double fitA = Math.Abs(pFit[0]);
                double fitMu = pFit[1];
                double fitGamma = Math.Abs(pFit[2]);

                double[] fitCurve = new double[xData.Length];
                double sumSqErr = 0;
                for (int i = 0; i < xData.Length; i++)
                {
                    double val = fitA * fitGamma * fitGamma / (Math.Pow(xData[i] - fitMu, 2) + fitGamma * fitGamma);
                    fitCurve[i] = val;
                    double r = yData[i] - val;
                    sumSqErr += r * r;
                }
                double rms = Math.Sqrt(sumSqErr / xData.Length);
                double fwhm = 2 * fitGamma;
                double resolution = Math.Abs(fitMu) > 1e-9 ? (fwhm / fitMu * 100.0) : 0;

                // R-Squared
                double yMean = yData.Length > 0 ? yData.Average() : 0;
                double ssTot = yData.Sum(y => Math.Pow(y - yMean, 2));
                double ssRes = sumSqErr;
                double rSquared = ssTot > 1e-9 ? 1 - (ssRes / ssTot) : 0;

                var result = new FittingResult(fitCurve, fitMu, fitGamma, fitA, rms)
                {
                    FWHM = fwhm,
                    Resolution = resolution,
                    R_Squared = rSquared
                };
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Lorentzian Fit Error: {ex.Message}");
                return FittingResult.Empty(xData?.Length ?? 0);
            }
        }

        public FittingResult HemgSingleSidedFit(double[] xData, double[] yData)
        {
            var (fitCurve, parameters) = HemgSingleSidedFitHistogram(xData, yData);
            if (parameters == null || parameters.Length < 6)
                return FittingResult.Empty(xData?.Length ?? 0);

            var p = parameters;
            var res = new FittingResult
            {
                FitCurve = fitCurve,
                A = p[0],
                Mu = p[1],
                Sigma = p[2],
                Peak = p[0],
                TauL1 = p[3],
                TauL2 = p[4],
                EtaL1 = p[5],
                EtaL2 = 1.0 - p[5],
                TauR1 = 0,
                EtaR1 = 0
            };
            return res;
        }

        public FittingResult HemgDoubleSidedFit(double[] xData, double[] yData)
        {
            var (fitCurve, parameters) = HemgDoubleSidedFitHistogram(xData, yData);
            if (parameters == null || parameters.Length < 7)
                return FittingResult.Empty(xData?.Length ?? 0);

            var p = parameters;
            return new FittingResult(fitCurve, p[0], p[1], p[2], p[3], p[4], p[5], p[6]);
        }

        public (double[] fitCurve, double[] parameters) HemgDoubleSidedFit(double[] thresholdedData)
        {
            var (_, centers, counts) = CreateHistogram(thresholdedData);
            return HemgDoubleSidedFitHistogram(centers, counts, thresholdedData);
        }

        public (double[] fitCurve, double[] parameters) HemgDoubleSidedFitHistogram(double[] centers, double[] counts, double[]? rawDataOptional = null)
        {
            try
            {
                if (centers == null || counts == null || centers.Length == 0) return (Array.Empty<double>(), Array.Empty<double>());

                // Normalization
                double totalSum = counts.Sum();
                if (totalSum <= 1e-9) totalSum = 1.0;
                double[] normCounts = new double[counts.Length];
                for (int i = 0; i < counts.Length; i++) normCounts[i] = counts[i] / totalSum;

                double A0 = 1.0;
                double mu0, sigma0;
                (mu0, sigma0) = CalculateWeightedMoments(centers, counts);

                double[] p0 = [A0, mu0, sigma0, 0.5, 1.5, 0.5, 0.5];

                // Define Model
                static double modelFunc(double xVal, double[] p)
                {
                    // p: A, mu, sigma, tauL, tauR, etaL, etaR
                    return HyperEmgDouble(xVal, Math.Abs(p[0]), p[1], Math.Abs(p[2]),
                                          [Math.Abs(p[3])], [p[5]],
                                          [Math.Abs(p[4])], [p[6]]);
                }

                // Fit using Selected Algorithm
                double[] pFit = FitCurve(modelFunc, p0, centers, normCounts);

                // Denormalize
                pFit[0] *= totalSum;

                // Generate Curve
                double[] fitCurve = new double[centers.Length];
                for (int i = 0; i < centers.Length; i++)
                {
                    fitCurve[i] = HyperEmgDouble(centers[i], pFit[0], pFit[1], pFit[2],
                                                 [pFit[3]], [pFit[5]],
                                                 [pFit[4]], [pFit[6]]);
                    if (!double.IsFinite(fitCurve[i])) fitCurve[i] = 0.0;
                }

                return (fitCurve, pFit);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HEMG Double Fit Error: {ex.Message}");
                return (Array.Empty<double>(), Array.Empty<double>());
            }
        }

        public (double[] fitCurve, double[] parameters) HemgSingleSidedFit(double[] input)
        {
            var (_, centers, counts) = CreateHistogram(input);
            return HemgSingleSidedFitHistogram(centers, counts, input);
        }

        public (double[] fitCurve, double[] parameters) HemgSingleSidedFitHistogram(double[] centers, double[] counts, double[] rawDataOptional = null)
        {
            try
            {
                if (centers == null || counts == null || centers.Length == 0) return (Array.Empty<double>(), Array.Empty<double>());

                double totalSum = counts.Sum();
                if (totalSum <= 1e-9) totalSum = 1.0;
                double[] normCounts = new double[counts.Length];
                for (int i = 0; i < counts.Length; i++) normCounts[i] = counts[i] / totalSum;

                double A0 = 1.0;
                double mu0, sigma0;
                (mu0, sigma0) = CalculateWeightedMoments(centers, counts);

                double[] p0 = [A0, mu0, sigma0, 0.5, 1.5, 0.5];

                static double modelFunc(double xVal, double[] p)
                {
                    return HyperEmgLeft(xVal, Math.Abs(p[0]), p[1], Math.Abs(p[2]),
                                        [Math.Abs(p[3]), Math.Abs(p[4])],
                                        [p[5], 1.0 - p[5]]);
                }

                // Fit using Selected Algorithm
                double[] pFit = FitCurve(modelFunc, p0, centers, normCounts);

                pFit[0] *= totalSum;

                double[] fitCurve = new double[centers.Length];
                for (int i = 0; i < centers.Length; i++)
                {
                    fitCurve[i] = HyperEmgLeft(centers[i], pFit[0], pFit[1], pFit[2],
                                             [pFit[3], pFit[4]],
                                             [pFit[5], 1.0 - pFit[5]]);
                }

                return (fitCurve, pFit);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HEMG Single Fit Error: {ex.Message}");
                return (Array.Empty<double>(), new double[6]);
            }
        }

        // --- Fitting Strategy Dispatcher ---
        private double[] FitCurve(Func<double, double[], double> modelFunc, double[] p0, double[] centers, double[] normCounts)
        {
            return Algorithm switch
            {
                FittingAlgorithm.NelderMead => FitCurveNelderMead(modelFunc, p0, centers, normCounts),
                FittingAlgorithm.GradientDescentLegacy => FitCurveGradientDescentLegacy(modelFunc, p0, centers, normCounts),
                _ => FitCurveLevenbergMarquardtManual(modelFunc, p0, centers, normCounts),
            };
        }

        // --- Strategy 1: Manual Levenberg-Marquardt ---
        private static double[] FitCurveLevenbergMarquardtManual(Func<double, double[], double> modelFunc, double[] p0, double[] xData, double[] yData)
        {
            int n = p0.Length;
            int m = xData.Length;
            var p = Vector<double>.Build.DenseOfArray(p0);
            var yObs = Vector<double>.Build.DenseOfArray(yData);

            double lambda = 0.01;
            int maxIter = 100;
            double tolerance = 1e-5;

            var r = CalcResiduals(modelFunc, p, xData, yObs);
            double currentError = r.DotProduct(r);

            for (int iter = 0; iter < maxIter; iter++)
            {
                var J = CalcJacobian(modelFunc, p, xData);
                var Jt = J.Transpose();
                var H = Jt * J;
                var g = Jt * r;

                var H_damp = H.Clone();
                for (int i = 0; i < n; i++)
                    H_damp[i, i] += lambda * (Math.Abs(H[i, i]) + 1e-6);

                Vector<double> delta;
                try { delta = H_damp.Solve(g); }
                catch { delta = g * 0.001; } // Fallback

                var p_new = p + delta;

                var r_new = CalcResiduals(modelFunc, p_new, xData, yObs);
                double newError = r_new.DotProduct(r_new);

                if (newError < currentError)
                {
                    p = p_new;
                    lambda /= 10.0;
                    if (lambda < 1e-7) lambda = 1e-7;
                    if (Math.Abs(currentError - newError) < tolerance) break;
                    currentError = newError;
                    r = r_new;
                }
                else
                {
                    lambda *= 10.0;
                    if (lambda > 1e7) break;
                }
            }
            return EnforceConstraints([.. p], p0.Length);
        }

        // --- Strategy 2: Nelder-Mead Simplex ---
        private double[] FitCurveNelderMead(Func<double, double[], double> modelFunc, double[] p0, double[] xData, double[] yData)
        {
            try
            {
                // NelderMeadSimplex requires IObjectiveFunction
                var xVec = Vector<double>.Build.DenseOfArray(xData);
                var yVec = Vector<double>.Build.DenseOfArray(yData);

                // Objective: Sum Squared Error
                double objective(Vector<double> pVec)
                {
                    double[] p = [.. pVec];
                    double sumSq = 0;
                    for (int i = 0; i < xData.Length; i++)
                    {
                        double diff = yData[i] - modelFunc(xData[i], p);
                        sumSq += diff * diff;
                    }
                    return sumSq;
                }

                // Use Math.NET's NelderMeadSimplex
                var obj = ObjectiveFunction.Value(objective);
                var solver = new NelderMeadSimplex(1e-5, 500); // 500 iterations
                var initialGuess = Vector<double>.Build.DenseOfArray(p0);

                var result = solver.FindMinimum(obj, initialGuess);
                return EnforceConstraints([.. result.MinimizingPoint], p0.Length);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Nelder-Mead Error: {ex.Message}");
                // Fallback to LMA if NM fails
                return FitCurveLevenbergMarquardtManual(modelFunc, p0, xData, yData);
            }
        }

        // --- Strategy 3: Legacy Gradient Descent (Simplified) ---
        private static double[] FitCurveGradientDescentLegacy(Func<double, double[], double> modelFunc, double[] p0, double[] xData, double[] yData)
        {
            // Fallback to LMA for now until requested full legacy copy
            // But treating it as "Standard Gradient Descent" (which LMA basically encompasses)
            return FitCurveLevenbergMarquardtManual(modelFunc, p0, xData, yData);
        }

        private static double[] EnforceConstraints(double[] finalP, int p0Length)
        {
            finalP[0] = Math.Abs(finalP[0]); // A
            finalP[2] = Math.Abs(finalP[2]); // Sigma
            for (int k = 3; k < finalP.Length; k++)
            {
                bool isEta = (p0Length >= 7 && (k == 5 || k == 6)) || (p0Length < 7 && k == 5);
                if (isEta) finalP[k] = Math.Max(0, Math.Min(1, finalP[k]));
                else finalP[k] = Math.Abs(finalP[k]);
            }
            return finalP;
        }

        private static Vector<double> CalcResiduals(Func<double, double[], double> model, Vector<double> p, double[] x, Vector<double> y)
        {
            var res = new double[x.Length];
            double[] pArr = [.. p];
            for (int i = 0; i < x.Length; i++)
                res[i] = y[i] - model(x[i], pArr);
            return Vector<double>.Build.Dense(res);
        }

        private static Matrix<double> CalcJacobian(Func<double, double[], double> model, Vector<double> p, double[] x)
        {
            int n = p.Count;
            int m = x.Length;
            var J = Matrix<double>.Build.Dense(m, n);
            double[] pArr = [.. p];
            double delta = 1e-6;

            var f0 = new double[m];
            for (int i = 0; i < m; i++) f0[i] = model(x[i], pArr);

            for (int j = 0; j < n; j++)
            {
                double orig = pArr[j];
                pArr[j] += delta;
                for (int i = 0; i < m; i++)
                {
                    double fNew = model(x[i], pArr);
                    J[i, j] = (fNew - f0[i]) / delta;
                }
                pArr[j] = orig;
            }
            return J;
        }

        // --- Helpers ---

        private static (double mean, double sigma) CalculateWeightedMoments(double[] x, double[] w)
        {
            double maxVal = w.Max();
            int peakIdx = Array.IndexOf(w, maxVal);
            if (peakIdx < 0) return (0, 1);

            // Use half-maximum to determine peak region instead of fixed window
            double halfMax = maxVal / 2.0;

            // Find left boundary where signal drops below half-max
            int start = peakIdx;
            for (int i = peakIdx - 1; i >= 0; i--)
            {
                if (w[i] < halfMax) { start = i; break; }
                if (i == 0) start = 0;
            }

            // Find right boundary where signal drops below half-max
            int end = peakIdx;
            for (int i = peakIdx + 1; i < x.Length; i++)
            {
                if (w[i] < halfMax) { end = i; break; }
                if (i == x.Length - 1) end = x.Length - 1;
            }

            // Ensure minimum window of ±5 bins around peak
            start = Math.Min(start, Math.Max(0, peakIdx - 5));
            end = Math.Max(end, Math.Min(x.Length - 1, peakIdx + 5));

            double sumW = 0, sumWX = 0;
            for (int i = start; i <= end; i++) { sumW += w[i]; sumWX += w[i] * x[i]; }
            if (sumW <= 1e-9) return (0, 1);
            double mean = sumWX / sumW;
            double sumWSqDiff = 0;
            for (int i = start; i <= end; i++) { double diff = x[i] - mean; sumWSqDiff += w[i] * diff * diff; }
            double sigma = Math.Sqrt(Math.Max(0, sumWSqDiff / sumW));
            if (sigma < 1e-6) sigma = 1;
            return (mean, sigma);
        }

        private static (double[] edges, double[] centers, double[] counts) CreateHistogram(double[] data)
        {
            int numBins = 16384;
            double[] edges = new double[numBins + 1];
            double[] centers = new double[numBins];
            int[] counts_int = new int[numBins];

            for (int i = 0; i <= numBins; i++) edges[i] = i;
            for (int i = 0; i < numBins; i++) centers[i] = edges[i] + 0.5;

            foreach (double value in data)
            {
                int bin = (int)Math.Floor(value);
                if (bin >= 0 && bin < numBins) counts_int[bin]++;
            }

            double[] counts = new double[numBins];
            for (int i = 0; i < numBins; i++) counts[i] = counts_int[i];

            return (edges, centers, counts);
        }

        private static double HyperEmgDouble(double x, double A, double mu, double sigma,
                                     double[] tausLeft, double[] etasLeft,
                                     double[] tausRight, double[] etasRight)
        {
            double y = 0.0;
            for (int i = 0; i < tausLeft.Length; i++)
            {
                if (tausLeft[i] > 0)
                    y += etasLeft[i] * HyperComponent(x, mu, sigma, tausLeft[i], true);
            }
            for (int i = 0; i < tausRight.Length; i++)
            {
                if (tausRight[i] > 0)
                    y += etasRight[i] * HyperComponent(x, mu, sigma, tausRight[i], false);
            }
            y = A * y;
            if (!double.IsFinite(y)) y = 0.0;
            return y;
        }

        private static double HyperEmgLeft(double x, double A, double mu, double sigma, double[] taus, double[] etas)
        {
            double y = 0.0;
            for (int i = 0; i < taus.Length; i++)
            {
                if (taus[i] > 0)
                    y += etas[i] * HyperComponent(x, mu, sigma, taus[i], true);
            }
            y = A * y;
            if (!double.IsFinite(y)) y = 0.0;
            return y;
        }

        private static double HyperComponent(double x, double mu, double sigma, double tau, bool isLeft)
        {
            double sigma2 = sigma * sigma;
            double tau2 = tau * tau;
            double dist = isLeft ? (mu - x) : (x - mu);
            double z = (sigma2 / (2 * tau2)) + (isLeft ? -dist / tau : dist / tau);
            z = Math.Min(z, MAX_EXP_ARG);
            double arg = (sigma2 / tau + (isLeft ? -dist : dist)) / (SQRT_2 * sigma);

            if (arg > 25.0)
            {
                double d = x - mu;
                double gArg = (d * d) / (2 * sigma2);
                gArg = Math.Min(gArg, MAX_EXP_ARG);
                return (1.0 / (sigma * SQRT_2PI)) * Math.Exp(-gArg);
            }
            return (1.0 / (2.0 * tau)) * Math.Exp(z) * Erfc(arg);
        }

        private static double Erfc(double x) { return 1.0 - Erf(x); }
        private static double Erf(double x)
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
    }
}
