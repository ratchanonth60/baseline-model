using System;
using System.Linq;
using System.Windows;

namespace BaselineMode.WPF.Presentation.Views.Flux
{
    public partial class FluxDetailWindow : Window
    {
        private double[]? _xData;
        private double[]? _yData;
        private string _currentTitle = string.Empty;
        private bool _isLogScale;

        private System.Drawing.Color _figureBg = System.Drawing.Color.FromArgb(255, 37, 37, 38);
        private System.Drawing.Color _dataBg = System.Drawing.Color.FromArgb(255, 37, 37, 38);
        private System.Drawing.Color _fgColor = System.Drawing.Color.White;
        private System.Drawing.Color _seriesColor = System.Drawing.Color.FromArgb(255, 0, 150, 136);

        public FluxDetailWindow()
        {
            InitializeComponent();
            ChkLogScale.Checked += (s, e) => { _isLogScale = true; UpdatePlot(); };
            ChkLogScale.Unchecked += (s, e) => { _isLogScale = false; UpdatePlot(); };
        }

        public void SetColorTheme(System.Drawing.Color figBg, System.Drawing.Color dataBg, System.Drawing.Color fgColor, System.Drawing.Color seriesColor)
        {
            _figureBg = figBg;
            _dataBg = dataBg;
            _fgColor = fgColor;
            _seriesColor = seriesColor;
            UpdatePlot();
        }

        public void ShowFluxData(double[]? xData, double[]? yData, string title, bool isLogScale = false)
        {
            _xData = xData;
            _yData = yData;
            _currentTitle = title;
            _isLogScale = isLogScale;
            TitleText.Text = title;
            Title = $"Detail View - {title}";
            ChkLogScale.IsChecked = isLogScale;
            UpdatePlot();
        }

        private void UpdatePlot()
        {
            DetailPlot.Plot.Clear();

            // Apply Theme
            DetailPlot.Plot.Style(figureBackground: _figureBg, dataBackground: _dataBg);
            DetailPlot.Plot.Style(tick: _fgColor,
                grid: System.Drawing.Color.FromArgb(60, _fgColor.R, _fgColor.G, _fgColor.B),
                titleLabel: _fgColor, axisLabel: _fgColor);

            if (_xData == null || _yData == null || _xData.Length == 0 || _yData.Length == 0)
            {
                DetailPlot.Plot.AddText("No data", 0, 0, size: 14, color: System.Drawing.Color.Gray);
                DetailPlot.Refresh();
                TxtDataPoints.Text = "0";
                TxtMaxFlux.Text = "-";
                TxtMeanFlux.Text = "-";
                TxtTimeRange.Text = "-";
                return;
            }

            // Filter valid data
            var validPairs = _xData.Zip(_yData, (x, y) => new { x, y })
                .Where(p => !double.IsNaN(p.y) && !double.IsInfinity(p.y))
                .ToArray();

            if (validPairs.Length == 0)
            {
                DetailPlot.Plot.AddText("No valid flux data", 0, 0, size: 14, color: System.Drawing.Color.Gray);
                DetailPlot.Refresh();
                return;
            }

            double[] filteredX = [.. validPairs.Select(p => p.x)];
            double[] filteredY = [.. validPairs.Select(p => p.y)];

            // Stats
            TxtDataPoints.Text = validPairs.Length.ToString("N0");
            TxtMaxFlux.Text = filteredY.Max().ToString("F2");
            TxtMeanFlux.Text = filteredY.Average().ToString("F2");
            TxtTimeRange.Text = $"{filteredX.Min():F2} – {filteredX.Max():F2} s";

            double[] plotY;
            if (_isLogScale)
            {
                plotY = [.. filteredY.Select(y => y > 0 ? Math.Log10(y) : -10)];
                DetailPlot.Plot.YAxis.TickLabelFormat(value => $"10^{value:F0}");
            }
            else
            {
                plotY = filteredY;
                DetailPlot.Plot.SetAxisLimitsY(0, double.NaN);
            }

            var scatter = DetailPlot.Plot.AddScatter(filteredX, plotY);
            scatter.Color = _seriesColor;
            scatter.LineWidth = 2;
            scatter.MarkerSize = 4;

            DetailPlot.Plot.Title(_currentTitle);
            DetailPlot.Plot.XLabel("Cumulative Time (s)");
            DetailPlot.Plot.YLabel(_isLogScale ? "Flux Density (log₁₀)" : "Flux Density (count/m²·s)");

            DetailPlot.Plot.SetAxisLimits(xMin: 0);
            DetailPlot.Refresh();
        }
    }
}