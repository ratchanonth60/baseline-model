namespace BaselineMode.WPF.Models
{
    public class FittingResult
    {
        public double[] FitCurve { get; set; }
        public double Mu { get; set; }
        public double Sigma { get; set; }
        public double Peak { get; set; }
        public double RMS { get; set; }

        public FittingResult(double[] fitCurve, double mu, double sigma, double peak, double rms)
        {
            FitCurve = fitCurve;
            Mu = mu;
            Sigma = sigma;
            Peak = peak;
            RMS = rms;
        }

        public static FittingResult Empty(int length)
        {
            return new FittingResult(new double[length], 0, 0, 0, 0);
        }
    }
}
