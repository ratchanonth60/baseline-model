using CommunityToolkit.Mvvm.ComponentModel;
using ScottPlot;

namespace BaselineMode.WPF.Presentation.ViewModels
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
        private string _title = "Channel";

        [ObservableProperty]
        private string _statsText = "No Data";

        public double[]? BinCenters { get; set; }
        public double[]? Counts { get; set; }
        public double[]? RawCounts { get; set; }

        [ObservableProperty]
        private bool _isFitting = false;

        // Multi-Fit Support: Dictionary of FitName -> FitData
        public Dictionary<string, FitData> ActiveFits { get; set; } = new Dictionary<string, FitData>();

        // Cache: FitType -> FitData
        private Dictionary<string, FitData> _fitCache = new Dictionary<string, FitData>();

        public void CacheFit(string fitType, FitData data)
        {
            if (!_fitCache.ContainsKey(fitType))
                _fitCache[fitType] = data;
        }

        public FitData? GetCachedFit(string fitType)
        {
            return _fitCache.ContainsKey(fitType) ? _fitCache[fitType] : null;
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

        public void RenderPlot()
        {
            if (PlotControl != null)
                RenderTo(PlotControl);
        }

        public void RenderTo(WpfPlot targetPlot)
        {
            if (targetPlot == null) return;
            targetPlot.Plot.Clear();

            if (Counts != null && Counts.Length > 0 && BinCenters != null)
            {
                bool isLogScale = IsLogScale;

                if (isLogScale)
                {
                    var scatter = targetPlot.Plot.AddScatter(BinCenters, Counts);
                    scatter.LineWidth = 2;
                    scatter.Color = System.Drawing.Color.Black;
                    scatter.MarkerSize = 5;
                    scatter.MarkerShape = ScottPlot.MarkerShape.filledCircle;
                    scatter.MarkerLineWidth = 0;
                    scatter.MarkerColor = System.Drawing.Color.DarkRed;
                    scatter.Label = "Data";

                    targetPlot.Plot.YAxis.TickLabelFormat(value => $"10^{value:F0}");
                    targetPlot.Plot.SetAxisLimitsY(0.1, double.NaN);
                }
                else
                {
                    var bar = targetPlot.Plot.AddBar(values: Counts, positions: BinCenters);
                    bar.FillColor = System.Drawing.Color.Black;
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

                // Stats Annotation (Only for primary/last update or consolidated)
                if (Mu > 0 && !isLogScale)
                {
                    string statsLabel = $"μ = {Mu:F2}\nσ = {Sigma:F2}\nFWHM = {FWHM:F2}\nRes = {Resolution:F2}%";
                    var annotation = targetPlot.Plot.AddText(statsLabel, 0.02, 0.98);
                    annotation.Font.Size = 10;
                    annotation.Font.Color = System.Drawing.Color.Blue;
                    annotation.BackgroundColor = System.Drawing.Color.FromArgb(220, 255, 255, 255);
                    annotation.BorderColor = System.Drawing.Color.Blue;
                    annotation.Alignment = ScottPlot.Alignment.UpperLeft;
                }

                targetPlot.Plot.XLabel("ADC Channel");
                targetPlot.Plot.YLabel(isLogScale ? "log scale Count" : "Count");
                targetPlot.Plot.Legend(true, ScottPlot.Alignment.UpperRight); // Show Legend
                targetPlot.Plot.AxisAuto();
            }

            targetPlot.Refresh();
        }

        public int ChannelIndex { get; set; }
    }
}
