using System.Linq;
using System.Windows;
using ScottPlot;
using BaselineMode.WPF.Core.Interfaces;

namespace BaselineMode.WPF.Presentation.Views.Calibration
{
    public partial class CalibrationDetailWindow : Window
    {
        private readonly IFittingService? _fittingService;

        public CalibrationDetailWindow()
        {
            InitializeComponent();
        }

        private double[]? _currentData;
        private string _currentTitle = string.Empty;
        private System.Drawing.Color _dataColor = System.Drawing.Color.FromArgb(255, 0, 150, 136);
        private bool _isUpdatingUi;

        public CalibrationDetailWindow(IFittingService fittingService) : this()
        {
            _fittingService = fittingService;
            ChkShowFit.Checked += (s, e) => UpdatePlot();
            ChkShowFit.Unchecked += (s, e) => UpdatePlot();
        }

        private string _xLabel = "ADC Channel";

        public void ShowHistogram(double[] data, string title, bool showFit = true, System.Drawing.Color? color = null, string xLabel = "ADC Channel")
        {
            _currentData = data;
            _currentTitle = title;
            _xLabel = xLabel;
            if (color.HasValue) _dataColor = color.Value;
            TitleText.Text = title;
            Title = $"Detail View - {title}";

            _isUpdatingUi = true;
            ChkShowFit.IsChecked = showFit;
            _isUpdatingUi = false;

            UpdatePlot();
        }

        private System.Drawing.Color _figureBg = System.Drawing.Color.FromArgb(255, 37, 37, 38);
        private System.Drawing.Color _dataBg = System.Drawing.Color.FromArgb(255, 37, 37, 38);
        private System.Drawing.Color _fgColor = System.Drawing.Color.White;

        public void SetColorTheme(System.Drawing.Color figureBg, System.Drawing.Color dataBg, System.Drawing.Color fgColor)
        {
            _figureBg = figureBg;
            _dataBg = dataBg;
            _fgColor = fgColor;
        }

        private void UpdatePlot()
        {
            if (_isUpdatingUi) return;

            DetailPlot.Plot.Clear();

            // Apply Theme
            DetailPlot.Plot.Style(figureBackground: _figureBg, dataBackground: _dataBg);
            DetailPlot.Plot.Style(tick: _fgColor, grid: System.Drawing.Color.FromArgb(60, _fgColor.R, _fgColor.G, _fgColor.B), titleLabel: _fgColor, axisLabel: _fgColor);
            DetailPlot.Plot.XAxis.Label(label: _xLabel, color: _fgColor);
            DetailPlot.Plot.YAxis.Label(label: "Count", color: _fgColor);

            if (_currentData == null || _currentData.Length == 0)
            {
                DetailPlot.Plot.AddText("No data", 0, 0, size: 14, color: System.Drawing.Color.Gray);
                DetailPlot.Refresh();
                return;
            }

            // Filter positive values
            var filteredData = _currentData.Where(v => v > 0).ToArray();
            TxtCounts.Text = filteredData.Length.ToString("N0");

            if (filteredData.Length == 0)
            {
                DetailPlot.Plot.AddText("No positive data", 0, 0, size: 14, color: System.Drawing.Color.Gray);
                DetailPlot.Refresh();
                return;
            }

            // Create histogram
            var (hist, binEdges) = ScottPlot.Statistics.Common.Histogram(filteredData, binCount: 4096);
            double[] binMidpoints = new double[hist.Length];
            for (int i = 0; i < hist.Length; i++)
                binMidpoints[i] = (binEdges[i] + binEdges[i + 1]) / 2.0;

            var bar = DetailPlot.Plot.AddBar(hist, binMidpoints);
            bar.BarWidth = 1;
            bar.FillColor = _dataColor;
            bar.BorderColor = bar.FillColor;
            bar.BorderLineWidth = 0;

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
                    // --- 4.1 Crop around peak for better convergence ---
                    double maxVal = hist.Max();
                    int peakIdx = Array.IndexOf(hist, maxVal);
                    int win = 100;
                    int start = Math.Max(0, peakIdx - win);
                    int end = Math.Min(hist.Length - 1, peakIdx + win);
                    int len = end - start;

                    if (len > 3)
                    {
                        double[] xFit = binMidpoints.Skip(start).Take(len).ToArray();
                        double[] yFit = hist.Skip(start).Take(len).ToArray();

                        var fitResult = _fittingService.GaussianFit(xFit, yFit);
                        if (fitResult?.FitCurve != null && fitResult.FitCurve.Length == xFit.Length)
                        {
                            DetailPlot.Plot.AddScatter(xFit, fitResult.FitCurve,
                                System.Drawing.Color.FromArgb(255, 255, 82, 82), lineWidth: 2);
                            DetailPlot.Plot.AddPoint(fitResult.Mu, fitResult.Peak,
                                color: System.Drawing.Color.Yellow, size: 10);

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

            DetailPlot.Plot.SetAxisLimits(yMin: 0);
            DetailPlot.Refresh();
        }
    }
}