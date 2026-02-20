using System.Windows;
using System.Windows.Controls;
using BaselineMode.WPF.Presentation.ViewModels;
using BaselineMode.WPF.Presentation.ViewModels.Calibration;
using BaselineMode.WPF.Presentation.ViewModels.Shared;
using ScottPlot;

namespace BaselineMode.WPF.Views.Calibration
{
    public partial class CalibrationView : UserControl
    {
        public CalibrationView()
        {
            InitializeComponent();
        }

        private void Plot_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is WpfPlot plot && plot.DataContext is ChannelViewModel vm)
            {
                // Assign the plot control to the ViewModel so it can render to it directly
                vm.PlotControl = plot;

                // If data was already loaded (e.g. Z-tab plots loaded lazily after ReadData),
                // re-render immediately so graphs appear when the tab is first shown.
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
                    plot.Plot.Style(ScottPlot.Style.Gray1);
                    plot.Plot.Title(vm.ChannelName);
                    plot.Refresh();
                }
            }
        }

        private static System.Drawing.Color ToDrawingColor(System.Windows.Media.Color wpfColor, System.Drawing.Color fallback)
        {
            try
            {
                return System.Drawing.Color.FromArgb(wpfColor.A, wpfColor.R, wpfColor.G, wpfColor.B);
            }
            catch { return fallback; }
        }

        private void Plot_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is WpfPlot plot && plot.DataContext is ChannelViewModel channelVM)
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
