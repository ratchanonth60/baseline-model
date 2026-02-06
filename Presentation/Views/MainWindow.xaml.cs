using System.Windows;
using BaselineMode.WPF.Views.models;
using ScottPlot;
using BaselineMode.WPF.Models;

namespace BaselineMode.WPF.Views
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
            if (sender is WpfPlot plot && plot.Tag is ChannelViewModel vm)
            {
                vm.PlotControl = plot;
                // Initial Configuration
                plot.Plot.Style(ScottPlot.Style.Seaborn);
                plot.Configuration.ScrollWheelZoom = true;

                // Render if data exists
                vm.RenderPlot();
            }
        }

        private void Vm_RequestPlotUpdate(object sender, PlotUpdateEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            foreach (var channelVM in vm.Channels)
            {
                channelVM.RenderPlot();
            }
        }
    }
}

