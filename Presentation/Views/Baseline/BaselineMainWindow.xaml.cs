using System.Windows;
using BaselineMode.WPF.Presentation.ViewModels;
using ScottPlot;
using BaselineMode.WPF.Core.Models;
using BaselineMode.WPF.Presentation.ViewModels.Baseline;
using BaselineMode.WPF.Presentation.ViewModels.Shared;
using BaselineMode.WPF.Core.Models.Shared;
using BaselineMode.WPF.Core.Helpers;

namespace BaselineMode.WPF.Presentation.Views.Baseline;

public partial class BaselineMainWindow : Window
{
    public BaselineMainWindow()
    {
        InitializeComponent();
        Loaded += BaselineMainWindow_Loaded;
    }

    private void BaselineMainWindow_Loaded(object sender, RoutedEventArgs e)
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

            if (DataContext is MainViewModel vm)
            {
                var figBg = ColorHelper.ToDrawingColor(vm.GraphFigureColor);
                var dataBg = ColorHelper.ToDrawingColor(vm.GraphDataColor);
                var foreColor = ColorHelper.ToDrawingColor(vm.GraphTextColor);
                var seriesColor = ColorHelper.ToDrawingColor(vm.GraphSeriesColor);
                channelVm.RenderPlot(figBg, dataBg, foreColor, seriesColor);
            }
        }
    }

    private void Vm_RequestPlotUpdate(object? sender, PlotUpdateEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var figBg = ColorHelper.ToDrawingColor(vm.GraphFigureColor);
        var dataBg = ColorHelper.ToDrawingColor(vm.GraphDataColor);
        var foreColor = ColorHelper.ToDrawingColor(vm.GraphTextColor);
        var seriesColor = ColorHelper.ToDrawingColor(vm.GraphSeriesColor);

        foreach (var channelVM in vm.Channels)
        {
            channelVM.RenderPlot(figBg, dataBg, foreColor, seriesColor);
        }
    }
}

