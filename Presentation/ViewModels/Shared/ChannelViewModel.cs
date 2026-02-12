using CommunityToolkit.Mvvm.ComponentModel;
using ScottPlot;

namespace BaselineMode.WPF.Presentation.ViewModels.Shared
{
    public partial class ChannelViewModel : ObservableObject
    {
        public class FitData
        {
            public double[]? Curve { get; set; }
            public System.Drawing.Color Color { get; set; }
            public string Label { get; set; } = "Fit";
        }

        [ObservableProperty]
        private string _channelName = "Channel";

        [ObservableProperty]
        private string _title = "Channel";

        [ObservableProperty]
        private string _statsText = "No Data";

        public double[]? BinCenters { get; set; }
        public double[]? Counts { get; set; }
        public double[]? RawCounts { get; set; }

        [ObservableProperty]
        private bool _isFitting = false;

        // Multi-Fit Support: Dictionary of FitName -> FitData
        public Dictionary<string, FitData> ActiveFits { get; set; } = [];

        // Cache: FitType -> FitData
        private readonly Dictionary<string, FitData> _fitCache = [];

        public void CacheFit(string fitType, FitData data)
        {
            if (!_fitCache.ContainsKey(fitType))
                _fitCache[fitType] = data;
        }

        public FitData? GetCachedFit(string fitType)
        {
            return _fitCache.TryGetValue(fitType, out FitData? value) ? value : null;
        }

        [ObservableProperty]
        private bool _isLogScale = false;

        // Statistics (Primary Fit)
        public double Mu { get; set; }
        public double Sigma { get; set; }
        public double Peak { get; set; }
        public double FWHM { get; set; }
        public double Resolution { get; set; }

        public WpfPlot? PlotControl { get; set; }

        public void RenderPlot(System.Drawing.Color figBg, System.Drawing.Color dataBg, System.Drawing.Color foreColor, System.Drawing.Color seriesColor, double? xMin = null, double? xMax = null, string? xLabel = null)
        {
            if (PlotControl != null)
                RenderTo(PlotControl, figBg, dataBg, foreColor, seriesColor, xMin, xMax, xLabel);
        }

        public void RenderTo(WpfPlot targetPlot, System.Drawing.Color figBg, System.Drawing.Color dataBg, System.Drawing.Color foreColor, System.Drawing.Color seriesColor, double? xMin = null, double? xMax = null, string? xLabel = null)
        {
            if (targetPlot == null) return;
            targetPlot.Plot.Clear();

            // Apply Configured Style
            targetPlot.Plot.Style(ScottPlot.Style.Gray1);

            targetPlot.Plot.Style(figureBackground: figBg, dataBackground: dataBg);
            targetPlot.Plot.XAxis.Label(color: foreColor);
            targetPlot.Plot.YAxis.Label(color: foreColor);
            targetPlot.Plot.XAxis.TickLabelStyle(color: foreColor);
            targetPlot.Plot.YAxis.TickLabelStyle(color: foreColor);
            targetPlot.Plot.Title(Title, color: foreColor); // Fixed _title usage

            if (Counts != null && Counts.Length > 0 && BinCenters != null)
            {
                bool isLogScale = IsLogScale;

                if (isLogScale)
                {
                    var scatter = targetPlot.Plot.AddScatter(BinCenters, Counts);
                    scatter.LineWidth = 2;
                    scatter.Color = foreColor; // Line matches text/foreground for contrast
                    scatter.MarkerSize = 5;
                    scatter.MarkerShape = ScottPlot.MarkerShape.filledCircle;
                    scatter.MarkerLineWidth = 0;
                    scatter.MarkerColor = seriesColor; // Markers use series color
                    scatter.Label = "Data";

                    targetPlot.Plot.YAxis.TickLabelFormat(value => $"10^{value:F0}");
                    targetPlot.Plot.SetAxisLimitsY(0.1, double.NaN);
                }
                else
                {
                    var bar = targetPlot.Plot.AddBar(values: Counts, positions: BinCenters);
                    bar.FillColor = seriesColor;
                    bar.BarWidth = 1;
                    bar.BorderLineWidth = 0;
                    bar.Label = "Data";

                    targetPlot.Plot.SetAxisLimitsY(0, double.NaN);
                }

                // Plot All Active Fits
                if (ActiveFits != null && ActiveFits.Count > 0)
                {
                    foreach (var fit in ActiveFits.Values)
                    {
                        if (fit.Curve != null && fit.Curve.Length > 0)
                        {
                            var fitScatter = targetPlot.Plot.AddScatter(BinCenters, fit.Curve);
                            fitScatter.LineWidth = 2;
                            fitScatter.Color = fit.Color;
                            fitScatter.MarkerSize = 0;
                            fitScatter.Label = fit.Label;
                        }
                    }
                }

                // Stats Annotation
                if (Mu > 0 && !isLogScale)
                {
                    string statsLabel = $"μ = {Mu:F2}\nσ = {Sigma:F2}\nFWHM = {FWHM:F2}\nRes = {Resolution:F2}%";
                    var annotation = targetPlot.Plot.AddText(statsLabel, 0.02, 0.98);
                    annotation.Font.Size = 10;
                    // Use series color or cyan for stats? Let's use Cyan as it stands out well.
                    // Or maybe derive from foreColor? Let's keep Cyan for now as it was good.
                    annotation.Font.Color = System.Drawing.Color.Cyan;
                    annotation.BackgroundColor = System.Drawing.Color.FromArgb(200, dataBg.R, dataBg.G, dataBg.B);
                    annotation.BorderColor = System.Drawing.Color.Cyan;
                    annotation.Alignment = ScottPlot.Alignment.UpperLeft;
                }

                targetPlot.Plot.XLabel(xLabel ?? "ADC Channel");
                targetPlot.Plot.YLabel(isLogScale ? "log scale Count" : "Count");
                targetPlot.Plot.Legend(true, ScottPlot.Alignment.UpperRight);

                if (xMin.HasValue && xMax.HasValue)
                {
                    targetPlot.Plot.SetAxisLimits(xMin: xMin.Value, xMax: xMax.Value);
                }
                else
                {
                    targetPlot.Plot.AxisAuto();
                }
            }
            else
            {
                // Show "No Data"
                targetPlot.Plot.AddText("No Data", 0, 0, size: 24, color: System.Drawing.Color.Gray);
                targetPlot.Plot.SetAxisLimits(-1, 1, -1, 1);
            }

            targetPlot.Refresh();
        }

        public int ChannelIndex { get; set; }
    }
}
