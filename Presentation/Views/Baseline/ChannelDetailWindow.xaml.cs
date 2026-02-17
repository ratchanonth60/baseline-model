using System.Windows;
using BaselineMode.WPF.Presentation.ViewModels;
using BaselineMode.WPF.Presentation.ViewModels.Baseline;
using BaselineMode.WPF.Presentation.ViewModels.Shared;
using BaselineMode.WPF.Core.Helpers;

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
                    var figBg = ColorHelper.ToDrawingColor(MainVM.GraphFigureColor);
                    var dataBg = ColorHelper.ToDrawingColor(MainVM.GraphDataColor);
                    var foreColor = ColorHelper.ToDrawingColor(MainVM.GraphTextColor);
                    var seriesColor = ColorHelper.ToDrawingColor(MainVM.GraphSeriesColor);
                    vm.RenderTo(DetailPlot, figBg, dataBg, foreColor, seriesColor);
                }
                else
                {
                    // Fallback to default
                    vm.RenderTo(DetailPlot, System.Drawing.Color.Gray, System.Drawing.Color.Gray, System.Drawing.Color.Black, System.Drawing.Color.Blue);
                }
            }
        }


    }
}
