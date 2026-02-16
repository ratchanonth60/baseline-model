namespace BaselineMode.WPF.Core.Interfaces
{
    /// <summary>
    /// Double-sided Hyper-EMG (Exponentially Modified Gaussian) fitting.
    /// </summary>
    public interface IHemgFittingService
    {
        /// <summary>
        /// Fit histogram (bin centers, counts) with double-sided Hyper-EMG.
        /// Returns (fitCurve, parameters) where parameters are [A, mu, sigma, tauL, tauR, etaL, etaR].
        /// </summary>
        (double[] fitCurve, double[] parameters) HemgDoubleSidedFit(double[] binCenters, double[] counts);
    }
}
