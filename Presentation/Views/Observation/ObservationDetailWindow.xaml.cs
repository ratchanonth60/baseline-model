using System.Linq;
using System.Windows;
using System.Windows.Input; // Added for Keyboard/Mouse modifiers
using ScottPlot;
using BaselineMode.WPF.Core.Interfaces;
using BaselineMode.WPF.Presentation.ViewModels.Shared; // Added for ChannelViewModel

namespace BaselineMode.WPF.Views.Observation
{
    public partial class ObservationDetailWindow : Window
    {
        private readonly IMathService? _fittingService;
        private readonly ChannelViewModel _viewModel;

        public ObservationDetailWindow(IMathService fittingService)
        {
            InitializeComponent();
            _fittingService = fittingService;

            // Initialize ViewModel
            _viewModel = new ChannelViewModel
            {
                MathService = _fittingService,
                PlotControl = DetailPlot,
                ResidualPlotControl = ResidualPlot
            };

            DataContext = _viewModel;

            // Setup Shift+Click for Peak Locking
            DetailPlot.MouseDown += (s, e) =>
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift && _viewModel.IsManualMode)
                {
                    var (mouseX, mouseY) = DetailPlot.GetMouseCoordinates();
                    _viewModel.ManualMu = mouseX;
                    _viewModel.IsLockedMu = true;
                }
            };
        }

        public void ShowHistogram(double[] data, string title, bool showFit = true, System.Drawing.Color? color = null,
            double xMin = 0, double xMax = 0, int binCount = 4096, double barWidthMultiplier = 1.0,
            int xAxisIndex = 0, double slope = 0.000427, double offset = 0.0)
        {
            _viewModel.Title = title;
            _viewModel.BarWidthMultiplier = barWidthMultiplier;

            // Prepare Data
            var filteredData = data.Where(v => v > 0).ToArray();
            if (filteredData.Length == 0)
            {
                _viewModel.StatsText = "No positive data available.";
                return;
            }

            // 1. Determine ADC Range (for Histogramming)
            // Inputs xMin/xMax are in User Units. We convert them to ADC.
            double adcXMin = xMin;
            double adcXMax = xMax > 0 ? xMax : 4096;

            if (xAxisIndex == 1) // Voltage (mV) -> ADC
            {
                // V = (ADC / 16383) * 5000  => ADC = (V * 16383) / 5000
                adcXMin = (xMin / 5000.0) * 16383.0;
                adcXMax = (xMax / 5000.0) * 16383.0;
            }
            else if (xAxisIndex == 2 && slope != 0) // Energy (MeV) -> ADC
            {
                // E = ADC*slope + offset => ADC = (E - offset) / slope
                adcXMin = (xMin - offset) / slope;
                adcXMax = (xMax - offset) / slope;
            }

            // Defaults if invalid or zero range
            if (adcXMax <= adcXMin)
            {
                adcXMin = 0;
                adcXMax = 16384;
            }

            // 2. Determine Bin Count (Resolution) from ADC Range
            int targetBinCount = (int)(adcXMax - adcXMin);
            if (targetBinCount > 16384) targetBinCount = 16384;
            if (targetBinCount < 100) targetBinCount = 100;

            // Allow override if valid binCount passed and not using Energy/Voltage (which distorts xMax int)
            int usedBinCount = (xAxisIndex > 0 || binCount < 100) ? targetBinCount : binCount;

            // 3. Generate Histogram in ADC Space
            var (hist, binEdges) = ScottPlot.Statistics.Common.Histogram(filteredData, min: adcXMin, max: adcXMax, binCount: usedBinCount);

            double[] binMidpoints = new double[hist.Length];
            for (int i = 0; i < hist.Length; i++)
                binMidpoints[i] = (binEdges[i] + binEdges[i + 1]) / 2.0;

            // 4. Transform for Display (ADC -> User Unit)
            string xLabel = "ADC Channel";
            double displayXMin = adcXMin;
            double displayXMax = adcXMax;

            if (xAxisIndex == 1) // ADC -> Voltage
            {
                for (int i = 0; i < binMidpoints.Length; i++)
                    binMidpoints[i] = (binMidpoints[i] / 16383.0) * 5000.0;

                xLabel = "Voltage (mV)";
                displayXMin = (adcXMin / 16383.0) * 5000.0;
                displayXMax = (adcXMax / 16383.0) * 5000.0;
            }
            else if (xAxisIndex == 2) // ADC -> Energy
            {
                for (int i = 0; i < binMidpoints.Length; i++)
                    binMidpoints[i] = (binMidpoints[i] * slope) + offset;

                xLabel = "Energy (MeV)";
                displayXMin = (adcXMin * slope) + offset;
                displayXMax = (adcXMax * slope) + offset;
            }

            _viewModel.BinCenters = binMidpoints;
            _viewModel.Counts = hist;
            _viewModel.RawCounts = hist;

            // Initial plot setup
            var dataColor = color ?? System.Drawing.Color.FromArgb(255, 0, 150, 136);

            // Auto-Fit (HEMG) for initialization if requested
            if (showFit && _fittingService != null)
            {
                PerformAutoFit(binMidpoints, hist);
            }

            // Final Render
            _viewModel.RenderPlot(
                System.Drawing.Color.FromArgb(255, 37, 37, 38), // Figure Bg
                System.Drawing.Color.FromArgb(255, 37, 37, 38), // Data Bg
                System.Drawing.Color.White,                       // Fore Color
                dataColor,                                        // Series Color
                displayXMin, displayXMax, xLabel
            );
        }

        private void PerformAutoFit(double[] x, double[] y)
        {
            try
            {
                // Crop for fitting logic (same as old code)
                double maxVal = y.Max();
                int peakIdx = Array.IndexOf(y, maxVal);
                int win = 100;
                int start = Math.Max(0, peakIdx - win);
                int end = Math.Min(y.Length - 1, peakIdx + win);
                int len = end - start;

                if (len < 3) return;

                double[] xFit = [.. x.Skip(start).Take(len)];
                double[] yFit = [.. y.Skip(start).Take(len)];

                if (_fittingService == null) return;

                // Prefer HEMG
                var fitResult = _fittingService.HemgDoubleSidedFit(xFit, yFit);

                if (fitResult.IsValid)
                {
                    // Generate fit curve for the full range (x) using the fitted parameters
                    // Parameters order: A, Mu, Sigma, TauL, TauR, EtaL, EtaR
                    var parameters = new double[]
                    {
                        fitResult.Peak,
                        fitResult.Mu,
                        fitResult.Sigma,
                        fitResult.TauL1,
                        fitResult.TauR1,
                        fitResult.EtaL1,
                        fitResult.EtaR1
                    };

                    var fullFitCurve = _fittingService.GenerateHemgCurve(x, parameters);

                    var fitData = new ChannelViewModel.FitData
                    {
                        Curve = fullFitCurve,
                        Color = System.Drawing.Color.Lime,
                        Label = "HEMG-D"
                    };
                    _viewModel.ActiveFits["HEMG-D"] = fitData;

                    // Update ViewModel Stats
                    _viewModel.Mu = fitResult.Mu;
                    _viewModel.Sigma = fitResult.Sigma;
                    _viewModel.Peak = fitResult.Peak;
                    _viewModel.FWHM = fitResult.FWHM;
                    _viewModel.Resolution = fitResult.Resolution;
                    _viewModel.StatsText = $"μ={fitResult.Mu:F2} σ={fitResult.Sigma:F2} FWHM={fitResult.FWHM:F2} Res={fitResult.Resolution:F2}%";

                    // Also populate Manual initial values
                    _viewModel.ManualA = fitResult.Peak;
                    _viewModel.ManualMu = fitResult.Mu;
                    _viewModel.ManualSigma = fitResult.Sigma;
                    _viewModel.ManualTauL = fitResult.TauL1;
                    _viewModel.ManualTauR = fitResult.TauR1;
                    _viewModel.ManualEtaL = fitResult.EtaL1;
                    _viewModel.ManualEtaR = fitResult.EtaR1;
                }
            }
            catch { }
        }

        public void SetColorTheme(System.Drawing.Color figureBg, System.Drawing.Color dataBg, System.Drawing.Color fgColor)
        {
            // This will be used when RenderPlot is called
        }
    }
}
