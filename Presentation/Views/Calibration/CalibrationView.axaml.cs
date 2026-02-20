using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BaselineMode.WPF.Presentation.ViewModels.Calibration;
using BaselineMode.WPF.Presentation.ViewModels.Shared;
using ScottPlot;
using ScottPlot.Avalonia;

namespace BaselineMode.WPF.Presentation.Views.Calibration
{
    public partial class CalibrationView : UserControl
    {
        public CalibrationView()
        {
            InitializeComponent();
        }

        private void Plot_Loaded(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is AvaPlot plot && plot.DataContext is ChannelViewModel vm)
            {
                // Assign the plot control to the ViewModel so it can render to it directly
                vm.PlotControl = plot;

                // If data was already loaded, re-render immediately
                if (vm.Counts != null && vm.Counts.Length > 0 && this.DataContext is CalibrationViewModel calibVM)
                {
                    var figBg = ToDrawingColor(calibVM.GraphFigureColor, System.Drawing.Color.FromArgb(255, 30, 30, 30));
                    var dataBg = ToDrawingColor(calibVM.GraphDataColor, System.Drawing.Color.FromArgb(255, 37, 37, 38));
                    var fgColor = ToDrawingColor(calibVM.GraphTextColor, System.Drawing.Color.White);
                    var seriesColor = ToDrawingColor(calibVM.GraphSeriesColor, System.Drawing.Color.Cyan);

                    string xLabel = calibVM.SelectedXAxisIndex == 1 ? "Voltage (mV)" : "ADC Channel";
                    vm.RenderPlot(figBg, dataBg, fgColor, seriesColor,
                        xMin: calibVM.XAxisMin, xMax: calibVM.XAxisMax, xLabel: xLabel);
                }
                else
                {
                    // Initial styling for empty plots
                    // Initial styling for empty plots
                    plot.Plot.Title(vm.ChannelName);
                    plot.Refresh();
                }
            }
        }

        private static System.Drawing.Color ToDrawingColor(Avalonia.Media.Color avColor, System.Drawing.Color fallback)
        {
            try
            {
                return System.Drawing.Color.FromArgb(avColor.A, avColor.R, avColor.G, avColor.B);
            }
            catch { return fallback; }
        }

        private void Plot_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (sender is AvaPlot plot && plot.DataContext is ChannelViewModel channelVM)
            {
                if (this.DataContext is CalibrationViewModel calibVM)
                {
                    if (calibVM.OpenZoomWindowCommand.CanExecute(channelVM))
                    {
                        calibVM.OpenZoomWindowCommand.Execute(channelVM);
                    }
                }
            }
        }
    }
}
