using System.Linq;
using System.Windows;
using ScottPlot;
using BaselineMode.WPF.Core.Interfaces;

namespace BaselineMode.WPF.Views.Observation
{
    public partial class ObservationDetailWindow : Window
    {
        private readonly IMathService? _fittingService;

        public ObservationDetailWindow()
        {
            InitializeComponent();
        }

        private double[]? _currentData;
        private string _currentTitle = string.Empty;
        private System.Drawing.Color _dataColor = System.Drawing.Color.FromArgb(255, 0, 150, 136);
        private bool _isUpdatingUi;

        private double _xMin = 0;
        private double _xMax = 4096;
        private int _binCount = 4096;
        private double _barWidthMultiplier = 1.0;

        public ObservationDetailWindow(IMathService fittingService) : this()
        {
            _fittingService = fittingService;
            ChkShowGauss.Checked += (s, e) => UpdatePlot();
            ChkShowGauss.Unchecked += (s, e) => UpdatePlot();
            ChkShowLorentz.Checked += (s, e) => UpdatePlot();
            ChkShowLorentz.Unchecked += (s, e) => UpdatePlot();
            ChkShowHemg.Checked += (s, e) => UpdatePlot();
            ChkShowHemg.Unchecked += (s, e) => UpdatePlot();
        }

        public void ShowHistogram(double[] data, string title, bool showFit = true, System.Drawing.Color? color = null,
            double xMin = 0, double xMax = 4096, int binCount = 4096, double barWidthMultiplier = 1.0)
        {
            _currentData = data;
            _currentTitle = title;
            if (color.HasValue) _dataColor = color.Value;
            _xMin = xMin;
            _xMax = xMax;
            _binCount = binCount;
            _barWidthMultiplier = barWidthMultiplier;

            TitleText.Text = title;
            Title = $"Detail View - {title}";

            _isUpdatingUi = true;
            ChkShowGauss.IsChecked = showFit; // Default Gaussian
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
            DetailPlot.Plot.XAxis.Label(label: "ADC Channel", color: _fgColor);
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
            var (hist, binEdges) = ScottPlot.Statistics.Common.Histogram(filteredData, min: _xMin, max: _xMax, binCount: _binCount);
            double[] binMidpoints = new double[hist.Length];
            for (int i = 0; i < hist.Length; i++)
                binMidpoints[i] = (binEdges[i] + binEdges[i + 1]) / 2.0;

            // วาด Bar Chart (Raw Data)
            double binWidth = binEdges[1] - binEdges[0];
            var bar = DetailPlot.Plot.AddBar(hist, binMidpoints);
            bar.BarWidth = binWidth * _barWidthMultiplier;
            bar.FillColor = _dataColor;

            // Reset Stats
            TxtPeak.Text = "-";
            TxtMean.Text = "-";
            TxtRMS.Text = "-";
            TxtFWHM.Text = "-";
            TxtResolution.Text = "-";

            // Multi-Fit Logic
            if (_fittingService != null)
            {
                var fitConfigs = new[]
                {
                    (isEnabled: ChkShowGauss.IsChecked == true, name: "Gaussian", color: System.Drawing.Color.Red, fitFunc: (System.Func<double[], double[], Core.Models.Baseline.FittingResult>)_fittingService.GaussianFit),
                    (isEnabled: ChkShowLorentz.IsChecked == true, name: "Lorentzian", color: System.Drawing.Color.Cyan, fitFunc: (System.Func<double[], double[], Core.Models.Baseline.FittingResult>)_fittingService.LorentzianFit),
                    (isEnabled: ChkShowHemg.IsChecked == true, name: "HEMG", color: System.Drawing.Color.Lime, fitFunc: (System.Func<double[], double[], Core.Models.Baseline.FittingResult>)_fittingService.HemgDoubleSidedFit)
                };

                foreach (var cfg in fitConfigs.Where(c => c.isEnabled))
                {
                    try
                    {
                        double maxVal = hist.Max();
                        int peakIdx = Array.IndexOf(hist, maxVal);
                        int win = 100;
                        int start = Math.Max(0, peakIdx - win);
                        int end = Math.Min(hist.Length - 1, peakIdx + win);
                        int len = end - start;
                        if (len < 3) continue;

                        double[] xFit = binMidpoints.Skip(start).Take(len).ToArray();
                        double[] yFit = hist.Skip(start).Take(len).ToArray();

                        var fitResult = cfg.fitFunc(xFit, yFit);

                        if (fitResult?.FitCurve != null && fitResult.Peak > 0)
                        {
                            var scatter = DetailPlot.Plot.AddScatter(xFit, fitResult.FitCurve, cfg.color, lineWidth: 2, markerSize: 0, label: cfg.name);

                            // Highlight peak for active fit
                            DetailPlot.Plot.AddPoint(fitResult.Mu, fitResult.Peak, System.Drawing.Color.Yellow, 8);

                            // Update stats labels (last enabled wins)
                            TxtPeak.Text = $"{fitResult.Peak:F0}";
                            TxtMean.Text = $"{fitResult.Mu:F2}";
                            TxtRMS.Text = $"{fitResult.Sigma:F2}";
                            TxtFWHM.Text = $"{fitResult.FWHM:F2}";
                            TxtResolution.Text = $"{fitResult.Resolution:F2}%";
                        }
                    }
                    catch { }
                }

                if (fitConfigs.Count(c => c.isEnabled) > 1)
                    DetailPlot.Plot.Legend(location: ScottPlot.Alignment.UpperRight);
            }

            if (TxtPeak.Text == "-") // If no fit was successful or enabled
            {
                TxtPeak.Text = $"{hist.Max()}";
                TxtMean.Text = $"{filteredData.Average():F2}";
                double avg = filteredData.Average();
                double stdDev = Math.Sqrt(filteredData.Sum(d => Math.Pow(d - avg, 2)) / filteredData.Length);
                TxtRMS.Text = $"{stdDev:F2}";
            }

            DetailPlot.Plot.SetAxisLimits(xMin: _xMin, xMax: _xMax, yMin: 0);
            DetailPlot.Refresh();
        }
    }
}
