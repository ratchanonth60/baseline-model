using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using BaselineMode.WPF.Views.Observation;
using BaselineMode.WPF.Views.Baseline;
using BaselineMode.WPF.Views.Flux;

namespace BaselineMode.WPF.Views.Shared
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

        private void FluxMode_Click(object sender, RoutedEventArgs e)
        {
            LaunchFluxMode();
        }

        private void FluxModeCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            LaunchFluxMode();
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

        private void LaunchFluxMode()
        {
            var app = (App)Application.Current;
            var fluxWindow = app.ServiceProvider.GetRequiredService<FluxMainWindow>();
            fluxWindow.Show();
            this.Close();
        }
    }
}

