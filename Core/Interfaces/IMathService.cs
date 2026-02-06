using System;
using BaselineMode.WPF.Core.Models;

namespace BaselineMode.WPF.Core.Interfaces
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
        /// <summary>
        /// Perform Hyper-EMG curve fitting
        /// </summary>
        FittingResult HyperEMGFit(double[] xData, double[] yData);

        /// <summary>
        /// Perform Hyper-EMG curve fitting with raw data for initialization
        /// </summary>
        FittingResult HyperEMGFit(double[] xData, double[] yData, double[] rawData);

        /// <summary>
        /// Perform Double-Sided Hyper-EMG curve fitting
        /// </summary>
        FittingResult HyperEMGDoubleSidedFit(double[] xData, double[] yData);

        /// <summary>
        /// Perform Double-Sided Hyper-EMG curve fitting with raw data for initialization
        /// </summary>
        FittingResult HyperEMGDoubleSidedFit(double[] xData, double[] yData, double[] rawData);
    }
}
