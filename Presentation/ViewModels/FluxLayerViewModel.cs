using CommunityToolkit.Mvvm.ComponentModel;
using ScottPlot;
using System.Diagnostics;

namespace BaselineMode.WPF.Presentation.ViewModels
{
    /// <summary>
    /// ViewModel for a single flux density layer plot (L1–L7).
    /// Displays scatter/line plots of flux density vs cumulative time.
    /// </summary>
    public partial class FluxLayerViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _layerName = "Layer";

        [ObservableProperty]
        private string _statsText = "No Data";

        public int LayerIndex { get; set; }

        /// <summary>X-axis data: cumulative time in seconds.</summary>
        public double[]? XData { get; set; }

        /// <summary>Y-axis data: flux density (count/m²·s).</summary>
        public double[]? YData { get; set; }

        /// <summary>Reference to the WpfPlot control for direct rendering.</summary>
        public WpfPlot? PlotControl { get; set; }

        /// <summary>
        /// Renders scatter plot of flux density vs time with theme colors.
        /// </summary>
        public void RenderPlot(
            System.Drawing.Color figBg,
            System.Drawing.Color dataBg,
            System.Drawing.Color foreColor,
            System.Drawing.Color seriesColor,
            bool isLogScale = false,
            double? xMax = null,
            string? xLabel = null,
            string? yLabel = null)
        {
            if (PlotControl != null)
            {
                Debug.WriteLine($"[FluxLayerVM] RenderPlot called for {LayerName}. XData count: {XData?.Length ?? 0}");
                RenderTo(PlotControl, figBg, dataBg, foreColor, seriesColor, isLogScale, xMax, xLabel, yLabel);
            }
            else
            {
                Debug.WriteLine($"[FluxLayerVM] RenderPlot called for {LayerName} but PlotControl is NULL");
            }
        }

        /// <summary>
        /// Renders the scatter plot to a specific WpfPlot control.
        /// </summary>
        public void RenderTo(
            WpfPlot targetPlot,
            System.Drawing.Color figBg,
            System.Drawing.Color dataBg,
            System.Drawing.Color foreColor,
            System.Drawing.Color seriesColor,
            bool isLogScale = false,
            double? xMax = null,
            string? xLabel = null,
            string? yLabel = null)
        {
            if (targetPlot == null) return;
            targetPlot.Plot.Clear();

            // Apply style
            targetPlot.Plot.Style(ScottPlot.Style.Gray1);
            targetPlot.Plot.Style(figureBackground: figBg, dataBackground: dataBg);
            targetPlot.Plot.XAxis.Label(color: foreColor);
            targetPlot.Plot.YAxis.Label(color: foreColor);
            targetPlot.Plot.XAxis.TickLabelStyle(color: foreColor);
            targetPlot.Plot.YAxis.TickLabelStyle(color: foreColor);
            targetPlot.Plot.Title($"Particle Flux Density: {LayerName}", color: foreColor);

            if (XData != null && YData != null && XData.Length > 0 && YData.Length > 0)
            {
                double[] plotYData;

                if (isLogScale)
                {
                    // Apply log10 transformation, handle zero/negative values
                    plotYData = new double[YData.Length];
                    for (int i = 0; i < YData.Length; i++)
                    {
                        plotYData[i] = YData[i] > 0 ? System.Math.Log10(YData[i]) : -10;
                    }

                    var scatter = targetPlot.Plot.AddScatter(XData, plotYData);
                    scatter.Color = seriesColor;
                    scatter.LineWidth = 1;
                    scatter.MarkerSize = 3;
                    scatter.MarkerShape = ScottPlot.MarkerShape.filledCircle;

                    targetPlot.Plot.YAxis.TickLabelFormat(value => $"10^{value:F0}");
                }
                else
                {
                    plotYData = YData;

                    var scatter = targetPlot.Plot.AddScatter(XData, plotYData);
                    scatter.Color = seriesColor;
                    scatter.LineWidth = 1;
                    scatter.MarkerSize = 3;
                    scatter.MarkerShape = ScottPlot.MarkerShape.filledCircle;

                    targetPlot.Plot.SetAxisLimitsY(0, double.NaN);
                }

                // Set axis limits
                if (xMax.HasValue)
                    targetPlot.Plot.SetAxisLimits(xMin: 0, xMax: xMax.Value);
                else
                    targetPlot.Plot.SetAxisLimits(xMin: 0);

                targetPlot.Plot.XLabel(xLabel ?? "Cumulative Time (s)");
                targetPlot.Plot.YLabel(yLabel ?? (isLogScale ? "Flux Density (log₁₀)" : "Flux Density (count/m²·s)"));
            }
            else
            {
                // No data placeholder
                targetPlot.Plot.AddText("No Data", 0, 0, size: 24, color: System.Drawing.Color.Gray);
                targetPlot.Plot.SetAxisLimits(-1, 1, -1, 1);
            }

            targetPlot.Refresh();
        }
    }
}
