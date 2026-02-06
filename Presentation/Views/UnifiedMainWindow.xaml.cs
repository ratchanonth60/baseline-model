using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ScottPlot;
using BaselineMode.WPF.Presentation.ViewModels;
using BaselineMode.WPF.Presentation.ViewModels.Observation;
using BaselineMode.WPF.Infrastructure.Services.Observation;
using BaselineMode.WPF.Core.Models.Observation;
using BaselineMode.WPF.Core.Interfaces.Observation;

namespace BaselineMode.WPF.Views
{
    public partial class UnifiedMainWindow : Window
    {
        // Baseline Mode fields
        private readonly MainViewModel _mainViewModel;

        // Observation Mode fields
        private readonly ObservationMainViewModel _observationViewModel;
        private readonly DispatcherTimer _obsDateTimeTimer;

        // DSSD Data for Observation - effectively a cache for plotting
        private int[]? _obsDSSDXData;
        private int[]? _obsDSSDYData;

        public UnifiedMainWindow(MainViewModel mainViewModel, ObservationMainViewModel observationViewModel)
        {
            InitializeComponent();
            _mainViewModel = mainViewModel;
            DataContext = mainViewModel; // Keep MainViewModel for Baseline mode as default? 
            // Better: Set specific contexts
            BaselineModeContent.DataContext = mainViewModel;
            ObservationModeContent.DataContext = observationViewModel;

            _observationViewModel = observationViewModel;

            // Subscribe to VM events
            _mainViewModel.RequestPlotUpdate += OnBaselinePlotUpdate;
            _observationViewModel.RequestPlotUpdate += OnObservationPlotUpdate;

            // Setup datetime timer
            _obsDateTimeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _obsDateTimeTimer.Tick += (s, e) => ObsDateTimeLabel.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _obsDateTimeTimer.Start();

            // Initialize plots
            InitializeObservationPlots();

            // Set initial mode
            SwitchToBaselineMode();
        }

        #region Ribbon Tab & Mode Navigation

        private void RibbonTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RibbonTabControl?.SelectedIndex == null) return;

            switch (RibbonTabControl.SelectedIndex)
            {
                case 0: // Baseline Tab
                    SwitchToBaselineMode();
                    break;
                case 1: // Observation Tab
                    SwitchToObservationMode();
                    break;
                case 2: // Settings Tab - keep showing Baseline content, settings are in Ribbon
                    SwitchToBaselineMode();
                    break;
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            SwitchToSettingsMode();
        }

        private void SwitchToBaselineMode()
        {
            if (BaselineModeContent == null) return;

            BaselineModeContent.Visibility = Visibility.Visible;
            ObservationModeContent.Visibility = Visibility.Collapsed;
            SettingsContent.Visibility = Visibility.Collapsed;
        }

        private void SwitchToObservationMode()
        {
            if (ObservationModeContent == null) return;

            BaselineModeContent.Visibility = Visibility.Collapsed;
            ObservationModeContent.Visibility = Visibility.Visible;
            SettingsContent.Visibility = Visibility.Collapsed;
        }

        private void SwitchToSettingsMode()
        {
            if (SettingsContent == null) return;

            BaselineModeContent.Visibility = Visibility.Collapsed;
            ObservationModeContent.Visibility = Visibility.Collapsed;
            SettingsContent.Visibility = Visibility.Visible;
        }

        #endregion

        #region Baseline Mode - WpfPlot Events

        private void WpfPlot_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is WpfPlot wpfPlot && wpfPlot.Tag is ChannelViewModel channelVm)
            {
                UpdatePlot(wpfPlot, channelVm);

                // Subscribe to changes
                channelVm.PropertyChanged += (s, args) => UpdatePlot(wpfPlot, channelVm);
            }
        }

        private void UpdatePlot(WpfPlot wpfPlot, ChannelViewModel channelVm)
        {
            channelVm.RenderTo(wpfPlot);
        }

        private void OnBaselinePlotUpdate(object? sender, Core.Models.PlotUpdateEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Refresh all channel plots
                foreach (var channel in _mainViewModel.Channels)
                {
                    channel.RenderPlot();
                }
            });
        }

        #endregion

        #region Observation Mode Events

        private void InitializeObservationPlots()
        {
            if (ObsPlotDSSDX != null)
            {
                ObsPlotDSSDX.Plot.Title("Pulse Height X");
                ObsPlotDSSDX.Plot.XLabel("ADC Channel");
                ObsPlotDSSDX.Plot.YLabel("Counts");
                ObsPlotDSSDX.Refresh();
            }

            if (ObsPlotDSSDY != null)
            {
                ObsPlotDSSDY.Plot.Title("Pulse Height Y");
                ObsPlotDSSDY.Plot.XLabel("ADC Channel");
                ObsPlotDSSDY.Plot.YLabel("Counts");
                ObsPlotDSSDY.Refresh();
            }
        }

        private void ObsBtnSelectFiles_Click(object sender, RoutedEventArgs e)
        {
            _observationViewModel.SelectFilesCommand.Execute(null);

            // Sync UI text (Ideally bind in XAML)
            if (_observationViewModel.InputFileList != null)
            {
                ObsTxtFileStatus.Text = $"Selected {_observationViewModel.InputFileList.Length} file(s)";
                if (_observationViewModel.InputFileList.Length > 0)
                {
                    ObsTxtOutputFileName.Text = System.IO.Path.GetFileNameWithoutExtension(_observationViewModel.InputFileList[0]);
                }
            }
        }

        private void OnObservationPlotUpdate(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateObservationDSSDPlots();
            });
        }

        private async void ObsBtnProcessData_Click(object sender, RoutedEventArgs e)
        {
            if (_observationViewModel.InputFileList == null || _observationViewModel.InputFileList.Length == 0)
            {
                MessageBox.Show("Please select files first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // UI updates via binding preferred, but manual for now for legacy compatibility
                ObsTxtProgress.Text = "Processing...";
                ObsProgressBar.IsIndeterminate = true;

                // Execute Command
                if (_observationViewModel.AnalyzeFilesCommand.CanExecute(null))
                {
                    await _observationViewModel.AnalyzeFilesCommand.ExecuteAsync(null);
                }

                // Status update handled by bindings or this after-await
                ObsTxtProgress.Text = _observationViewModel.StatusMessage;
                ObsProgressBar.IsIndeterminate = false;
                ObsProgressBar.Value = 100;

                // Note: IsBusy binding in XAML would be better.
            }
            catch (Exception ex)
            {
                ObsTxtProgress.Text = "Error!";
                ObsProgressBar.IsIndeterminate = false;
                MessageBox.Show($"Processing error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ObsBtnReadData_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Read Data functionality to be implemented via ViewModel.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ObsBtnReset_Click(object sender, RoutedEventArgs e)
        {
            // ViewModel reset logic here if available
            // For now, clear UI
            ObsTxtFileStatus.Text = "No files selected";
            ObsTxtOutputFileName.Text = "";
            ObsTxtProgress.Text = "Ready";
            ObsProgressBar.Value = 0;
            ObsTxtDataCount.Text = "-";
            ObsTxtParticleCount.Text = "-";
            ObsTxtStartTime.Text = "-";
            ObsTxtStopTime.Text = "-";
            ObsTxtHeaderData.Text = "";

            // Clear plots
            ObsPlotDSSDX?.Plot.Clear();
            ObsPlotDSSDX?.Refresh();
            ObsPlotDSSDY?.Plot.Clear();
            ObsPlotDSSDY?.Refresh();
        }

        private void ObsBtnHeaderCheck_Click(object sender, RoutedEventArgs e)
        {
            // Header check logic can be moved to VM
            if (_observationViewModel.InputFileList == null || _observationViewModel.InputFileList.Length == 0)
            {
                MessageBox.Show("Please select files first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Using DataProcessor via ViewModel interface
                var headerInfo = _observationViewModel.DataProcessor.ReadHeader(_observationViewModel.InputFileList[0]);
                ObsTxtHeaderData.Text = headerInfo;
                ObsTxtHeaderStatus.Text = "Header check passed ✓";
            }
            catch (Exception ex)
            {
                ObsTxtHeaderStatus.Text = "Header check failed";
                MessageBox.Show($"Error reading header: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ObsBtnOpenResults_Click(object sender, RoutedEventArgs e)
        {
            // Infer directory from input list if available or from a stored property in VM
            // For now, assume input list path
            if (_observationViewModel.InputFileList != null && _observationViewModel.InputFileList.Length > 0)
            {
                var dir = System.IO.Path.GetDirectoryName(_observationViewModel.InputFileList[0]);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    System.Diagnostics.Process.Start("explorer.exe", dir);
                }
                else
                {
                    MessageBox.Show("Output directory not found.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show("No files selected to determine output directory.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ObsCmbDSSDLayer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateObservationDSSDPlots();
        }

        private void ObsTxtDSSDAxisChanged(object sender, TextChangedEventArgs e)
        {
            UpdateObservationDSSDPlots();
        }

        private void ObsChkDSSDFit_Changed(object sender, RoutedEventArgs e)
        {
            UpdateObservationDSSDPlots();
        }

        private void UpdateObservationDSSDPlots()
        {
            if (ObsPlotDSSDX == null || ObsPlotDSSDY == null)
                return;

            try
            {
                int xMin = int.TryParse(ObsTxtDSSDXMin?.Text, out int min) ? min : 0;
                int xMax = int.TryParse(ObsTxtDSSDXMax?.Text, out int max) ? max : 16384;

                // Clear plots
                ObsPlotDSSDX.Plot.Clear();
                ObsPlotDSSDY.Plot.Clear();

                // Get selected layer
                int layerIndex = ObsCmbDSSDLayer?.SelectedIndex ?? 0;
                // Map index to Enum: 0=L1, 1=L2, 2=L6, 3=L7
                // And generate key string for dictionary
                string layerKey = layerIndex switch
                {
                    0 => "L1",
                    1 => "L2",
                    2 => "L6",
                    3 => "L7",
                    _ => "L1"
                };

                // Get Data from ViewModel (HistogramData which is Dictionary<string, int[]>)
                var data = _observationViewModel.HistogramData;

                if (data != null)
                {
                    // Update X Plot
                    if (data.TryGetValue($"DSSD{layerKey}_X", out int[]? xData) && xData != null)
                    {
                        _obsDSSDXData = xData;
                        double[] xs = new double[xData.Length];
                        double[] ys = new double[xData.Length];
                        for (int i = 0; i < xData.Length && i < xMax; i++)
                        {
                            if (i >= xMin)
                            {
                                xs[i] = i;
                                ys[i] = xData[i];
                            }
                        }
                        ObsPlotDSSDX.Plot.AddBar(ys, xs);

                        var (peak, mean, fwhm) = CalculateStats(ys);
                        ObsTxtDSSDXPeak.Text = peak.ToString("F2");
                        ObsTxtDSSDXMean.Text = mean.ToString("F2");
                        ObsTxtDSSDXFWHM.Text = fwhm.ToString("F2");
                    }

                    // Update Y Plot
                    if (data.TryGetValue($"DSSD{layerKey}_Y", out int[]? yData) && yData != null)
                    {
                        _obsDSSDYData = yData;
                        double[] xs = new double[yData.Length];
                        double[] ys = new double[yData.Length];
                        for (int i = 0; i < yData.Length && i < xMax; i++)
                        {
                            if (i >= xMin)
                            {
                                xs[i] = i;
                                ys[i] = yData[i];
                            }
                        }
                        ObsPlotDSSDY.Plot.AddBar(ys, xs);

                        var (peak, mean, fwhm) = CalculateStats(ys);
                        ObsTxtDSSDYPeak.Text = peak.ToString("F2");
                        ObsTxtDSSDYMean.Text = mean.ToString("F2");
                        ObsTxtDSSDYFWHM.Text = fwhm.ToString("F2");
                    }
                }

                ObsPlotDSSDX.Plot.SetAxisLimits(xMin, xMax, 0, null);
                ObsPlotDSSDY.Plot.SetAxisLimits(xMin, xMax, 0, null);

                ObsPlotDSSDX.Refresh();
                ObsPlotDSSDY.Refresh();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating DSSD plots: {ex.Message}");
            }
        }

        private (double peak, double mean, double fwhm) CalculateStats(double[] data)
        {
            if (data == null || data.Length == 0)
                return (0, 0, 0);

            double peak = 0;
            int peakIndex = 0;
            double sum = 0;
            double weightedSum = 0;

            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] > peak)
                {
                    peak = data[i];
                    peakIndex = i;
                }
                sum += data[i];
                weightedSum += i * data[i];
            }

            double mean = sum > 0 ? weightedSum / sum : 0;

            // Calculate FWHM
            double halfMax = peak / 2;
            int leftIndex = peakIndex;
            int rightIndex = peakIndex;

            while (leftIndex > 0 && data[leftIndex] > halfMax) leftIndex--;
            while (rightIndex < data.Length - 1 && data[rightIndex] > halfMax) rightIndex++;

            double fwhm = rightIndex - leftIndex;

            return (peakIndex, mean, fwhm);
        }

        #endregion
    }
}
