using System.Windows;
using BaselineMode.WPF.Presentation.ViewModels;
using ScottPlot;
using BaselineMode.WPF.Core.Models;
using BaselineMode.WPF.Presentation.ViewModels.Baseline;
using BaselineMode.WPF.Presentation.ViewModels.Shared;
using BaselineMode.WPF.Core.Models.Shared;

namespace BaselineMode.WPF.Views.Baseline
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.RequestPlotUpdate += Vm_RequestPlotUpdate;
            }
        }

        private void WpfPlot_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is WpfPlot plot && plot.Tag is ChannelViewModel channelVm)
            {
                channelVm.PlotControl = plot;
                // Initial Configuration
                plot.Plot.Style(ScottPlot.Style.Seaborn);
                plot.Configuration.ScrollWheelZoom = true;

                // Render if data exists
                if (DataContext is MainViewModel vm)
                {
                    var figBg = ToDrawingColor(vm.GraphFigureColor);
                    var dataBg = ToDrawingColor(vm.GraphDataColor);
                    var foreColor = ToDrawingColor(vm.GraphTextColor);
                    var seriesColor = ToDrawingColor(vm.GraphSeriesColor);
                    channelVm.RenderPlot(figBg, dataBg, foreColor, seriesColor);
                }
            }
        }

        private void Vm_RequestPlotUpdate(object sender, PlotUpdateEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            var figBg = ToDrawingColor(vm.GraphFigureColor);
            var dataBg = ToDrawingColor(vm.GraphDataColor);
            var foreColor = ToDrawingColor(vm.GraphTextColor);
            var seriesColor = ToDrawingColor(vm.GraphSeriesColor);

            foreach (var channelVM in vm.Channels)
            {
                channelVM.RenderPlot(figBg, dataBg, foreColor, seriesColor);
            }
        }

        private static System.Drawing.Color ToDrawingColor(System.Windows.Media.Color mediaColor)
        {
            return System.Drawing.Color.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B);
        }
    }
}

