namespace BaselineMode.WPF.Core.Models.Baseline
{
    public enum HemgMode
    {
        Gaussian,
        Lorentzian,
        LeftTailed,
        RightTailed,
        DoubleSided
    }

    public class HemgFitConfig
    {
        // Fitting Settings
        public int MaxIterations { get; set; } = 30;
        public double Lambda { get; set; } = 0.01; // Initial Damping
        public HemgMode Mode { get; set; } = HemgMode.DoubleSided; // Default to DoubleSided as per request

        // Data/Hardware Settings
        public int AdcResolution { get; set; } = 16384;
        public double VoltageRange { get; set; } = 5000.0; // mV
        public int HistogramBinCount { get; set; } = 16384;
    }
}
