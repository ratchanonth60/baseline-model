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
            BaselineMode.WPF.Core.Models.Baseline.AnalysisAxisConfig? axisConfig = null)
        {
            _viewModel.Title = title;
            axisConfig ??= new BaselineMode.WPF.Core.Models.Baseline.AnalysisAxisConfig
            {
                XMin = 0,
                XMax = 0,
                BinCount = 4096,
                AxisIndex = 0,
                Slope = 0.000427,
                Offset = 0.0,
                VoltageMax = 5000.0,
                AdcResolution = 16383.0,
                BarWidthMultiplier = 1.0
            };

            _viewModel.BarWidthMultiplier = axisConfig.BarWidthMultiplier;

            // Prepare Data
            var filteredData = data.Where(v => v > 0).ToArray();
            if (filteredData.Length == 0)
            {
                _viewModel.StatsText = "No positive data available.";
                return;
            }

            // 1. Determine ADC Range (for Histogramming)
            // Inputs xMin/xMax are in User Units. We convert them to ADC.
            double adcXMin = axisConfig.XMin;
            double adcXMax = axisConfig.XMax > 0 ? axisConfig.XMax : 4096;

            if (axisConfig.AxisIndex == 1) // Voltage (mV) -> ADC
            {
                // V = (ADC / AdcResolution) * VoltageMax  => ADC = (V * AdcResolution) / VoltageMax
                adcXMin = (axisConfig.XMin / axisConfig.VoltageMax) * axisConfig.AdcResolution;
                adcXMax = (axisConfig.XMax / axisConfig.VoltageMax) * axisConfig.AdcResolution;
            }
            else if (axisConfig.AxisIndex == 2 && axisConfig.Slope != 0) // Energy (MeV) -> ADC
            {
                // E = ADC*slope + offset => ADC = (E - offset) / slope
                adcXMin = (axisConfig.XMin - axisConfig.Offset) / axisConfig.Slope;
                adcXMax = (axisConfig.XMax - axisConfig.Offset) / axisConfig.Slope;
            }

            // Defaults if invalid or zero range
            if (adcXMax <= adcXMin)
            {
                adcXMin = 0;
                adcXMax = (int)axisConfig.AdcResolution + 1;
            }

            // 2. Determine Bin Count (Resolution) from ADC Range
            int targetBinCount = (int)(adcXMax - adcXMin);
            if (targetBinCount > (int)axisConfig.AdcResolution + 1) targetBinCount = (int)axisConfig.AdcResolution + 1;
            if (targetBinCount < 100) targetBinCount = 100;

            // Allow override if valid binCount passed and not using Energy/Voltage (which distorts xMax int)
            // Re-using passed BinCount if reasonable
            int usedBinCount = (axisConfig.AxisIndex > 0 || axisConfig.BinCount < 100) ? targetBinCount : axisConfig.BinCount;

            // 3. Generate Histogram in ADC Space
            var (hist, binEdges) = ScottPlot.Statistics.Common.Histogram(filteredData, min: adcXMin, max: adcXMax, binCount: usedBinCount);

            double[] binMidpoints = new double[hist.Length];
            for (int i = 0; i < hist.Length; i++)
                binMidpoints[i] = (binEdges[i] + binEdges[i + 1]) / 2.0;

            // 4. Transform for Display (ADC -> User Unit)
            string xLabel = "ADC Channel";
            double displayXMin = adcXMin;
            double displayXMax = adcXMax;

            if (axisConfig.AxisIndex == 1) // ADC -> Voltage
            {
                for (int i = 0; i < binMidpoints.Length; i++)
                    binMidpoints[i] = (binMidpoints[i] / axisConfig.AdcResolution) * axisConfig.VoltageMax;

                xLabel = "Voltage (mV)";
                displayXMin = (adcXMin / axisConfig.AdcResolution) * axisConfig.VoltageMax;
                displayXMax = (adcXMax / axisConfig.AdcResolution) * axisConfig.VoltageMax;
            }
            else if (axisConfig.AxisIndex == 2) // ADC -> Energy
            {
                for (int i = 0; i < binMidpoints.Length; i++)
                    binMidpoints[i] = (binMidpoints[i] * axisConfig.Slope) + axisConfig.Offset;

                xLabel = "Energy (MeV)";
                displayXMin = (adcXMin * axisConfig.Slope) + axisConfig.Offset;
                displayXMax = (adcXMax * axisConfig.Slope) + axisConfig.Offset;
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
                        fitResult.A,
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
                        Label = "HEMG-D",
                        A = fitResult.A,
                        Mu = fitResult.Mu,
                        Sigma = fitResult.Sigma,
                        TauL = fitResult.TauL1,
                        TauR = fitResult.TauR1,
                        EtaL = fitResult.EtaL1,
                        EtaR = fitResult.EtaR1
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
                    _viewModel.ManualA = fitResult.A;
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
