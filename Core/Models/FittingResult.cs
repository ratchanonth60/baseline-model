namespace BaselineMode.WPF.Models
{
    public class FittingResult
    {
        public double[] FitCurve { get; set; }
        public double Mu { get; set; }
        public double Sigma { get; set; }
        public double Peak { get; set; }
        public double RMS { get; set; }

        // HEMG-specific parameters
        public double A { get; set; }
        public double TauL1 { get; set; }
        public double TauR1 { get; set; }
        public double EtaL1 { get; set; }
        public double EtaR1 { get; set; }

        public FittingResult(double[] fitCurve, double mu, double sigma, double peak, double rms)
        {
            FitCurve = fitCurve;
            Mu = mu;
            Sigma = sigma;
            Peak = peak;
            RMS = rms;
            A = 0;
            TauL1 = 0;
            TauR1 = 0;
            EtaL1 = 0;
            EtaR1 = 0;
        }

        // Constructor for HEMG results
        public FittingResult(double[] fitCurve, double a, double mu, double sigma, double tauL1, 
            double tauR1, double etaL1, double etaR1)
        {
            FitCurve = fitCurve;
            A = a;
            Mu = mu;
            Sigma = sigma;
            TauL1 = tauL1;
            TauR1 = tauR1;
            EtaL1 = etaL1;
            EtaR1 = etaR1;
            Peak = a;
            RMS = 0;
        }

        public static FittingResult Empty(int length)
        {
            return new FittingResult(new double[length], 0, 0, 0, 0);
        }
    }
}
