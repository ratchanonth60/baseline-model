using System;
using BaselineMode.WPF.Core.Models;
using BaselineMode.WPF.Core.Models.Baseline;

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
        /// Gets or sets the fitting algorithm to use
        /// </summary>
        BaselineMode.WPF.Core.Models.Baseline.FittingAlgorithm Algorithm { get; set; }

        /// <summary>
        /// Perform Gaussian curve fitting
        /// </summary>
        BaselineMode.WPF.Core.Models.Baseline.FittingResult GaussianFit(double[] xData, double[] yData);

        /// <summary>
        /// Perform Hyper-EMG curve fitting
        /// </summary>
        BaselineMode.WPF.Core.Models.Baseline.FittingResult HyperEMGFit(double[] xData, double[] yData);

        /// <summary>
        /// Perform Hyper-EMG curve fitting with raw data for initialization
        /// </summary>
        BaselineMode.WPF.Core.Models.Baseline.FittingResult HyperEMGFit(double[] xData, double[] yData, double[] rawData);

        /// <summary>
        /// Perform Double-Sided Hyper-EMG curve fitting
        /// </summary>
        BaselineMode.WPF.Core.Models.Baseline.FittingResult HyperEMGDoubleSidedFit(double[] xData, double[] yData);

        /// <summary>
        /// Perform Double-Sided Hyper-EMG curve fitting with raw data for initialization
        /// </summary>
        BaselineMode.WPF.Core.Models.Baseline.FittingResult HyperEMGDoubleSidedFit(double[] xData, double[] yData, double[] rawData);

        /// <summary>
        /// Perform Lorentzian curve fitting
        /// </summary>
        BaselineMode.WPF.Core.Models.Baseline.FittingResult LorentzianFit(double[] xData, double[] yData);

        /// <summary>
        /// Perform Hyper-EMG Double-Sided curve fitting
        /// </summary>
        BaselineMode.WPF.Core.Models.Baseline.FittingResult HemgDoubleSidedFit(double[] xData, double[] yData);

        /// <summary>
        /// Hyper-EMG Double-Sided fitting with raw data for better initialization
        /// </summary>
        BaselineMode.WPF.Core.Models.Baseline.FittingResult HemgDoubleSidedFit(double[] xData, double[] yData, double[]? rawData, BaselineMode.WPF.Core.Models.Baseline.HemgFitConfig? config = null);

        /// <summary>
        /// Hyper-EMG Double-Sided fitting with explicit initial parameters and optional modification locks
        /// </summary>
        BaselineMode.WPF.Core.Models.Baseline.FittingResult HemgDoubleSidedFitManual(double[] xData, double[] yData, double[]? initialGuess, bool[]? fixedParams = null);

        /// <summary>
        /// Generate HEMG curve from parameters
        /// </summary>
        double[] GenerateHemgCurve(double[] x, double[] parameters);
        /// <summary>
        /// Calculate histogram from data
        /// </summary>
        (double[] counts, double[] binEdges) CalculateHistogram(double[] data, double min, double max, int binCount);

        double[] GenerateGaussianCurve(double[] x, double A, double mu, double sigma);
        double[] GenerateLorentzianCurve(double[] x, double A, double mu, double sigma);
    }
}
