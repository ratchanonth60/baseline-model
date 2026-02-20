using CommunityToolkit.Mvvm.ComponentModel;
using ScottPlot;
using ScottPlot.Avalonia;
using System.Diagnostics;
using BaselineMode.WPF.Core.Helpers;
using System.Linq;

namespace BaselineMode.WPF.Presentation.ViewModels.Flux
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

        /// <summary>Reference to the AvaPlot control for direct rendering.</summary>
        public AvaPlot? PlotControl { get; set; }

        /// <summary>
        /// Renders scatter plot of flux density vs time with theme colors.
        /// </summary>
        public void RenderPlot(
            System.Drawing.Color figBg,
            System.Drawing.Color dataBg,
            System.Drawing.Color foreColor,
            System.Drawing.Color seriesColor,
            bool isLogScale = false,
            double xMin = 0,
            double? xMax = null,
            string? xLabel = null,
            string? yLabel = null,
            double widthMultiplier = 1.0)
        {
            if (PlotControl != null)
            {
                Debug.WriteLine($"[FluxLayerVM] RenderPlot called for {LayerName}. XData count: {XData?.Length ?? 0}");
                RenderTo(PlotControl, figBg, dataBg, foreColor, seriesColor, isLogScale, xMin, xMax, xLabel, yLabel, widthMultiplier);
            }
            else
            {
                Debug.WriteLine($"[FluxLayerVM] RenderPlot called for {LayerName} but PlotControl is NULL");
            }
        }

        /// <summary>
        /// Renders the scatter plot to a specific AvaPlot control.
        /// </summary>
        public void RenderTo(
            AvaPlot targetPlot,
            System.Drawing.Color figBg,
            System.Drawing.Color dataBg,
            System.Drawing.Color foreColor,
            System.Drawing.Color seriesColor,
            bool isLogScale = false,
            double xMin = 0,
            double? xMax = null,
            string? xLabel = null,
            string? yLabel = null,
            double widthMultiplier = 1.0)
        {
            if (targetPlot == null) return;
            targetPlot.Plot.Clear();

            // Apply style - Theme colors
            targetPlot.Plot.FigureBackground.Color = ColorHelper.ToScottPlotColor(figBg);
            targetPlot.Plot.DataBackground.Color = ColorHelper.ToScottPlotColor(dataBg);

            var fgColor = ColorHelper.ToScottPlotColor(foreColor);
            targetPlot.Plot.Axes.Color(fgColor);
            targetPlot.Plot.Axes.Title.Label.ForeColor = fgColor;
            targetPlot.Plot.Axes.Bottom.Label.ForeColor = fgColor;
            targetPlot.Plot.Axes.Left.Label.ForeColor = fgColor;

            targetPlot.Plot.Title($"Particle Flux Density: {LayerName}");

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

                    var scatter = targetPlot.Plot.Add.Scatter(XData, plotYData);
                    scatter.Color = ColorHelper.ToScottPlotColor(seriesColor);
                    scatter.LineWidth = (float)(1 * widthMultiplier);
                    scatter.MarkerSize = (float)(3 * widthMultiplier);
                    scatter.MarkerShape = ScottPlot.MarkerShape.FilledCircle;

                    // Manual tick formatter if needed, but simple for now
                }
                else
                {
                    plotYData = YData;

                    var scatter = targetPlot.Plot.Add.Scatter(XData, plotYData);
                    scatter.Color = ColorHelper.ToScottPlotColor(seriesColor);
                    scatter.LineWidth = (float)(1 * widthMultiplier);
                    scatter.MarkerSize = (float)(3 * widthMultiplier);
                    scatter.MarkerShape = ScottPlot.MarkerShape.FilledCircle;

                    targetPlot.Plot.Axes.SetLimits(bottom: 0);
                }

                // Set axis limits
                double finalXMax = xMax ?? (XData.Length > 0 ? XData.Max() : 1);
                targetPlot.Plot.Axes.SetLimits(left: xMin, right: finalXMax);

                targetPlot.Plot.Axes.Bottom.Label.Text = xLabel ?? "Cumulative Time (s)";
                targetPlot.Plot.Axes.Left.Label.Text = yLabel ?? (isLogScale ? "Flux Density (log??)" : "Flux Density (count/m??s)");
            }
            else
            {
                // No data placeholder
                targetPlot.Plot.Add.Text("No Data", 0, 0);
                targetPlot.Plot.Axes.SetLimits(-1, 1, -1, 1);
            }

            targetPlot.Refresh();
        }
    }
}
