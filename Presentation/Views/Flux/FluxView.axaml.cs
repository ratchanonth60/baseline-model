using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Diagnostics;
using BaselineMode.WPF.Presentation.ViewModels.Flux;
using BaselineMode.WPF.Core.Helpers;
using ScottPlot;
using ScottPlot.Avalonia;

namespace BaselineMode.WPF.Presentation.Views.Flux
{
    public partial class FluxView : UserControl
    {
        public FluxView()
        {
            InitializeComponent();
        }

        private void Plot_Loaded(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is AvaPlot plot && plot.DataContext is FluxLayerViewModel vm)
            {
                Debug.WriteLine($"[FluxView] Plot_Loaded for {vm.LayerName}");
                vm.PlotControl = plot;
                vm.PlotControl = plot;
                plot.Plot.Title(vm.LayerName);
                plot.Refresh();
            }
        }

        private void Plot_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (sender is not AvaPlot plot || plot.DataContext is not FluxLayerViewModel layerVM) return;
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
