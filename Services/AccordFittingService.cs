// Services/AccordFittingService.cs
using System;
using System.Linq;
using Accord.Statistics.Distributions.Univariate;

namespace BaselineMode.WPF.Services
{
    public class AccordFittingService
    {
        public (double[] fitCurve, double mu, double sigma, double peak) GaussianFit(double[] data)
        {
            try
            {
                // Create histogram
                var histogram = CreateHistogram(data, 16384);
                double[] binCenters = histogram.binCenters;
                double[] counts = histogram.counts;

                // Fit using Normal Distribution
                var normal = NormalDistribution.Estimate(data);
                double mu = normal.Mean;
                double sigma = normal.StandardDeviation;

                // Generate fit curve
                double[] fitCurve = new double[binCenters.Length];
                double peak = counts.Max();

                // Scale normal distribution to match histogram peak
                double normalPeak = normal.ProbabilityDensityFunction(mu);
                double scale = peak / normalPeak;

                for (int i = 0; i < binCenters.Length; i++)
                {
                    fitCurve[i] = normal.ProbabilityDensityFunction(binCenters[i]) * scale;
                }

                return (fitCurve, mu, sigma, peak);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Accord Gaussian Fit Error: {ex.Message}");
                return (Array.Empty<double>(), 0, 0, 0);
            }
        }

        private (double[] binCenters, double[] counts) CreateHistogram(double[] data, int binCount)
        {
            if (data.Length == 0) return (Array.Empty<double>(), Array.Empty<double>());

            double min = data.Min();
            double max = data.Max();
            double binWidth = (max - min) / binCount;

            int[] histogram = new int[binCount];

            foreach (var value in data)
            {
                int bin = (int)((value - min) / binWidth);
                if (bin >= binCount) bin = binCount - 1;
                if (bin < 0) bin = 0;
                histogram[bin]++;
            }

            double[] binCenters = new double[binCount];
            double[] counts = new double[binCount];

            for (int i = 0; i < binCount; i++)
            {
                binCenters[i] = min + (i + 0.5) * binWidth;
                counts[i] = histogram[i];
            }

            return (binCenters, counts);
        }
    }
}