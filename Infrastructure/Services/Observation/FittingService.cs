using System;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Optimization;

using BaselineMode.WPF.Interfaces.Observation;
using BaselineMode.WPF.Models.Observation;

namespace BaselineMode.WPF.Services.Observation
{
    public class ObservationFittingService : IObservationFittingService
    {
        public ObservationFittingResult GaussianFit(double[] xData, double[] yData)
        {
            // Input validation
            if (xData == null || yData == null || xData.Length != yData.Length || xData.Length < 3)
            {
                return null;
            }

            try
            {
                // 1. Estimate Initial Guesses
                double maxY = yData.Max();
                int maxIndex = Array.IndexOf(yData, maxY);
                double estimatedMean = xData[maxIndex];

                // Estimate Width (Sigma) via FWHM approx
                double halfMax = maxY / 2.0;
                int leftIdx = -1;
                int rightIdx = -1;

                // Scan outward from peak for half-max points
                for (int i = maxIndex; i >= 0; i--)
                {
                    if (yData[i] <= halfMax) { leftIdx = i; break; }
                }
                for (int i = maxIndex; i < yData.Length; i++)
                {
                    if (yData[i] <= halfMax) { rightIdx = i; break; }
                }

                double fwhmGuess = 0;
                if (leftIdx >= 0 && rightIdx >= 0 && rightIdx > leftIdx)
                {
                    fwhmGuess = xData[rightIdx] - xData[leftIdx];
                }
                else
                {
                    // Fallback guess: assume peak width is roughly 10% of range if not found
                    fwhmGuess = (xData.Last() - xData.First()) * 0.1;
                }

                double estimatedSigma = fwhmGuess / 2.355;
                double estimatedAmplitude = maxY;

                // Initial Vector: [Amplitude, Mean, Sigma]
                var initialGuess = Vector<double>.Build.Dense(new[] { estimatedAmplitude, estimatedMean, estimatedSigma });

                // 2. Define Model Function: f(x, p) = A * exp( - (x - mu)^2 / (2 * sigma^2) )
                // p[0] = Amplitude, p[1] = Mean, p[2] = Sigma
                // 2. Define Model Function: f(p, x) = A * exp( - (x - mu)^2 / (2 * sigma^2) )
                // p[0] = Amplitude, p[1] = Mean, p[2] = Sigma
                Func<Vector<double>, Vector<double>, Vector<double>> model = (p, x) =>
                {
                    double amp = p[0];
                    double mu = p[1];
                    double sig = p[2];

                    var y = Vector<double>.Build.Dense(x.Count);
                    for (int i = 0; i < x.Count; i++)
                    {
                        y[i] = amp * Math.Exp(-Math.Pow(x[i] - mu, 2) / (2 * sig * sig));
                    }
                    return y;
                };

                // 3. Define Objective Function (Nonlinear Least Squares)
                var obj = ObjectiveFunction.NonlinearModel(model, Vector<double>.Build.Dense(xData), Vector<double>.Build.Dense(yData));

                // 4. Solve using Levenberg-Marquardt
                var solver = new LevenbergMarquardtMinimizer();
                var result = solver.FindMinimum(obj, initialGuess);

                var minimizedParams = result.MinimizingPoint;
                double finalAmp = minimizedParams[0];
                double finalMean = minimizedParams[1];
                double finalSigma = minimizedParams[2];

                // 5. Generate Fitted Curve
                double[] fittedY = model(minimizedParams, Vector<double>.Build.Dense(xData)).ToArray();

                // 6. Calculate FWHM and Resolution
                // FWHM = 2.355 * sigma
                double finalFWHM = 2.355 * Math.Abs(finalSigma);
                double finalResolution = (Math.Abs(finalMean) > 1e-9) ? (finalFWHM / finalMean * 100.0) : 0;

                // 7. Calculate R-squared
                double yMean = yData.Average();
                double ssTot = yData.Sum(y => Math.Pow(y - yMean, 2));
                double ssRes = yData.Zip(fittedY, (y, f) => Math.Pow(y - f, 2)).Sum();
                double rSquared = (ssTot > 1e-9) ? 1 - (ssRes / ssTot) : 0;

                return new ObservationFittingResult
                {
                    Amplitude = finalAmp,
                    Mean = finalMean,
                    Sigma = finalSigma,
                    FittedCurve = fittedY,
                    FWHM = finalFWHM,
                    Resolution = finalResolution,
                    R_Squared = rSquared
                };
            }
            catch (Exception)
            {
                // In case of fitting failure, return null or handle gracefully
                return null;
            }
        }
    }
}
