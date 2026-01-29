using System;
using BaselineMode.WPF.Models;

namespace BaselineMode.WPF.Services
{
    /// <summary>
    /// Interface for mathematical operations with memory-safe implementations
    /// </summary>
    public interface IMathService : IDisposable
    {
        /// <summary>
        /// Calculate statistical moments (mean, sigma, peak)
        /// </summary>
        (double mean, double sigma, double peak) CalculateMoments(double[] xData, double[] yData);

        /// <summary>
        /// Calculate RMS (Root Mean Square)
        /// </summary>
        double CalculateRMS(double[] xData, double[] yData, double mean);

        /// <summary>
        /// Perform Gaussian curve fitting
        /// </summary>
        FittingResult GaussianFit(double[] xData, double[] yData);

        /// <summary>
        /// Perform Hyper-EMG curve fitting
        /// </summary>
        FittingResult HyperEMGFit(double[] xData, double[] yData);
    }
}
