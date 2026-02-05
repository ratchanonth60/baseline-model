using System;

namespace BaselineMode.WPF.Services.Observation
{
    public class KalmanFilter
    {
        private double A, H, Q, R, P, x;

        public KalmanFilter(double A, double H, double Q, double R, double initial_P, double initial_x)
        {
            this.A = A;
            this.H = H;
            this.Q = Q;
            this.R = R;
            this.P = initial_P;
            this.x = initial_x;
        }

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
            x = x + K * (input - H * x);
            P = (1 - K * H) * P;

            return x;
        }
    }
}
