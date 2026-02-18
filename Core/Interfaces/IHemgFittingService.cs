namespace BaselineMode.WPF.Core.Interfaces
{
    /// <summary>
    /// Double-sided Hyper-EMG (Exponentially Modified Gaussian) fitting.
    /// </summary>
    public interface IHemgFittingService
    {
        /// <summary>
        /// Generate HEMG curve from parameters [A, mu, sigma, tauL, tauR, etaL, etaR]
        /// </summary>
        double[] GenerateHemgCurve(double[] x, double[] p);

        /// <summary>
        /// Fit histogram (bin centers, counts) with double-sided Hyper-EMG.
        /// Returns (fitCurve, parameters) where parameters are [A, mu, sigma, tauL, tauR, etaL, etaR].
        /// </summary>
        (double[] fitCurve, double[] parameters) HemgDoubleSidedFit(double[] binCenters, double[] counts, double[]? initialGuess = null, bool[]? fixedParams = null);
    }
}
