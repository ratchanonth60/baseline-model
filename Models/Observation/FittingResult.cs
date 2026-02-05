namespace BaselineMode.WPF.Models.Observation
{
    public class ObservationFittingResult
    {
        public double Amplitude { get; set; }
        public double Mean { get; set; }
        public double Sigma { get; set; }
        public double[] FittedCurve { get; set; }
        public double FWHM { get; set; }
        public double Resolution { get; set; } // in %
        public double R_Squared { get; set; }
    }
}
