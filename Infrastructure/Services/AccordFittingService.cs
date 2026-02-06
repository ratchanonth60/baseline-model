// Services/AccordFittingService.cs
// Migrated from Accord.NET to use native C# implementation
using System;
using System.Linq;

namespace BaselineMode.WPF.Infrastructure.Services
{
    /// <summary>
    /// Fitting service that provides Gaussian fit functionality.
    /// Originally used Accord.NET, now uses native C# implementation.
    /// </summary>
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

                // Calculate mean and standard deviation using native C#
                double mu = data.Average();
                double sumSquaredDiff = data.Sum(x => Math.Pow(x - mu, 2));
                double sigma = Math.Sqrt(sumSquaredDiff / (data.Length - 1));

                // Generate fit curve
                double[] fitCurve = new double[binCenters.Length];
                double peak = counts.Max();

                // Calculate normal distribution PDF at mean
                double normalPeak = GaussianPdf(mu, mu, sigma);
                double scale = normalPeak > 0 ? peak / normalPeak : 1;

                for (int i = 0; i < binCenters.Length; i++)
                {
                    fitCurve[i] = GaussianPdf(binCenters[i], mu, sigma) * scale;
                }

                return (fitCurve, mu, sigma, peak);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Gaussian Fit Error: {ex.Message}");
                return (Array.Empty<double>(), 0, 0, 0);
            }
        }

        /// <summary>
        /// Gaussian probability density function
        /// </summary>
        private double GaussianPdf(double x, double mu, double sigma)
        {
            if (sigma <= 0) return 0;
            double exponent = -Math.Pow(x - mu, 2) / (2 * sigma * sigma);
            return (1.0 / (sigma * Math.Sqrt(2 * Math.PI))) * Math.Exp(exponent);
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