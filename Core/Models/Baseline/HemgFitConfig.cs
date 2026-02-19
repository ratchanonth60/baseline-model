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

        // ROI Settings
        public double RoiSigmaMultiplier { get; set; } = 8.0;
        public int DefaultRoiWindow { get; set; } = 100;

        // Optimization Settings
        public double InitialEta { get; set; } = 0.05;
        public double JacobianEpsilon { get; set; } = 1e-5;
        public double ConvergenceTolerance { get; set; } = 1e-4; // Assuming 1e-4 is a common default, check implementation for actual
        public double MinParameterValue { get; set; } = 1e-3;
        public double MaxTotalEta { get; set; } = 0.99;
    }
}
