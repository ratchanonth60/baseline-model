using System;
using System.Windows;
using System.Windows.Threading;
using BaselineMode.WPF.Core.Models;
using BaselineMode.WPF.Presentation.ViewModels;
using BaselineMode.WPF.Presentation.ViewModels.Flux;

namespace BaselineMode.WPF.Presentation.Views.Flux
{
    public partial class FluxMainWindow : Window
    {
        private readonly DispatcherTimer _timeTimer;

        public FluxMainWindow(FluxViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Initialize the clock timer
            _timeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timeTimer.Tick += (s, e) =>
            {
                DateTimeLabel.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            };
            _timeTimer.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            _timeTimer.Stop();
            base.OnClosed(e);
        }
    }
}