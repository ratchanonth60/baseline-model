using CommunityToolkit.Mvvm.ComponentModel;

namespace BaselineMode.WPF.ViewModels
{
    public partial class HeatmapViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _title = "Coincidence Heatmap (X vs Z)";

        // 8x8 Matrix: [Row, Col] -> [Z-index, X-index] or similar
        // Let's assume Rows = Z (Ch 9-16), Cols = X (Ch 1-8)
        public double[,] HeatmapData { get; set; }

        public string[] XLabels { get; set; }
        public string[] YLabels { get; set; }

        public HeatmapViewModel(double[,] data)
        {
            HeatmapData = data;
            XLabels = new string[] { "X1", "X2", "X3", "X4", "X5", "X6", "X7", "X8" };
            YLabels = new string[] { "Z1", "Z2", "Z3", "Z4", "Z5", "Z6", "Z7", "Z8" };
        }
    }
}
