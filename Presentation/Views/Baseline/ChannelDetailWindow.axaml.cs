using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ScottPlot.Avalonia;
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

        private void ChannelDetailWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            if (DataContext is ChannelViewModel vm)
            {
                vm.PlotControl = DetailPlot;
                vm.ResidualPlotControl = ResidualPlot;

                // UX: Shift+Click to Lock Peak
                DetailPlot.PointerPressed += (s, pe) =>
                {
                    if (pe.KeyModifiers.HasFlag(KeyModifiers.Shift) && vm.IsManualMode)
                    {
                        var position = pe.GetPosition(DetailPlot);
                        var coordinates = DetailPlot.Plot.GetCoordinates(new ScottPlot.Pixel((float)position.X, (float)position.Y));
                        vm.ManualMu = coordinates.X;
                        vm.IsLockedMu = true;
                    }
                };

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
                    vm.RenderTo(DetailPlot, System.Drawing.Color.Gray, System.Drawing.Color.Gray, System.Drawing.Color.Black, System.Drawing.Color.Blue);
                }
            }
        }
    }
}
