using System.Windows;
using System.Windows.Controls;
using BaselineMode.WPF.Presentation.ViewModels;
using ScottPlot;

namespace BaselineMode.WPF.Presentation.Views.Flux
{
    public partial class FluxView : UserControl
    {
        public FluxView()
        {
            InitializeComponent();
        }

        private void Plot_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is WpfPlot plot && plot.DataContext is FluxLayerViewModel vm)
            {
                vm.PlotControl = plot;
                plot.Plot.Style(ScottPlot.Style.Gray1);
                plot.Plot.Title(vm.LayerName);
                plot.Refresh();
            }
        }

        private void Plot_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is WpfPlot plot && plot.DataContext is FluxLayerViewModel layerVM)
            {
                if (this.DataContext is FluxViewModel fluxVM)
                {
                    var detailWindow = new FluxDetailWindow();
                    detailWindow.ShowFluxData(
                        layerVM.XData, layerVM.YData,
                        $"Flux Density: {layerVM.LayerName}",
                        fluxVM.IsLogScale);
                    detailWindow.Show();
                }
            }
        }
    }
}
