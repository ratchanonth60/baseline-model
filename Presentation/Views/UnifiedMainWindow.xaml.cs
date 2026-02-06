using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ScottPlot;
using BaselineMode.WPF.Views.models;
using BaselineMode.WPF.Services.Observation;
using BaselineMode.WPF.Models.Observation;

namespace BaselineMode.WPF.Views
{
    public partial class UnifiedMainWindow : Window
    {
        // Observation Mode fields
        private string[]? _obsSelectedFiles;
        private string? _obsOutputDirectory;
        private readonly ObservationDataProcessor _obsDataProcessor;
        private readonly ObservationFittingService _obsFittingService;
        private readonly DispatcherTimer _obsDateTimeTimer;
        private Dictionary<string, int[]>? _obsProcessedData;
        
        // DSSD Data for Observation
        private int[]? _obsDSSDXData;
        private int[]? _obsDSSDYData;
        
        // Current mode tracking
        private bool _isObservationMode = false;

        public UnifiedMainWindow()
        {
            InitializeComponent();
            
            // Initialize Observation services
            _obsDataProcessor = new ObservationDataProcessor();
            _obsFittingService = new ObservationFittingService();
            
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
            // Ribbon tab changed - can be used for additional logic if needed
        }
        
        private void BtnSwitchToBaseline_Click(object sender, RoutedEventArgs e)
        {
            SwitchToBaselineMode();
        }
        
        private void BtnSwitchToObservation_Click(object sender, RoutedEventArgs e)
        {
            SwitchToObservationMode();
        }
        
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            SwitchToSettingsMode();
        }
        
        private void SwitchToBaselineMode()
        {
            _isObservationMode = false;
            if (BaselineModeContent == null) return;
            
            BaselineModeContent.Visibility = Visibility.Visible;
            ObservationModeContent.Visibility = Visibility.Collapsed;
            SettingsContent.Visibility = Visibility.Collapsed;
            
            TxtCurrentMode.Text = "📊 Baseline Mode";
            TxtModeDescription.Text = "Charged Particle Detector Analysis";
        }
        
        private void SwitchToObservationMode()
        {
            _isObservationMode = true;
            if (ObservationModeContent == null) return;
            
            BaselineModeContent.Visibility = Visibility.Collapsed;
            ObservationModeContent.Visibility = Visibility.Visible;
            SettingsContent.Visibility = Visibility.Collapsed;
            
            TxtCurrentMode.Text = "🔬 Observation Mode";
            TxtModeDescription.Text = "Particle Analysis & Detection";
        }
        
        private void SwitchToSettingsMode()
        {
            if (SettingsContent == null) return;
            
            BaselineModeContent.Visibility = Visibility.Collapsed;
            ObservationModeContent.Visibility = Visibility.Collapsed;
            SettingsContent.Visibility = Visibility.Visible;
            
            TxtCurrentMode.Text = "⚙️ Settings";
            TxtModeDescription.Text = "Application Configuration";
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
            wpfPlot.Plot.Clear();
            
            if (channelVm.BinCenters != null && channelVm.Counts != null && 
                channelVm.BinCenters.Length > 0 && channelVm.Counts.Length > 0)
            {
                wpfPlot.Plot.AddBar(channelVm.Counts, channelVm.BinCenters);
                wpfPlot.Plot.Title(channelVm.Title);
                wpfPlot.Plot.XLabel("Channel");
                wpfPlot.Plot.YLabel("Counts");
            }
            
            wpfPlot.Refresh();
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
            var dialog = new OpenFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                Multiselect = true,
                Title = "Select Data Files"
            };
            
            if (dialog.ShowDialog() == true)
            {
                _obsSelectedFiles = dialog.FileNames;
                _obsOutputDirectory = Path.GetDirectoryName(dialog.FileNames[0]);
                
                ObsTxtFileStatus.Text = $"Selected {_obsSelectedFiles.Length} file(s)";
                ObsTxtOutputFileName.Text = Path.GetFileNameWithoutExtension(dialog.FileNames[0]);
            }
        }
        
        private async void ObsBtnProcessData_Click(object sender, RoutedEventArgs e)
        {
            if (_obsSelectedFiles == null || _obsSelectedFiles.Length == 0)
            {
                MessageBox.Show("Please select files first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            try
            {
                ObsTxtProgress.Text = "Processing...";
                ObsProgressBar.IsIndeterminate = true;
                
                _obsProcessedData = await _obsDataProcessor.ProcessFilesAsync(_obsSelectedFiles);
                
                ObsTxtProgress.Text = "Processing complete!";
                ObsProgressBar.IsIndeterminate = false;
                ObsProgressBar.Value = 100;
                
                ObsTxtDataCount.Text = _obsProcessedData.Count.ToString();
                
                // Update DSSD plots
                UpdateObservationDSSDPlots();
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
            MessageBox.Show("Read Data functionality - loads previously processed data.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        
        private void ObsBtnReset_Click(object sender, RoutedEventArgs e)
        {
            _obsSelectedFiles = null;
            _obsProcessedData = null;
            _obsOutputDirectory = null;
            
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
            if (_obsSelectedFiles == null || _obsSelectedFiles.Length == 0)
            {
                MessageBox.Show("Please select files first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            try
            {
                var headerInfo = _obsDataProcessor.ReadHeader(_obsSelectedFiles[0]);
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
            if (!string.IsNullOrEmpty(_obsOutputDirectory) && Directory.Exists(_obsOutputDirectory))
            {
                System.Diagnostics.Process.Start("explorer.exe", _obsOutputDirectory);
            }
            else
            {
                MessageBox.Show("No output directory available.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
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
            if (_obsProcessedData == null || ObsPlotDSSDX == null || ObsPlotDSSDY == null)
                return;
            
            try
            {
                int xMin = int.TryParse(ObsTxtDSSDXMin?.Text, out int min) ? min : 0;
                int xMax = int.TryParse(ObsTxtDSSDXMax?.Text, out int max) ? max : 16384;
                
                // Clear and update plots
                ObsPlotDSSDX.Plot.Clear();
                ObsPlotDSSDY.Plot.Clear();
                
                // Get selected layer
                int layerIndex = ObsCmbDSSDLayer?.SelectedIndex ?? 0;
                string layerKey = layerIndex switch
                {
                    0 => "L1",
                    1 => "L2",
                    2 => "L6",
                    3 => "L7",
                    _ => "L1"
                };
                
                // Try to get data for the selected layer
                if (_obsProcessedData.TryGetValue($"DSSD{layerKey}_X", out int[]? xData))
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
                    
                    // Calculate stats
                    var (peak, mean, fwhm) = CalculateStats(ys);
                    ObsTxtDSSDXPeak.Text = peak.ToString("F2");
                    ObsTxtDSSDXMean.Text = mean.ToString("F2");
                    ObsTxtDSSDXFWHM.Text = fwhm.ToString("F2");
                }
                
                if (_obsProcessedData.TryGetValue($"DSSD{layerKey}_Y", out int[]? yData))
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
