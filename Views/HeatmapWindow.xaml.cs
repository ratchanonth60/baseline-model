using System;
using System.Linq;
using System.Windows;
using BaselineMode.WPF.Views.models;
using ScottPlot;

namespace BaselineMode.WPF.Views
{
    public partial class HeatmapWindow : Window
    {
        public HeatmapWindow()
        {
            InitializeComponent();
            this.Loaded += HeatmapWindow_Loaded;
        }

        private void HeatmapWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is HeatmapViewModel vm && vm.HeatmapData != null)
            {
                PlotHeatmap(vm);
            }
        }

        private void PlotHeatmap(HeatmapViewModel vm)
        {
            HeatmapPlot.Plot.Clear();

            // data is [rows, cols]
            // We want Rows = Y (Z channels), Cols = X (X channels)
            // ScottPlot 4 AddHeatmap takes double[,] intensity

            var hm = HeatmapPlot.Plot.AddHeatmap(vm.HeatmapData, lockScales: false);
            hm.Smooth = true; // Interpolation? User said "Heatmap -3d" so maybe plain blocks is better?
                              // Let's try regular first (Smooth=false is default usually but true effectively makes it look nicer)
                              // Actually for discrete channels, Smooth=false (blocks) is more accurate.
            hm.Smooth = false;

            var cb = HeatmapPlot.Plot.AddColorbar(hm);

            // Labels
            HeatmapPlot.Plot.XLabel("X Channels (1-8)");
            HeatmapPlot.Plot.YLabel("Z Channels (9-16)");
            HeatmapPlot.Plot.Title("Coincidence Heatmap");

            // Custom Ticks
            double[] xPositions = Enumerable.Range(0, 8).Select(x => (double)x).ToArray();
            string[] xLabels = vm.XLabels;
            HeatmapPlot.Plot.XTicks(xPositions, xLabels);

            double[] yPositions = Enumerable.Range(0, 8).Select(y => (double)y).ToArray();
            string[] yLabels = vm.YLabels;
            HeatmapPlot.Plot.YTicks(yPositions, yLabels);

            // Stats
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
