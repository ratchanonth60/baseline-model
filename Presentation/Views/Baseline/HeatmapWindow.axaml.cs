using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ScottPlot.Avalonia;
using BaselineMode.WPF.Presentation.ViewModels;
using BaselineMode.WPF.Presentation.ViewModels.Flux;

namespace BaselineMode.WPF.Views.Baseline
{
    public partial class HeatmapWindow : Window
    {
        public HeatmapWindow()
        {
            InitializeComponent();
            this.Loaded += HeatmapWindow_Loaded;
        }

        private void HeatmapWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            if (DataContext is HeatmapViewModel vm && vm.HeatmapData != null)
            {
                PlotHeatmap(vm);
            }
        }

        private void PlotHeatmap(HeatmapViewModel vm)
        {
            HeatmapPlot.Plot.Clear();

            var hm = HeatmapPlot.Plot.Add.Heatmap(vm.HeatmapData);

            var cb = HeatmapPlot.Plot.Add.ColorBar(hm);

            HeatmapPlot.Plot.XLabel("X Channels (1-8)");
            HeatmapPlot.Plot.YLabel("Z Channels (9-16)");
            HeatmapPlot.Plot.Title("Coincidence Heatmap");

            double[] xPositions = [.. Enumerable.Range(0, 8).Select(x => (double)x)];
            string[] xLabels = vm.XLabels;
            HeatmapPlot.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(xPositions, xLabels);

            double[] yPositions = [.. Enumerable.Range(0, 8).Select(y => (double)y)];
            string[] yLabels = vm.YLabels;
            HeatmapPlot.Plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericManual(yPositions, yLabels);

            double maxVal = 0;
            double total = 0;
            int rows = vm.HeatmapData.GetLength(0);
            int cols = vm.HeatmapData.GetLength(1);

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    double val = vm.HeatmapData[r, c];
                    if (val > maxVal) maxVal = val;
                    total += val;
                }

            TxtTotalEvents.Text = total.ToString("N0");
            TxtMaxCount.Text = maxVal.ToString("N0");

            HeatmapPlot.Refresh();
        }
    }
}
