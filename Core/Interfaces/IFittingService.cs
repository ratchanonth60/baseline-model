using System;
using BaselineMode.WPF.Core.Models;

namespace BaselineMode.WPF.Core.Interfaces
{
    public interface IFittingService
    {
        FittingAlgorithm Algorithm { get; set; }

        /// <summary>
        /// Perform Gaussian curve fitting
        /// </summary>
        FittingResult GaussianFit(double[] xData, double[] yData);

        /// <summary>
        /// Single-sided HEMG fit (Left-tailed)
        /// </summary>
        FittingResult HemgSingleSidedFit(double[] xData, double[] yData);

        /// <summary>
        /// Fit data with double-sided Hyper-EMG function
        /// </summary>
        FittingResult HemgDoubleSidedFit(double[] xData, double[] yData);
    }
}
