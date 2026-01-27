using System.Windows;
using BaselineMode.WPF.ViewModels;

namespace BaselineMode.WPF.Views
{
    public partial class ChannelDetailWindow : Window
    {
        public ChannelDetailWindow()
        {
            InitializeComponent();
            
            this.Loaded += ChannelDetailWindow_Loaded;
        }

        private void ChannelDetailWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ChannelViewModel vm)
            {
                // Instruct VM to render to OUR plot control
                vm.RenderTo(DetailPlot);
            }
        }
    }
}
