using CommunityToolkit.Mvvm.ComponentModel;
using ScottPlot;

namespace BaselineMode.WPF.Views.models
{
    public partial class ChannelViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _title = "Channel";

        [ObservableProperty]
        private string _statsText = "No Data";

        // We can hold the plot data here to update the specific WpfPlot
        public double[]? BinCenters { get; set; }
        public double[]? Counts { get; set; } // Log scale counts for display
        public double[]? RawCounts { get; set; } // Linear scale counts (original)
        public double[]? FitCurve { get; set; }

        // Statistics
        public double Mu { get; set; }
        public double Sigma { get; set; }
        public double Peak { get; set; }
        public double FWHM { get; set; }
        public double Resolution { get; set; }

        // Reference to the actual control for refreshing
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
                // Plot histogram as scatter with markers (ให้เหมือนรูป)
                var histScatter = targetPlot.Plot.AddScatter(BinCenters, Counts);
                histScatter.LineWidth = 1;
                histScatter.Color = System.Drawing.Color.Black;
                histScatter.MarkerSize = 0;
                histScatter.LineStyle = ScottPlot.LineStyle.Solid;

                // Plot Fit Curve
                if (FitCurve != null && FitCurve.Length > 0 && BinCenters.Length == FitCurve.Length)
                {
                    double maxFit = FitCurve.Max();

                    if (maxFit > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"Plotting fit for {Title}: max={maxFit:F1}");

                        var fitScatter = targetPlot.Plot.AddScatter(BinCenters, FitCurve);
                        fitScatter.LineWidth = 2;
                        fitScatter.Color = System.Drawing.Color.Red;
                        fitScatter.MarkerSize = 0;
                        fitScatter.Label = "Fit";
                    }
                }

                // Add statistics text annotation
                if (Mu > 0)
                {
                    string statsLabel = $"μ = {Mu:F2}\nσ = {Sigma:F2}\nFWHM = {FWHM:F2}\nRes = {Resolution:F2}%";
                    var annotation = targetPlot.Plot.AddText(statsLabel, 0.02, 0.98);
                    annotation.Font.Size = 10;
                    annotation.Font.Color = System.Drawing.Color.Blue;
                    annotation.BackgroundColor = System.Drawing.Color.FromArgb(220, 255, 255, 255);
                    annotation.BorderColor = System.Drawing.Color.Blue;
                    annotation.Alignment = ScottPlot.Alignment.UpperLeft;
                }

                targetPlot.Plot.XLabel("ADC Channel (0-16384)");
                targetPlot.Plot.YLabel("Count (#)");

                // Set axis limits to avoid log(0) issues
                targetPlot.Plot.SetAxisLimitsY(0.1, double.NaN);

                targetPlot.Plot.AxisAuto();
            }

            targetPlot.Refresh();
        }

        private ScottPlot.MarkerShape createMarkerShape() => ScottPlot.MarkerShape.filledCircle;

        public int ChannelIndex { get; set; }
    }
}
