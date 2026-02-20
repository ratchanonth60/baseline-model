using System;
using System.Linq;
using Avalonia.Controls;
using ScottPlot.Avalonia;

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
            ChkLogScale.IsCheckedChanged += (s, e) =>
            {
                _isLogScale = ChkLogScale.IsChecked == true;
                UpdatePlot();
            };
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
            DetailPlot.Plot.FigureBackground.Color = new ScottPlot.Color(_figureBg.R, _figureBg.G, _figureBg.B, _figureBg.A);
            DetailPlot.Plot.DataBackground.Color = new ScottPlot.Color(_dataBg.R, _dataBg.G, _dataBg.B, _dataBg.A);

            var spFgColor = new ScottPlot.Color(_fgColor.R, _fgColor.G, _fgColor.B, _fgColor.A);
            DetailPlot.Plot.Axes.Color(spFgColor);
            DetailPlot.Plot.Grid.MajorLineColor = new ScottPlot.Color(_fgColor.R, _fgColor.G, _fgColor.B, (byte)60);

            if (_xData == null || _yData == null || _xData.Length == 0 || _yData.Length == 0)
            {
                var txt = DetailPlot.Plot.Add.Text("No data", 0, 0);
                txt.LabelFontSize = 14;
                txt.LabelFontColor = ScottPlot.Colors.Gray;
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
                var txt = DetailPlot.Plot.Add.Text("No valid flux data", 0, 0);
                txt.LabelFontSize = 14;
                txt.LabelFontColor = ScottPlot.Colors.Gray;
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
                if (DetailPlot.Plot.Axes.Left.TickGenerator is ScottPlot.TickGenerators.NumericAutomatic tickGen)
                {
                    tickGen.LabelFormatter = new Func<double, string>(value => $"10^{value:F0}");
                }
            }
            else
            {
                plotY = filteredY;
                DetailPlot.Plot.Axes.SetLimits(bottom: 0, top: null);
            }

            var scatter = DetailPlot.Plot.Add.Scatter(filteredX, plotY);
            scatter.Color = new ScottPlot.Color(_seriesColor.R, _seriesColor.G, _seriesColor.B, _seriesColor.A);
            scatter.LineWidth = 2;
            scatter.MarkerSize = 4;

            DetailPlot.Plot.Title(_currentTitle);
            DetailPlot.Plot.XLabel("Cumulative Time (s)");
            DetailPlot.Plot.YLabel(_isLogScale ? "Flux Density (log₁₀)" : "Flux Density (count/m²·s)");

            DetailPlot.Plot.Axes.SetLimits(left: 0);
            DetailPlot.Refresh();
        }
    }
}
