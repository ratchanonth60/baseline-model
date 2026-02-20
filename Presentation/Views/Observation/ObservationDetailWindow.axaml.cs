using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BaselineMode.WPF.Core.Helpers;
using BaselineMode.WPF.Core.Interfaces;
using BaselineMode.WPF.Core.Models.Baseline;
using BaselineMode.WPF.Core.Models.Observation; // For HemgFitResult if needed
using ScottPlot;
using ScottPlot.Avalonia;

namespace BaselineMode.WPF.Views.Observation
{
    public partial class ObservationDetailWindow : Window
    {
        private readonly IMathService _mathService;

        // State for refreshing
        private double[]? _currentData;
        private string _currentTitle = "";
        private ScottPlot.Color? _currentBarColor;
        private AnalysisAxisConfig? _currentAxisConfig;
        private bool _showFit = false;

        // Store last auto-fit parameters for manual mode initialization
        private double _lastAutoAmplitude = 100;
        private double _lastAutoMean = 2048;
        private double _lastAutoSigma = 50;
        private double _lastAutoTau = 10;

        public ObservationDetailWindow()
        {
            InitializeComponent();
            _mathService = null!;
        }

        public ObservationDetailWindow(IMathService mathService)
        {
            InitializeComponent();
            _mathService = mathService;

            // Subscribe to slider changes
            SldrAmplitude.PropertyChanged += OnSliderChanged;
            SldrMean.PropertyChanged += OnSliderChanged;
            SldrSigma.PropertyChanged += OnSliderChanged;
            SldrTau.PropertyChanged += OnSliderChanged;

            // Subscribe to PointerPressed for zoom/click interactions
            DetailPlot.PointerPressed += OnPlotPointerPressed;
        }

        public void SetColorTheme(ScottPlot.Color figure, ScottPlot.Color data, ScottPlot.Color foreground)
        {
            DetailPlot.Plot.FigureBackground.Color = figure;
            DetailPlot.Plot.DataBackground.Color = data;

            DetailPlot.Plot.Axes.Color(foreground);
            DetailPlot.Plot.Axes.Title.Label.ForeColor = foreground;
            DetailPlot.Plot.Axes.Bottom.Label.ForeColor = foreground;
            DetailPlot.Plot.Axes.Left.Label.ForeColor = foreground;

            DetailPlot.Refresh();
        }

        public void ShowHistogram(double[] data, string title, bool showFit, ScottPlot.Color? barColor, AnalysisAxisConfig? config = null)
        {
            _currentData = data;
            _currentTitle = title;
            _showFit = showFit;
            _currentBarColor = barColor;
            _currentAxisConfig = config;

            TxtTitle.Text = title;
            Title = $"Detail: {title}";

            if (config != null)
            {
                TxtXMin.Text = config.XMin.ToString();
                TxtXMax.Text = config.XMax.ToString();
                TxtBinCount.Text = config.BinCount.ToString();
            }

            RefreshPlot();
        }

        private void OnSliderChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property.Name != "Value") return;

            TxtAmplitudeVal.Text = $"{SldrAmplitude.Value:F1}";
            TxtMeanVal.Text = $"{SldrMean.Value:F1}";
            TxtSigmaVal.Text = $"{SldrSigma.Value:F2}";
            TxtTauVal.Text = $"{SldrTau.Value:F2}";

            if (ChkManualFit?.IsChecked == true)
            {
                RefreshPlot();
            }
        }

        private void OnPlotPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(DetailPlot);
            if (point.Properties.IsMiddleButtonPressed ||
                (point.Properties.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
            {
                DetailPlot.Plot.Axes.AutoScale();
                DetailPlot.Refresh();
            }
        }

        private void CmbFitMethod_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            RefreshPlot();
        }

        private void ChkManualFit_Changed(object? sender, RoutedEventArgs e)
        {
            bool isManual = ChkManualFit?.IsChecked == true;
            ManualFitPanel.IsVisible = isManual;

            if (isManual)
            {
                // Initialize sliders with last auto-fit params
                SldrAmplitude.Value = _lastAutoAmplitude;
                SldrMean.Value = _lastAutoMean;
                SldrSigma.Value = _lastAutoSigma;
                SldrTau.Value = _lastAutoTau;
            }

            RefreshPlot();
        }

        private void BtnRefresh_Click(object? sender, RoutedEventArgs e)
        {
            RefreshPlot();
        }

        private void BtnClose_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void RefreshPlot()
        {
            if (DetailPlot == null) return;

            DetailPlot.Plot.Clear();

            // Re-apply theme if needed or assume it persists. 
            // Note: Clear() removes plottables but keeps generic layout/styles usually, 
            // but in ScottPlot 5 specific axis customization might be reset? 
            // Better to re-apply basic labels. Theme colors persist in Figure/Data background.

            DetailPlot.Plot.Title(_currentTitle);
            DetailPlot.Plot.Axes.Left.Label.Text = "Count";
            DetailPlot.Plot.Axes.Bottom.Label.Text = "ADC Channel";

            if (_currentData == null || _currentData.Length == 0)
            {
                DetailPlot.Plot.Add.Text("No data", 0, 0);
                DetailPlot.Refresh();
                return;
            }

            // Parse params
            if (!int.TryParse(TxtBinCount?.Text, out int binCount)) binCount = 256;
            if (!double.TryParse(TxtXMin?.Text, out double xMin)) xMin = 0;
            if (!double.TryParse(TxtXMax?.Text, out double xMax)) xMax = 4096;

            var filteredData = _currentData.Where(v => v >= xMin && v <= xMax).ToArray();
            if (filteredData.Length == 0)
            {
                DetailPlot.Plot.Add.Text("No data in range", 0, 0);
                DetailPlot.Refresh();
                return;
            }

            // Create histogram
            var (hist, binEdges) = _mathService.CalculateHistogram(filteredData, min: xMin, max: xMax, binCount: binCount);
            double[] binMidpoints = new double[hist.Length];
            for (int i = 0; i < hist.Length; i++)
                binMidpoints[i] = (binEdges[i] + binEdges[i + 1]) / 2.0;

            // Plot
            var bars = new ScottPlot.Bar[hist.Length];
            for (int i = 0; i < hist.Length; i++)
            {
                bars[i] = new ScottPlot.Bar() { Position = binMidpoints[i], Value = hist[i] };
            }

            var barPlot = DetailPlot.Plot.Add.Bars(bars);
            double barWidth = (xMax - xMin) / binCount;
            // ScottPlot 5 Bars handle width differently (based on axis units if not specified? No, usually auto). 
            // But we created specific Bars with Position. We can't set "BarWidth" on the whole plot easily if positions are varying? 
            // Actually BarPlot has a generic width property maybe? Or the bars themselves do?
            // In SP5, Bar object has no width. 
            // Wait, `Add.Bars(bars)` returns a `BarPlot`. 
            // We might need to adjust logic. 
            // But for now, default width might be fine or we can rely on standard behavior.
            // If we want exact width, we might need to use `Add.Bars(values, positions)` if available, or just let it be.
            // Let's check color.

            if (_currentBarColor.HasValue)
                barPlot.Color = _currentBarColor.Value;
            else
                barPlot.Color = ScottPlot.Colors.Cyan;

            // Fitting
            var selectedFit = (CmbFitMethod?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Gaussian";

            if (ChkManualFit?.IsChecked == true)
            {
                // Manual mode: use slider values
                double amplitude = SldrAmplitude.Value;
                double mean = SldrMean.Value;
                double sigma = SldrSigma.Value;
                double tau = SldrTau.Value;

                double[] fitCurve;
                if (selectedFit == "HEMG")
                {
                    // Pack parameters: A, Mu, Sigma, TauL, TauR, EtaL, EtaR
                    double[] paramsArr = { amplitude, mean, sigma, tau, tau, 0.5, 0.5 };
                    fitCurve = _mathService.GenerateHemgCurve(binMidpoints, paramsArr);
                }
                else if (selectedFit == "Lorentzian")
                {
                    fitCurve = _mathService.GenerateLorentzianCurve(binMidpoints, amplitude, mean, sigma);
                }
                else
                {
                    fitCurve = _mathService.GenerateGaussianCurve(binMidpoints, amplitude, mean, sigma);
                }

                if (fitCurve != null && fitCurve.Length == binMidpoints.Length && !fitCurve.Any(double.IsNaN))
                {
                    var sp = DetailPlot.Plot.Add.Scatter(binMidpoints, fitCurve);
                    sp.Color = ScottPlot.Colors.Yellow;
                    sp.LineWidth = 2;
                }
            }
            else
            {
                // Auto-fit
                try
                {
                    // Crop around peak
                    double maxVal = hist.Max();
                    int peakIdx = Array.IndexOf(hist, maxVal);
                    int win = Math.Max(20, binCount / 4);
                    int start = Math.Max(0, peakIdx - win);
                    int end = Math.Min(hist.Length - 1, peakIdx + win);
                    int len = end - start;

                    if (len > 3)
                    {
                        double[] xFit = [.. binMidpoints.Skip(start).Take(len)];
                        double[] yFit = [.. hist.Skip(start).Take(len)];

                        BaselineMode.WPF.Core.Models.Baseline.FittingResult? fitResult = null;
                        if (selectedFit == "HEMG")
                        {
                            fitResult = _mathService.HemgDoubleSidedFit(xFit, yFit);
                        }
                        else if (selectedFit == "Lorentzian")
                        {
                            fitResult = _mathService.LorentzianFit(xFit, yFit);
                        }
                        else
                        {
                            fitResult = _mathService.GaussianFit(xFit, yFit);
                        }

                        if (fitResult != null && fitResult.IsValid && fitResult.FitCurve != null && fitResult.FitCurve.Length == xFit.Length)
                        {
                            if (!fitResult.FitCurve.Any(double.IsNaN))
                            {
                                var sp = DetailPlot.Plot.Add.Scatter(xFit, fitResult.FitCurve);
                                sp.Color = ScottPlot.Colors.Red;
                                sp.LineWidth = 2;

                                var mp = DetailPlot.Plot.Add.Marker(fitResult.Mu, fitResult.Peak);
                                mp.Color = ScottPlot.Colors.Yellow;
                                mp.Size = 8;
                                mp.Shape = MarkerShape.FilledCircle; // Explicitly set shape

                                TxtDetailPeak.Text = $"{fitResult.Peak:F2}";
                                TxtDetailMean.Text = $"{fitResult.Mu:F2}";
                                TxtDetailRMS.Text = $"{fitResult.Sigma:F2}";
                                TxtDetailFWHM.Text = $"{fitResult.FWHM:F2}";
                                TxtDetailRes.Text = $"{fitResult.Resolution:F2}%";

                                // Store for manual mode
                                _lastAutoAmplitude = fitResult.Peak;
                                _lastAutoMean = fitResult.Mu;
                                _lastAutoSigma = fitResult.Sigma;
                                if (fitResult.TauL1 > 0)
                                {
                                    _lastAutoTau = fitResult.TauL1;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Detail fit error: {ex.Message}");
                }
            }

            DetailPlot.Plot.Axes.SetLimits(left: xMin, right: xMax, bottom: 0, top: null);
            DetailPlot.Refresh();
        }
    }
}
