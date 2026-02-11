using System.Windows;
using System.Windows.Controls;
using BaselineMode.WPF.Presentation.ViewModels;
using ScottPlot;

namespace BaselineMode.WPF.Presentation.Views.Calibration
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

                // Initial styling
                plot.Plot.Style(ScottPlot.Style.Gray1);
                plot.Plot.Title(vm.ChannelName); // Fixed property name
                plot.Refresh();
            }
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
