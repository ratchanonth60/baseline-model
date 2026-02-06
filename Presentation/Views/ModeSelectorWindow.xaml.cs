using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using BaselineMode.WPF.Views.Observation;

namespace BaselineMode.WPF.Views
{
    public partial class ModeSelectorWindow : Window
    {
        public ModeSelectorWindow()
        {
            InitializeComponent();
        }

        private void BaselineMode_Click(object sender, RoutedEventArgs e)
        {
            LaunchBaselineMode();
        }

        private void BaselineModeCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            LaunchBaselineMode();
        }

        private void ObservationMode_Click(object sender, RoutedEventArgs e)
        {
            LaunchObservationMode();
        }

        private void ObservationModeCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            LaunchObservationMode();
        }

        private void LaunchBaselineMode()
        {
            var mainWindow = new MainWindow();
            mainWindow.Show();
            this.Close();
        }

        private void LaunchObservationMode()
        {
            var app = (App)Application.Current;
            var observationWindow = app.ServiceProvider.GetRequiredService<ObservationMainWindow>();
            observationWindow.Show();
            this.Close();
        }
    }
}
