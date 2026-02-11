using System;

namespace BaselineMode.WPF.Infrastructure.Services.Observation
{
    public class KalmanFilter(double A, double H, double Q, double R, double initial_P, double initial_x)
    {
        private readonly double A = A;
        private readonly double H = H;
        private double Q = Q;
        private double R = R;
        private double P = initial_P;
        private double x = initial_x;

        public void SetR(double R)
        {
            this.R = R;
        }

        public double GetR()
        {
            return this.R;
        }

        public void SetQ(double Q)
        {
            this.Q = Q;
        }

        public double GetQ()
        {
            return this.Q;
        }

        public double Output(double input)
        {
            // Time update - prediction
            x = A * x;
            P = A * P * A + Q;

            // Measurement update - correction
            double K = P * H / (H * P * H + R);
            x += K * (input - H * x);
            P = (1 - K * H) * P;

            return x;
        }
    }
}
