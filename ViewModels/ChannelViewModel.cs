using CommunityToolkit.Mvvm.ComponentModel;
using ScottPlot;

namespace BaselineMode.WPF.ViewModels
{
    public partial class ChannelViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _title = "Channel";

        [ObservableProperty]
        private string _statsText = "No Data";

        // We can hold the plot data here to update the specific WpfPlot
        public double[] BinCenters { get; set; }
        public double[] Counts { get; set; }
        public double[] FitCurve { get; set; }

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

            if (Counts != null && Counts.Length > 0)
            {
                var bar = targetPlot.Plot.AddBar(Counts, BinCenters);
                bar.FillColor = System.Drawing.Color.Black;

                // ✅ ตรวจสอบ FitCurve:
                if (FitCurve != null && FitCurve.Length > 0 && BinCenters != null && BinCenters.Length == FitCurve.Length)
                {
                    double maxFit = FitCurve.Max();

                    if (maxFit > 0) // ✅ เช็คว่ามีค่ามากกว่า 0
                    {
                        System.Diagnostics.Debug.WriteLine($"Plotting fit for {Title}: max={maxFit:F1}");

                        var fitScatter = targetPlot.Plot.AddScatter(BinCenters, FitCurve);
                        fitScatter.LineWidth = 2;
                        fitScatter.Color = System.Drawing.Color.Red;
                        fitScatter.MarkerSize = 0;
                        fitScatter.Label = "Gaussian Fit";

                        // Find and mark peak
                        int peakIdx = Array.IndexOf(FitCurve, maxFit);
                        if (peakIdx >= 0 && peakIdx < BinCenters.Length)
                        {
                            targetPlot.Plot.AddPoint(BinCenters[peakIdx], FitCurve[peakIdx],
                                color: System.Drawing.Color.Blue, size: 7);
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Skipping fit plot for {Title}: maxFit={maxFit}");
                    }
                }

                targetPlot.Plot.AxisAutoY();
            }

            targetPlot.Refresh();
        }

        private ScottPlot.MarkerShape createMarkerShape() => ScottPlot.MarkerShape.filledCircle;

        public int ChannelIndex { get; set; }
    }
}
