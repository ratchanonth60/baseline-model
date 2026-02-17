using System.Runtime.CompilerServices;

namespace BaselineMode.WPF.Infrastructure.Services
{
    public class KalmanFilter(double A, double H, double Q, double R, double initial_P, double initial_x)
    {
        private readonly double A = A;
        private readonly double H = H;
        private double Q = Q;
        private double R = R;
        private double P = initial_P;
        private double x = initial_x;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetR(double R) => this.R = R;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetR() => this.R;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetQ(double Q) => this.Q = Q;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetQ() => this.Q;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
