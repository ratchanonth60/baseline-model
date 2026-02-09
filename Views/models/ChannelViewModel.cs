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
        [ObservableProperty]
        private bool _isLogScale = false;
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
                // ตรวจสอบว่าเป็น Log Scale หรือไม่
                bool isLogScale = IsLogScale; // ต้องเพิ่ม property นี้

                if (isLogScale)

                {
                    // Log Scale - ใช้ Scatter Plot เหมือน WinForms
                    var scatter = targetPlot.Plot.AddScatter(BinCenters, Counts);
                    scatter.LineWidth = 2;
                    scatter.Color = System.Drawing.Color.Black;
                    scatter.MarkerSize = 5;
                    scatter.MarkerShape = ScottPlot.MarkerShape.filledCircle;
                    scatter.MarkerLineWidth = 0;
                    scatter.MarkerColor = System.Drawing.Color.DarkRed;

                    // ตั้งค่า Y-axis format
                    targetPlot.Plot.YAxis.TickLabelFormat(value => $"10^{value:F0}");
                    targetPlot.Plot.SetAxisLimitsY(0.1, double.NaN);
                }
                else
                {
                    // Linear Scale - ใช้ Bar Chart เหมือน WinForms
                    var bar = targetPlot.Plot.AddBar(values: Counts, positions: BinCenters);
                    bar.FillColor = System.Drawing.Color.Black;
                    bar.BorderLineWidth = 0;

                    targetPlot.Plot.SetAxisLimitsY(0, double.NaN);
                }

                // Plot Fit Curve (ถ้ามี)
                if (FitCurve != null && FitCurve.Length > 0 && BinCenters.Length == FitCurve.Length)
                {
                    double maxFit = FitCurve.Max();
                    if (maxFit > 0)
                    {
                        var fitScatter = targetPlot.Plot.AddScatter(BinCenters, FitCurve);
                        fitScatter.LineWidth = 2;
                        fitScatter.Color = System.Drawing.Color.Red;
                        fitScatter.MarkerSize = 0;
                        fitScatter.Label = "Fit";
                    }
                }

                targetPlot.Plot.XLabel("ADC Channel (0-16384)");
                targetPlot.Plot.YLabel(isLogScale ? "log scale Count (#)" : "Count (#)");
                targetPlot.Plot.AxisAuto();

                // Statistics Annotation (after AxisAuto so we know the axis limits)
                if (Mu > 0 && !isLogScale)
                {
                    string statsLabel = $"μ = {Mu:F2}\nσ = {Sigma:F2}\nFWHM = {FWHM:F2}\nRes = {Resolution:F2}%";
                    var limits = targetPlot.Plot.GetAxisLimits();
                    double textX = limits.XMin + (limits.XMax - limits.XMin) * 0.02;
                    double textY = limits.YMax - (limits.YMax - limits.YMin) * 0.02;
                    var annotation = targetPlot.Plot.AddText(statsLabel, textX, textY);
                    annotation.Font.Size = 10;
                    annotation.Font.Color = System.Drawing.Color.Blue;
                    annotation.BackgroundColor = System.Drawing.Color.FromArgb(220, 255, 255, 255);
                    annotation.BorderColor = System.Drawing.Color.Blue;
                    annotation.Alignment = ScottPlot.Alignment.UpperLeft;
                }
            }

            targetPlot.Refresh();
        }

        public int ChannelIndex { get; set; }
    }
}
