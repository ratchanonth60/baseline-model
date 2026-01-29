using Xunit;
using BaselineMode.WPF.Services;
using System;

namespace BaselineMode.WPF.Tests
{
    public class MathServiceTests
    {
        private readonly MathService _mathService;

        public MathServiceTests()
        {
            _mathService = new MathService();
        }

        #region CalculateMoments Tests

        [Fact]
        public void CalculateMoments_UniformDistribution_CalculatesMean()
        {
            // Arrange
            double[] xData = { 1, 2, 3, 4, 5 };
            double[] yData = { 1, 1, 1, 1, 1 }; // Uniform weights

            // Act
            var (mean, sigma, peak) = _mathService.CalculateMoments(xData, yData);

            // Assert
            Assert.Equal(3.0, mean, 5); // Mean should be 3
            Assert.Equal(1.0, peak);
        }

        [Fact]
        public void CalculateMoments_SinglePeak_FindsPeak()
        {
            // Arrange
            double[] xData = { 1, 2, 3, 4, 5 };
            double[] yData = { 1, 2, 10, 2, 1 }; // Peak at x=3

            // Act
            var (mean, sigma, peak) = _mathService.CalculateMoments(xData, yData);

            // Assert
            Assert.Equal(10.0, peak);
            Assert.True(mean > 2.5 && mean < 3.5); // Mean should be around 3
        }

        [Fact]
        public void CalculateMoments_AllZeros_ReturnsZeros()
        {
            // Arrange
            double[] xData = { 1, 2, 3, 4, 5 };
            double[] yData = { 0, 0, 0, 0, 0 };

            // Act
            var (mean, sigma, peak) = _mathService.CalculateMoments(xData, yData);

            // Assert
            Assert.Equal(0.0, mean);
            Assert.Equal(0.0, sigma);
            Assert.Equal(0.0, peak);
        }

        [Fact]
        public void CalculateMoments_GaussianLike_CalculatesSigma()
        {
            // Arrange - Gaussian-like distribution centered at 5
            double[] xData = new double[11];
            double[] yData = new double[11];
            for (int i = 0; i < 11; i++)
            {
                xData[i] = i;
                double x = i - 5; // Center at 5
                yData[i] = Math.Exp(-x * x / 2); // Gaussian with sigma=1
            }

            // Act
            var (mean, sigma, peak) = _mathService.CalculateMoments(xData, yData);

            // Assert
            Assert.True(Math.Abs(mean - 5.0) < 0.1, $"Mean should be ~5, got {mean}");
            Assert.True(sigma > 0.5 && sigma < 2.0, $"Sigma should be ~1, got {sigma}");
            Assert.Equal(1.0, peak, 3); // Peak at center
        }

        #endregion

        #region KalmanFilter Tests

        [Fact]
        public void KalmanFilter_ConstantInput_ConvergesToValue()
        {
            // Arrange
            var kalman = new MathService.KalmanFilter(
                A: 1.0, H: 1.0, Q: 0.01, R: 0.1,
                initial_P: 1.0, initial_x: 0.0);

            // Act - Feed constant value multiple times
            double lastOutput = 0;
            for (int i = 0; i < 100; i++)
            {
                lastOutput = kalman.Output(10.0);
            }

            // Assert - Should converge close to 10
            Assert.True(Math.Abs(lastOutput - 10.0) < 0.5, $"Should converge to 10, got {lastOutput}");
        }

        [Fact]
        public void KalmanFilter_NoisyInput_SmoothsOutput()
        {
            // Arrange
            var kalman = new MathService.KalmanFilter(
                A: 1.0, H: 1.0, Q: 0.1, R: 1.0,
                initial_P: 1.0, initial_x: 0.0);
            var random = new Random(42);

            // Act - Feed noisy input (true value = 5, noise ±2)
            double sumSquaredError = 0;
            int count = 100;
            for (int i = 0; i < count; i++)
            {
                double trueValue = 5.0;
                double noise = (random.NextDouble() - 0.5) * 4; // ±2
                double noisyInput = trueValue + noise;
                double filtered = kalman.Output(noisyInput);

                if (i > 20) // After warmup
                {
                    double error = filtered - trueValue;
                    sumSquaredError += error * error;
                }
            }

            // Assert - RMS error should be less than input noise
            double rmsError = Math.Sqrt(sumSquaredError / (count - 20));
            Assert.True(rmsError < 1.5, $"Filtered RMS error {rmsError} should be less than noise");
        }

        [Fact]
        public void KalmanFilter_SetR_UpdatesNoiseParameter()
        {
            // Arrange
            var kalman = new MathService.KalmanFilter(
                A: 1.0, H: 1.0, Q: 0.1, R: 1.0,
                initial_P: 1.0, initial_x: 0.0);

            // Act
            kalman.SetR(5.0);

            // Assert
            Assert.Equal(5.0, kalman.GetR());
        }

        [Fact]
        public void KalmanFilter_SetQ_UpdatesProcessNoise()
        {
            // Arrange
            var kalman = new MathService.KalmanFilter(
                A: 1.0, H: 1.0, Q: 0.1, R: 1.0,
                initial_P: 1.0, initial_x: 0.0);

            // Act
            kalman.SetQ(0.5);

            // Assert
            Assert.Equal(0.5, kalman.GetQ());
        }

        [Fact]
        public void KalmanFilter_HighR_SlowerResponse()
        {
            // Arrange - High R = trust model more, slower response to changes
            var kalmanHighR = new MathService.KalmanFilter(
                A: 1.0, H: 1.0, Q: 0.01, R: 10.0,
                initial_P: 1.0, initial_x: 0.0);

            var kalmanLowR = new MathService.KalmanFilter(
                A: 1.0, H: 1.0, Q: 0.01, R: 0.1,
                initial_P: 1.0, initial_x: 0.0);

            // Act - Single step from 0 to 10
            double outputHighR = kalmanHighR.Output(10.0);
            double outputLowR = kalmanLowR.Output(10.0);

            // Assert - Low R should respond faster (closer to 10)
            Assert.True(outputLowR > outputHighR,
                $"Low R output {outputLowR} should be > High R output {outputHighR}");
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void CalculateMoments_EmptyArrays_HandledGracefully()
        {
            // Arrange
            double[] xData = Array.Empty<double>();
            double[] yData = Array.Empty<double>();

            // Act & Assert - Should not throw
            var (mean, sigma, peak) = _mathService.CalculateMoments(xData, yData);

            // Peak will be double.MinValue for empty array, mean/sigma could be NaN or 0
            Assert.True(double.IsNaN(mean) || mean == 0);
        }

        [Fact]
        public void CalculateMoments_SingleElement_ReturnsCorrectValues()
        {
            // Arrange
            double[] xData = { 5.0 };
            double[] yData = { 10.0 };

            // Act
            var (mean, sigma, peak) = _mathService.CalculateMoments(xData, yData);

            // Assert
            Assert.Equal(5.0, mean);
            Assert.Equal(0.0, sigma); // Single point has no spread
            Assert.Equal(10.0, peak);
        }

        #endregion
    }
}
