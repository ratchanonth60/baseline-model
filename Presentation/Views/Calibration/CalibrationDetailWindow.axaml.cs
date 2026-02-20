using System;
using System.Linq;
using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using BaselineMode.WPF.Core.Interfaces;
using BaselineMode.WPF.Core.Helpers;

namespace BaselineMode.WPF.Presentation.Views.Calibration
{
    public partial class CalibrationDetailWindow : Window
    {
        private readonly IFittingService? _fittingService;
        private readonly IMathService? _mathService;

        public CalibrationDetailWindow()
        {
            InitializeComponent();
        }

        private double[]? _currentData;
        private string _currentTitle = string.Empty;
        private ScottPlot.Color _dataColor = ScottPlot.Colors.Teal;
        private bool _isUpdatingUi;

        public CalibrationDetailWindow(IFittingService fittingService, IMathService mathService) : this()
        {
            _fittingService = fittingService;
            _mathService = mathService;
            ChkShowFit.IsCheckedChanged += (s, e) => UpdatePlot();
        }

        private string _xLabel = "ADC Channel";

        public void ShowHistogram(double[] data, string title, bool showFit = true, System.Drawing.Color? color = null, string xLabel = "ADC Channel")
        {
            _currentData = data;
            _currentTitle = title;
            _xLabel = xLabel;
            if (color.HasValue) _dataColor = ColorHelper.ToScottPlotColor(color.Value);
            TitleText.Text = title;
            Title = $"Detail View - {title}";

            _isUpdatingUi = true;
            ChkShowFit.IsChecked = showFit;
            _isUpdatingUi = false;

            UpdatePlot();
        }

        private ScottPlot.Color _figureBg = ScottPlot.Color.FromHex("#252526");
        private ScottPlot.Color _dataBg = ScottPlot.Color.FromHex("#252526");
        private ScottPlot.Color _fgColor = ScottPlot.Colors.White;

        public void SetColorTheme(System.Drawing.Color figureBg, System.Drawing.Color dataBg, System.Drawing.Color fgColor)
        {
            _figureBg = ColorHelper.ToScottPlotColor(figureBg);
            _dataBg = ColorHelper.ToScottPlotColor(dataBg);
            _fgColor = ColorHelper.ToScottPlotColor(fgColor);
        }

        private void UpdatePlot()
        {
            if (_isUpdatingUi) return;

            DetailPlot.Plot.Clear();

            // Apply Theme
            DetailPlot.Plot.FigureBackground.Color = _figureBg;
            DetailPlot.Plot.DataBackground.Color = _dataBg;

            DetailPlot.Plot.Axes.Color(_fgColor);
            DetailPlot.Plot.Axes.Bottom.Label.Text = _xLabel;
            DetailPlot.Plot.Axes.Bottom.Label.ForeColor = _fgColor;
            DetailPlot.Plot.Axes.Left.Label.Text = "Count";
            DetailPlot.Plot.Axes.Left.Label.ForeColor = _fgColor;

            if (_currentData == null || _currentData.Length == 0)
            {
                DetailPlot.Plot.Add.Text("No data", 0, 0).LabelFontColor = ScottPlot.Colors.Gray;
                DetailPlot.Refresh();
                return;
            }

            // Filter positive values
            var filteredData = _currentData.Where(v => v > 0).ToArray();
            TxtCounts.Text = filteredData.Length.ToString("N0");

            if (filteredData.Length == 0)
            {
                DetailPlot.Plot.Add.Text("No positive data", 0, 0).LabelFontColor = ScottPlot.Colors.Gray;
                DetailPlot.Refresh();
                return;
            }

            // Create histogram
            if (_mathService == null)
            {
                DetailPlot.Plot.Add.Text("Math Service Unavailable", 0, 0).LabelFontColor = ScottPlot.Colors.Red;
                DetailPlot.Refresh();
                return;
            }

            var (hist, binEdges) = _mathService.CalculateHistogram(filteredData, min: 0, max: 4096, binCount: 4096);
            double[] binMidpoints = new double[hist.Length];
            for (int i = 0; i < hist.Length; i++)
                binMidpoints[i] = (binEdges[i] + binEdges[i + 1]) / 2.0;

            var bars = new ScottPlot.Bar[hist.Length];
            for (int i = 0; i < hist.Length; i++)
            {
                bars[i] = new ScottPlot.Bar() { Position = binMidpoints[i], Value = hist[i] };
            }

            var barPlot = DetailPlot.Plot.Add.Bars(bars);
            barPlot.Color = _dataColor;
            // No direct border width property on BarPlot in simple usage, keeps default

            // Reset Stats
            TxtPeak.Text = "-";
            TxtMean.Text = "-";
            TxtRMS.Text = "-";
            TxtFWHM.Text = "-";
            TxtResolution.Text = "-";

            // Try Gaussian fit if checked
            if (ChkShowFit.IsChecked == true && _fittingService != null)
            {
                try
                {
                    double maxVal = hist.Max();
                    int peakIdx = Array.IndexOf(hist, maxVal);
                    int win = 100;
                    int start = Math.Max(0, peakIdx - win);
                    int end = Math.Min(hist.Length - 1, peakIdx + win);
                    int len = end - start;

                    if (len > 3)
                    {
                        double[] xFit = [.. binMidpoints.Skip(start).Take(len)];
                        double[] yFit = [.. hist.Skip(start).Take(len)];

                        var fitResult = _fittingService.GaussianFit(xFit, yFit);
                        if (fitResult?.IsValid == true && fitResult.FitCurve != null && fitResult.FitCurve.Length == xFit.Length)
                        {
                            var sp = DetailPlot.Plot.Add.Scatter(xFit, fitResult.FitCurve);
                            sp.Color = ScottPlot.Colors.Red; // "ARGB(255, 255, 82, 82)" -> Red-ish
                            sp.LineWidth = 2;

                            var mp = DetailPlot.Plot.Add.Marker(fitResult.Mu, fitResult.Peak);
                            mp.Color = ScottPlot.Colors.Yellow;
                            mp.Size = 10;

                            TxtPeak.Text = $"{fitResult.Peak:F0}";
                            TxtMean.Text = $"{fitResult.Mu:F2}";
                            TxtRMS.Text = $"{fitResult.Sigma:F2}";
                            TxtFWHM.Text = $"{fitResult.FWHM:F2}";
                            TxtResolution.Text = $"{fitResult.Resolution:F2}%";
                        }
                    }
                }
                catch { /* Fitting failed */ }
            }
            else
            {
                // Calculate basic stats manually if fit is disabled
                TxtPeak.Text = $"{hist.Max()}";
                TxtMean.Text = $"{filteredData.Average():F2}";

                double avg = filteredData.Average();
                double sumSquares = filteredData.Sum(d => Math.Pow(d - avg, 2));
                double stdDev = Math.Sqrt(sumSquares / filteredData.Length);
                TxtRMS.Text = $"{stdDev:F2}";
            }

            DetailPlot.Plot.Axes.SetLimits(bottom: 0, top: null);
            DetailPlot.Refresh();
        }
    }
}
