using System.Windows;
using BaselineMode.WPF.Presentation.ViewModels;
using BaselineMode.WPF.Presentation.ViewModels.Baseline;
using BaselineMode.WPF.Presentation.ViewModels.Shared;

namespace BaselineMode.WPF.Views.Baseline
{
    public partial class ChannelDetailWindow : Window
    {
        public MainViewModel? MainVM { get; set; }

        public ChannelDetailWindow()
        {
            InitializeComponent();
            Loaded += ChannelDetailWindow_Loaded;
        }

        private void ChannelDetailWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ChannelViewModel vm)
            {
                if (MainVM != null)
                {
                    var figBg = ToDrawingColor(MainVM.GraphFigureColor);
                    var dataBg = ToDrawingColor(MainVM.GraphDataColor);
                    var foreColor = ToDrawingColor(MainVM.GraphTextColor);
                    var seriesColor = ToDrawingColor(MainVM.GraphSeriesColor);
                    vm.RenderTo(DetailPlot, figBg, dataBg, foreColor, seriesColor);
                }
                else
                {
                    // Fallback to default
                    vm.RenderTo(DetailPlot, System.Drawing.Color.Gray, System.Drawing.Color.Gray, System.Drawing.Color.Black, System.Drawing.Color.Blue);
                }
            }
        }

        private System.Drawing.Color ToDrawingColor(System.Windows.Media.Color mediaColor)
        {
            return System.Drawing.Color.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B);
        }
    }
}
