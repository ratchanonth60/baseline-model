using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using BaselineMode.WPF.Presentation.ViewModels;
using ScottPlot;
using BaselineMode.WPF.Presentation.ViewModels.Flux;
using BaselineMode.WPF.Core.Helpers;

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
                Debug.WriteLine($"[FluxView] Plot_Loaded for {vm.LayerName}");
                vm.PlotControl = plot;
                plot.Plot.Style(ScottPlot.Style.Gray1);
                plot.Plot.Title(vm.LayerName);
                plot.Refresh();
            }
        }

        private void Plot_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not WpfPlot plot || plot.DataContext is not FluxLayerViewModel layerVM) return;
            if (this.DataContext is not FluxViewModel fluxVM) return;

            var detailWindow = new FluxDetailWindow();

            // Set Theme
            var figBg = ColorHelper.ToDrawingColor(fluxVM.GraphFigureColor);
            var dataBg = ColorHelper.ToDrawingColor(fluxVM.GraphDataColor);
            var fgColor = ColorHelper.ToDrawingColor(fluxVM.GraphTextColor);
            var seriesColor = ColorHelper.ToDrawingColor(fluxVM.GraphSeriesColor);

            detailWindow.SetColorTheme(figBg, dataBg, fgColor, seriesColor);

            detailWindow.ShowFluxData(
                layerVM.XData,
                layerVM.YData,
                $"Flux Density: {layerVM.LayerName}",
                fluxVM.IsLogScale);

            detailWindow.Show();
        }


    }
}
