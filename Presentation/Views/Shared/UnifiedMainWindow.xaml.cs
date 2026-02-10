using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ExcelDataReader;
using ScottPlot;
using BaselineMode.WPF.Presentation.ViewModels;
using BaselineMode.WPF.Presentation.ViewModels.Observation;
using BaselineMode.WPF.Infrastructure.Services.Observation;
using BaselineMode.WPF.Core.Models.Observation;
using BaselineMode.WPF.Core.Interfaces.Observation;
using BaselineMode.WPF.Views.Observation;

namespace BaselineMode.WPF.Views.Shared
{
    public partial class UnifiedMainWindow : Window
    {
        // Baseline Mode fields
        private readonly MainViewModel _mainViewModel;

        // Observation Mode fields
        private readonly ObservationMainViewModel _observationViewModel;
        private readonly DispatcherTimer _obsDateTimeTimer;

        // Observation state
        private string? _lastSavedFilePath;
        private int _totalSteps;
        private int _data = 1;
        private bool _stopFlag;
        private const string FORMAT_DATE = "yyyy-MMM-dd HH:mm:ss.fff";
        private const string NA = "N/A";

        public UnifiedMainWindow(MainViewModel mainViewModel, ObservationMainViewModel observationViewModel)
        {
            InitializeComponent();
            _mainViewModel = mainViewModel;
            _observationViewModel = observationViewModel;

            DataContext = mainViewModel;

            if (ViewBaseline != null) ViewBaseline.DataContext = mainViewModel;
            if (ViewObservation != null) ViewObservation.DataContext = observationViewModel;

            _mainViewModel.RequestPlotUpdate += OnBaselinePlotUpdate;
            _observationViewModel.RequestPlotUpdate += OnObservationPlotUpdate;

            _obsDateTimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _obsDateTimeTimer.Tick += (s, e) =>
            {
                if (ObsDateTimeLabel != null)
                    ObsDateTimeLabel.Text = DateTime.Now.ToString(FORMAT_DATE);
            };
            _obsDateTimeTimer.Start();

            this.Loaded += UnifiedMainWindow_Loaded;
        }

        private void UnifiedMainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeObservationPlots();
            SwitchToBaselineMode();
            UpdateToolbarVisibility();

            var graphSettingsPanel = this.FindName("GraphSettingsPanel") as Border;
            if (graphSettingsPanel != null)
            {
                graphSettingsPanel.DataContext = _observationViewModel;
            }
        }

        #region Ribbon Tab & Mode Navigation

        private void TabBaseline_Checked(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;
            SwitchToBaselineMode();
            UpdateToolbarVisibility();
        }

        private void TabObservation_Checked(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;
            SwitchToObservationMode();
            UpdateToolbarVisibility();
        }

        private void TabSettings_Checked(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;
            SwitchToSettingsMode();
            UpdateToolbarVisibility();
        }

        private void UpdateToolbarVisibility()
        {
            if (ToolbarBaseline == null || ToolbarObservation == null || ToolbarSettings == null) return;
            ToolbarBaseline.Visibility = TabBaseline?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ToolbarObservation.Visibility = TabObservation?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ToolbarSettings.Visibility = TabSettings?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnViewHistogram_Click(object sender, RoutedEventArgs e)
        {
            if (BtnViewHistogram != null && BtnViewDataTable != null)
            {
                BtnViewHistogram.Style = FindResource("WorkspaceTabBtnActive") as System.Windows.Style;
                BtnViewDataTable.Style = FindResource("WorkspaceTabBtn") as System.Windows.Style;
            }
            if (HistogramView != null && DataTableView != null)
            {
                HistogramView.Visibility = Visibility.Visible;
                DataTableView.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnViewDataTable_Click(object sender, RoutedEventArgs e)
        {
            if (BtnViewHistogram != null && BtnViewDataTable != null)
            {
                BtnViewHistogram.Style = FindResource("WorkspaceTabBtn") as System.Windows.Style;
                BtnViewDataTable.Style = FindResource("WorkspaceTabBtnActive") as System.Windows.Style;
            }
            if (HistogramView != null && DataTableView != null)
            {
                HistogramView.Visibility = Visibility.Collapsed;
                DataTableView.Visibility = Visibility.Visible;
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Theme settings will be available in a future update.", "Theme Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SwitchToBaselineMode()
        {
            if (ViewBaseline == null || ViewObservation == null || ViewSettings == null) return;
            ViewBaseline.Visibility = Visibility.Visible;
            ViewObservation.Visibility = Visibility.Collapsed;
            ViewSettings.Visibility = Visibility.Collapsed;
        }

        private void SwitchToObservationMode()
        {
            if (ViewBaseline == null || ViewObservation == null || ViewSettings == null) return;
            ViewBaseline.Visibility = Visibility.Collapsed;
            ViewObservation.Visibility = Visibility.Visible;
            ViewSettings.Visibility = Visibility.Collapsed;
        }

        private void SwitchToSettingsMode()
        {
            if (ViewBaseline == null || ViewObservation == null || ViewSettings == null) return;
            ViewBaseline.Visibility = Visibility.Collapsed;
            ViewObservation.Visibility = Visibility.Collapsed;
            ViewSettings.Visibility = Visibility.Visible;
        }

        #endregion

        #region Baseline Mode - WpfPlot Events

        private void WpfPlot_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is WpfPlot wpfPlot && wpfPlot.Tag is ChannelViewModel channelVm)
            {
                UpdatePlot(wpfPlot, channelVm);
                channelVm.PropertyChanged += (s, args) => UpdatePlot(wpfPlot, channelVm);
            }
        }

        private void UpdatePlot(WpfPlot wpfPlot, ChannelViewModel channelVm)
        {
            var figBg = ToDrawingColor(_mainViewModel.GraphFigureColor);
            var dataBg = ToDrawingColor(_mainViewModel.GraphDataColor);
            var foreColor = ToDrawingColor(_mainViewModel.GraphTextColor);
            var seriesColor = ToDrawingColor(_mainViewModel.GraphSeriesColor);

            channelVm.RenderTo(wpfPlot, figBg, dataBg, foreColor, seriesColor);
        }

        private void OnBaselinePlotUpdate(object? sender, Core.Models.PlotUpdateEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var figBg = ToDrawingColor(_mainViewModel.GraphFigureColor);
                var dataBg = ToDrawingColor(_mainViewModel.GraphDataColor);
                var foreColor = ToDrawingColor(_mainViewModel.GraphTextColor);
                var seriesColor = ToDrawingColor(_mainViewModel.GraphSeriesColor);

                foreach (var channel in _mainViewModel.Channels)
                {
                    channel.RenderPlot(figBg, dataBg, foreColor, seriesColor);
                }
            });
        }

        #endregion

        #region Observation Mode Events

        private void InitializeObservationPlots()
        {
            var allObsPlots = GetAllObservationPlots();
            foreach (var plot in allObsPlots)
            {
                if (plot != null)
                {
                    plot.Plot.Style(ScottPlot.Style.Gray1);
                    plot.Plot.Style(figureBackground: System.Drawing.Color.FromArgb(37, 37, 38));
                    plot.Plot.Style(dataBackground: System.Drawing.Color.FromArgb(40, 40, 40));
                    plot.Refresh();
                }
            }
        }

        private WpfPlot[] GetAllObservationPlots()
        {
            return new[]
            {
                ObsPlotDSSDX, ObsPlotDSSDY, ObsPlotBGOHigh, ObsPlotBGOLow,
                ObsPlotStripX1, ObsPlotStripX2, ObsPlotStripX3, ObsPlotStripX4,
                ObsPlotStripX5, ObsPlotStripX6, ObsPlotStripX7, ObsPlotStripX8,
                ObsPlotStripY1, ObsPlotStripY2, ObsPlotStripY3, ObsPlotStripY4,
                ObsPlotStripY5, ObsPlotStripY6, ObsPlotStripY7, ObsPlotStripY8
            };
        }

        private void ObsBtnSelectFiles_Click(object sender, RoutedEventArgs e)
        {
            _observationViewModel.SelectFilesCommand.Execute(null);
            if (_observationViewModel.InputFileList != null && _observationViewModel.InputFileList.Length > 0)
            {
                ObsTxtStatus.Text = $"{_observationViewModel.InputFileList.Length} file(s)";
                ObsTxtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
                ObsTxtOutputFileName.Text = Path.GetFileNameWithoutExtension(_observationViewModel.InputFileList[0]);
                ObsTxtProgress.Text = "Files loaded";
                UpdateObsStatus(_observationViewModel.StatusMessage);
            }
        }

        private void OnObservationPlotUpdate(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                RefreshDSSDPlots();
                RefreshBGOPlots();
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
                _observationViewModel.OutputFileName = ObsTxtOutputFileName.Text;
                _observationViewModel.UseCustomSavePath = ObsChkCustomSave.IsChecked == true;

                ObsTxtProgress.Text = "Processing...";
                ObsProgressBar.IsIndeterminate = true;
                ObsTxtStatus.Text = "BUSY";
                ObsTxtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xB3, 0x47));

                // 1. Convert/Save to Excel (with optional dialog)
                if (_observationViewModel.ConvertFilesToExcelCommand.CanExecute(null))
                {
                    await _observationViewModel.ConvertFilesToExcelCommand.ExecuteAsync(null);
                }

                // 2. Analyze for plotting
                if (_observationViewModel.AnalyzeFilesCommand.CanExecute(null))
                {
                    await _observationViewModel.AnalyzeFilesCommand.ExecuteAsync(null);
                }

                ObsTxtProgress.Text = "Processing complete";
                ObsProgressBar.IsIndeterminate = false;
                ObsProgressBar.Value = 100;
                ObsTxtStatus.Text = "DONE";
                ObsTxtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));

                // Refresh all plots
                RefreshDSSDPlots();
                RefreshBGOPlots();

                UpdateObsStatus("Processing and saving complete");
            }
            catch (Exception ex)
            {
                ObsTxtProgress.Text = "Error!";
                ObsProgressBar.IsIndeterminate = false;
                ObsTxtStatus.Text = "ERROR";
                ObsTxtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF4, 0x43, 0x36));
                MessageBox.Show($"Processing error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ObsBtnReadData_Click(object sender, RoutedEventArgs e)
        {
            string outputName = ObsTxtOutputFileName.Text.Trim();

            // Use FileHelper to find the file (searches Documents/DSSD_Analysis, Debug folder, etc.)
            string? fileName = _observationViewModel.FileHelper.FindExcelFile(outputName);

            if (fileName == null)
            {
                var result = MessageBox.Show(
                    $"File '{outputName}' not found in default locations.\nDo you want to browse for the file manually?",
                    "File Not Found",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var openFileDialog = new OpenFileDialog
                    {
                        Filter = "Excel Files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                        Title = "Select Particle Data File"
                    };

                    string initialDir = _observationViewModel.FileHelper.GetOutputFolder("");
                    if (Directory.Exists(initialDir))
                        openFileDialog.InitialDirectory = initialDir;

                    if (openFileDialog.ShowDialog() == true)
                    {
                        fileName = openFileDialog.FileName;
                    }
                    else
                    {
                        return; // User cancelled
                    }
                }
                else
                {
                    return;
                }
            }

            try
            {
                using var stream = File.Open(fileName, FileMode.Open, FileAccess.Read);
                using var reader = ExcelReaderFactory.CreateReader(stream);

                var result = reader.AsDataSet();
                var rawData = result.Tables[0];
                _totalSteps = rawData.Rows.Count;

                ObsTxtDataCount.Text = $"{_totalSteps}";
                ObsTxtParticleCount.Text = $"{_totalSteps * 5}";

                ObsProgressBar.Maximum = _totalSteps;
                ObsProgressBar.Value = 0;
                ObsProgressBar.IsIndeterminate = false;
                _data = 1;
                _stopFlag = false;

                bool isFirstData = true;
                string[] hexData = Array.Empty<string>();

                while (_data <= _totalSteps && !_stopFlag)
                {
                    string? hexString = rawData.Rows[_data - 1][0].ToString();
                    if (hexString == null) { _data++; continue; }
                    hexData = _observationViewModel.DataProcessor.SplitHexData(hexString);

                    ObsProgressBar.Value = _data;
                    ObsTxtProgress.Text = $"Processing... {Math.Round((double)_data / _totalSteps * 100)}%";

                    if (isFirstData)
                    {
                        ObsTxtStartTime.Text = _observationViewModel.DataProcessor.GetDateTimeFromHexData(hexData).ToString(FORMAT_DATE);
                        isFirstData = false;
                    }

                    _observationViewModel.DataProcessor.ProcessParticles(hexData);
                    _data++;

                    Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                }

                if (_totalSteps > 0)
                {
                    string? lastHex = rawData.Rows[_totalSteps - 1][0].ToString();
                    if (lastHex != null)
                    {
                        var lastHexData = _observationViewModel.DataProcessor.SplitHexData(lastHex);
                        ObsTxtStopTime.Text = _observationViewModel.DataProcessor.GetDateTimeFromHexData(lastHexData).ToString(FORMAT_DATE);
                    }
                }

                ObsTxtProgress.Text = "Process Complete";
                ObsTxtStatus.Text = "DONE";
                ObsTxtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));

                // Refresh all plots
                RefreshDSSDPlots();
                RefreshBGOPlots();

                // Save results
                _lastSavedFilePath = _observationViewModel.ExcelHelper.SaveAllResultsToExcel(
                    ObsTxtOutputFileName.Text, _observationViewModel.DataProcessor.AllResults);
                _observationViewModel.DataProcessor.AllResults.Clear();

                UpdateObsStatus("Processing complete");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ObsBtnReset_Click(object sender, RoutedEventArgs e)
        {
            _stopFlag = true;
            _data = 1;

            if (_observationViewModel.ResetCommand.CanExecute(null))
                _observationViewModel.ResetCommand.Execute(null);

            ObsTxtOutputFileName.Text = "";
            ObsTxtProgress.Text = "Ready";
            ObsProgressBar.Value = 0;
            ObsProgressBar.IsIndeterminate = false;
            ObsTxtStatus.Text = "READY";
            ObsTxtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
            ObsTxtDataCount.Text = "-";
            ObsTxtParticleCount.Text = "-";
            ObsTxtStartTime.Text = "-";
            ObsTxtStopTime.Text = "-";

            // Clear all plots
            foreach (var plot in GetAllObservationPlots())
            {
                if (plot != null)
                {
                    plot.Plot.Clear();
                    plot.Refresh();
                }
            }

            UpdateObsStatus("Reset complete");
        }

        private void ObsBtnHeaderCheck_Click(object sender, RoutedEventArgs e)
        {
            string outputName = ObsTxtOutputFileName.Text.Trim();

            // Use FileHelper to find the file
            string? fileName = _observationViewModel.FileHelper.FindExcelFile(outputName);

            if (fileName == null)
            {
                var result = MessageBox.Show(
                    $"File '{outputName}' not found in default locations.\nDo you want to browse for the file manually?",
                    "File Not Found",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var openFileDialog = new OpenFileDialog
                    {
                        Filter = "Excel Files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                        Title = "Select Particle Data File"
                    };

                    string initialDir = _observationViewModel.FileHelper.GetOutputFolder("");
                    if (Directory.Exists(initialDir))
                        openFileDialog.InitialDirectory = initialDir;

                    if (openFileDialog.ShowDialog() == true)
                    {
                        fileName = openFileDialog.FileName;
                    }
                    else
                    {
                        return; // User cancelled
                    }
                }
                else
                {
                    return;
                }
            }

            try
            {
                using var stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = ExcelReaderFactory.CreateReader(stream);

                var result = reader.AsDataSet();
                var rawData = result.Tables[0];
                int totalSteps = rawData.Rows.Count;
                bool headerOk = true;

                for (int i = 1; i <= totalSteps; i++)
                {
                    string? hexString = rawData.Rows[i - 1][0].ToString();
                    if (hexString == null || !hexString.StartsWith(ObservationConstants.HeaderStart))
                    {
                        ObsTxtProgress.Text = $"Header INCORRECT at row {i}";
                        ObsTxtStatus.Text = "HEADER ERR";
                        ObsTxtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF4, 0x43, 0x36));
                        headerOk = false;
                        break;
                    }
                }

                if (headerOk)
                {
                    ObsTxtProgress.Text = "✓ Header is correct!";
                    ObsTxtStatus.Text = "HEADER OK";
                    ObsTxtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
                }
            }
            catch (Exception ex)
            {
                ObsTxtStatus.Text = "HEADER ERR";
                ObsTxtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF4, 0x43, 0x36));
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ObsBtnOpenResults_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_lastSavedFilePath) && File.Exists(_lastSavedFilePath))
            {
                Process.Start(new ProcessStartInfo { FileName = _lastSavedFilePath, UseShellExecute = true });
            }
            else
            {
                // Fallback: try opening Source folder
                string projectDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string folderPath = Path.Combine(projectDirectory, ObservationConstants.SourceFolderName);
                if (Directory.Exists(folderPath))
                    Process.Start("explorer.exe", folderPath);
                else
                    MessageBox.Show("No result file found.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        #endregion

        #region Observation - DSSD Plot Handlers

        private void ObsCmbDSSDLayer_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshDSSDPlots();

        private void ObsTxtDSSDAxisChanged(object sender, TextChangedEventArgs e)
        {
            if (!double.TryParse(ObsTxtDSSDXMin?.Text, out double xMin)) xMin = 0;
            if (!double.TryParse(ObsTxtDSSDXMax?.Text, out double xMax)) xMax = ObservationConstants.ChartXMaxDSSD;

            if (ObsPlotDSSDX != null) { ObsPlotDSSDX.Plot.SetAxisLimits(xMin: xMin, xMax: xMax); ObsPlotDSSDX.Refresh(); }
            if (ObsPlotDSSDY != null) { ObsPlotDSSDY.Plot.SetAxisLimits(xMin: xMin, xMax: xMax); ObsPlotDSSDY.Refresh(); }
        }

        private void ObsChkDSSDFit_Changed(object sender, RoutedEventArgs e) => RefreshDSSDPlots();

        private void RefreshDSSDPlots()
        {
            if (_observationViewModel?.DataProcessor == null) return;

            int layerIndex = ObsCmbDSSDLayer?.SelectedIndex ?? 0;
            DetectorLayer layerKey = layerIndex switch
            {
                0 => DetectorLayer.L1,
                1 => DetectorLayer.L2,
                2 => DetectorLayer.L6,
                3 => DetectorLayer.L7,
                _ => DetectorLayer.L1
            };

            var layerData = _observationViewModel.GetDSSDLayerData(layerKey);

            // Main X/Y plots
            PlotHistogram(ObsPlotDSSDX, layerData?.PulseHeightX?.ToArray(), "Pulse Height (X)",
                ObsTxtDSSDXPeak, ObsTxtDSSDXCounts, ObsTxtDSSDXMean, ObsTxtDSSDXRMS, ObsTxtDSSDXFWHM, ObsTxtDSSDXRes);
            PlotHistogram(ObsPlotDSSDY, layerData?.PulseHeightY?.ToArray(), "Pulse Height (Y)",
                ObsTxtDSSDYPeak, ObsTxtDSSDYCounts, ObsTxtDSSDYMean, ObsTxtDSSDYRMS, ObsTxtDSSDYFWHM, ObsTxtDSSDYRes);

            // Strip X plots
            if (layerData != null)
            {
                var stripX = new List<int>[8];
                var stripY = new List<int>[8];
                for (int i = 0; i < 8; i++)
                {
                    stripX[i] = layerData.StripX.ContainsKey(i + 1) ? layerData.StripX[i + 1] : new List<int>();
                    stripY[i] = layerData.StripY.ContainsKey(i + 1) ? layerData.StripY[i + 1] : new List<int>();
                }

                PlotStripHistogram(ObsPlotStripX1, stripX[0]?.Select(x => (double)x).ToArray(), "X1", ObsTxtX1Peak, ObsTxtX1Counts, ObsTxtX1Mean, ObsTxtX1RMS, ObsTxtX1FWHM, ObsTxtX1Res);
                PlotStripHistogram(ObsPlotStripX2, stripX[1]?.Select(x => (double)x).ToArray(), "X2", ObsTxtX2Peak, ObsTxtX2Counts, ObsTxtX2Mean, ObsTxtX2RMS, ObsTxtX2FWHM, ObsTxtX2Res);
                PlotStripHistogram(ObsPlotStripX3, stripX[2]?.Select(x => (double)x).ToArray(), "X3", ObsTxtX3Peak, ObsTxtX3Counts, ObsTxtX3Mean, ObsTxtX3RMS, ObsTxtX3FWHM, ObsTxtX3Res);
                PlotStripHistogram(ObsPlotStripX4, stripX[3]?.Select(x => (double)x).ToArray(), "X4", ObsTxtX4Peak, ObsTxtX4Counts, ObsTxtX4Mean, ObsTxtX4RMS, ObsTxtX4FWHM, ObsTxtX4Res);
                PlotStripHistogram(ObsPlotStripX5, stripX[4]?.Select(x => (double)x).ToArray(), "X5", ObsTxtX5Peak, ObsTxtX5Counts, ObsTxtX5Mean, ObsTxtX5RMS, ObsTxtX5FWHM, ObsTxtX5Res);
                PlotStripHistogram(ObsPlotStripX6, stripX[5]?.Select(x => (double)x).ToArray(), "X6", ObsTxtX6Peak, ObsTxtX6Counts, ObsTxtX6Mean, ObsTxtX6RMS, ObsTxtX6FWHM, ObsTxtX6Res);
                PlotStripHistogram(ObsPlotStripX7, stripX[6]?.Select(x => (double)x).ToArray(), "X7", ObsTxtX7Peak, ObsTxtX7Counts, ObsTxtX7Mean, ObsTxtX7RMS, ObsTxtX7FWHM, ObsTxtX7Res);
                PlotStripHistogram(ObsPlotStripX8, stripX[7]?.Select(x => (double)x).ToArray(), "X8", ObsTxtX8Peak, ObsTxtX8Counts, ObsTxtX8Mean, ObsTxtX8RMS, ObsTxtX8FWHM, ObsTxtX8Res);

                PlotStripHistogram(ObsPlotStripY1, stripY[0]?.Select(x => (double)x).ToArray(), "Y1", ObsTxtY1Peak, ObsTxtY1Counts, ObsTxtY1Mean, ObsTxtY1RMS, ObsTxtY1FWHM, ObsTxtY1Res);
                PlotStripHistogram(ObsPlotStripY2, stripY[1]?.Select(x => (double)x).ToArray(), "Y2", ObsTxtY2Peak, ObsTxtY2Counts, ObsTxtY2Mean, ObsTxtY2RMS, ObsTxtY2FWHM, ObsTxtY2Res);
                PlotStripHistogram(ObsPlotStripY3, stripY[2]?.Select(x => (double)x).ToArray(), "Y3", ObsTxtY3Peak, ObsTxtY3Counts, ObsTxtY3Mean, ObsTxtY3RMS, ObsTxtY3FWHM, ObsTxtY3Res);
                PlotStripHistogram(ObsPlotStripY4, stripY[3]?.Select(x => (double)x).ToArray(), "Y4", ObsTxtY4Peak, ObsTxtY4Counts, ObsTxtY4Mean, ObsTxtY4RMS, ObsTxtY4FWHM, ObsTxtY4Res);
                PlotStripHistogram(ObsPlotStripY5, stripY[4]?.Select(x => (double)x).ToArray(), "Y5", ObsTxtY5Peak, ObsTxtY5Counts, ObsTxtY5Mean, ObsTxtY5RMS, ObsTxtY5FWHM, ObsTxtY5Res);
                PlotStripHistogram(ObsPlotStripY6, stripY[5]?.Select(x => (double)x).ToArray(), "Y6", ObsTxtY6Peak, ObsTxtY6Counts, ObsTxtY6Mean, ObsTxtY6RMS, ObsTxtY6FWHM, ObsTxtY6Res);
                PlotStripHistogram(ObsPlotStripY7, stripY[6]?.Select(x => (double)x).ToArray(), "Y7", ObsTxtY7Peak, ObsTxtY7Counts, ObsTxtY7Mean, ObsTxtY7RMS, ObsTxtY7FWHM, ObsTxtY7Res);
                PlotStripHistogram(ObsPlotStripY8, stripY[7]?.Select(x => (double)x).ToArray(), "Y8", ObsTxtY8Peak, ObsTxtY8Counts, ObsTxtY8Mean, ObsTxtY8RMS, ObsTxtY8FWHM, ObsTxtY8Res);
            }
        }

        #endregion

        #region Observation - BGO Plot Handlers

        private void ObsCmbBGOLayer_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshBGOPlots();

        private void ObsTxtBGOAxisChanged(object sender, TextChangedEventArgs e)
        {
            if (!double.TryParse(ObsTxtBGOXMin?.Text, out double xMin)) xMin = 0;
            if (!double.TryParse(ObsTxtBGOXMax?.Text, out double xMax)) xMax = ObservationConstants.ChartXMaxBGO;

            if (ObsPlotBGOHigh != null) { ObsPlotBGOHigh.Plot.SetAxisLimits(xMin: xMin, xMax: xMax); ObsPlotBGOHigh.Refresh(); }
            if (ObsPlotBGOLow != null) { ObsPlotBGOLow.Plot.SetAxisLimits(xMin: xMin, xMax: xMax); ObsPlotBGOLow.Refresh(); }
        }

        private void RefreshBGOPlots()
        {
            if (_observationViewModel?.DataProcessor == null) return;

            int layerIndex = ObsCmbBGOLayer?.SelectedIndex ?? 0;
            BGOLayer layerKey = layerIndex switch
            {
                0 => BGOLayer.L3,
                1 => BGOLayer.L4,
                2 => BGOLayer.L5,
                _ => BGOLayer.L3
            };

            var bgoData = _observationViewModel.GetBGOLayerData(layerKey);

            PlotBGOHistogram(ObsPlotBGOHigh, bgoData?.HighGain?.ToArray(), "BGO High Gain",
                ObsTxtBGOHPeak, ObsTxtBGOHMean, ObsTxtBGOHRMS, ObsTxtBGOHFWHM, ObsTxtBGOHRes);
            PlotBGOHistogram(ObsPlotBGOLow, bgoData?.LowGain?.ToArray(), "BGO Low Gain",
                ObsTxtBGOLPeak, ObsTxtBGOLMean, ObsTxtBGOLRMS, ObsTxtBGOLFWHM, ObsTxtBGOLRes);
        }

        #endregion

        #region Plot Helpers

        private System.Drawing.Color ToDrawingColor(System.Windows.Media.Color mediaColor)
        {
            return System.Drawing.Color.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B);
        }

        private System.Drawing.Color GetBackgroundColor()
        {
            return ToDrawingColor(_observationViewModel.SelectedGraphBackground);
        }

        private System.Drawing.Color GetForegroundColor()
        {
            var c = _observationViewModel.SelectedGraphBackground;
            // Simple luminance calculation
            double luma = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            return luma < 128 ? System.Drawing.Color.White : System.Drawing.Color.Black;
        }



        private void PlotHistogram(WpfPlot? plot, double[]? data, string title,
            TextBlock? peakLabel, TextBlock? countsLabel, TextBlock? meanLabel,
            TextBlock? rmsLabel, TextBlock? fwhmLabel, TextBlock? resLabel)
        {
            if (plot == null) return;

            plot.Plot.Clear();
            plot.Plot.Style(figureBackground: GetBackgroundColor(), dataBackground: GetBackgroundColor());
            plot.Plot.XAxis.Label(label: "Pulse Height (Channel)", color: GetForegroundColor());
            plot.Plot.YAxis.Label(label: "Counts", color: GetForegroundColor());
            plot.Plot.XAxis.TickLabelStyle(color: GetForegroundColor());
            plot.Plot.YAxis.TickLabelStyle(color: GetForegroundColor());
            plot.Plot.Title(title, color: GetForegroundColor());

            // Reset labels first
            if (peakLabel != null) peakLabel.Text = "-";
            if (countsLabel != null) countsLabel.Text = "-";
            if (meanLabel != null) meanLabel.Text = "-";
            if (rmsLabel != null) rmsLabel.Text = "-";
            if (fwhmLabel != null) fwhmLabel.Text = "-";
            if (resLabel != null) resLabel.Text = "-";

            if (data == null || data.Length == 0 || data.All(v => v == 0))
            {
                plot.Plot.AddText("No data", 0, 0, size: 14, color: System.Drawing.Color.Gray);
                plot.Plot.SetAxisLimits(-1, 1, -1, 1);
                plot.Refresh();
                return;
            }

            var filteredData = data.Where(v => v > 0).ToArray();
            if (filteredData.Length == 0)
            {
                plot.Plot.AddText("No data > 0", 0, 0, size: 14, color: System.Drawing.Color.Gray);
                plot.Refresh();
                return;
            }

            if (countsLabel != null) countsLabel.Text = filteredData.Length.ToString("N0");

            var (hist, binEdges) = ScottPlot.Statistics.Common.Histogram(filteredData, binCount: 4096);
            double[] binMidpoints = new double[hist.Length];
            for (int i = 0; i < hist.Length; i++)
                binMidpoints[i] = (binEdges[i] + binEdges[i + 1]) / 2.0;

            var bar = plot.Plot.AddBar(hist, binMidpoints);
            bar.FillColor = ToDrawingColor(_observationViewModel.SelectedDSSDColor);

            // Calculate basic stats manually first
            if (peakLabel != null) peakLabel.Text = $"{hist.Max()}";
            if (meanLabel != null) meanLabel.Text = $"{filteredData.Average():F2}";

            double avg = filteredData.Average();
            double sumSquares = filteredData.Sum(d => Math.Pow(d - avg, 2));
            double stdDev = Math.Sqrt(sumSquares / filteredData.Length);
            if (rmsLabel != null) rmsLabel.Text = $"{stdDev:F2}";

            // Fitting logic
            if (ObsChkDSSDFit?.IsChecked == true)
            {
                try
                {
                    var fitResult = _observationViewModel.FittingService.GaussianFit(binMidpoints, hist);
                    if (fitResult != null)
                    {
                        plot.Plot.AddScatter(binMidpoints, fitResult.FitCurve, System.Drawing.Color.Red, lineWidth: 2);
                        plot.Plot.AddPoint(fitResult.Mu, fitResult.Peak, color: System.Drawing.Color.Yellow, size: 8);

                        if (peakLabel != null) peakLabel.Text = $"{fitResult.Peak:F0}";
                        if (meanLabel != null) meanLabel.Text = $"{fitResult.Mu:F2}";
                        if (rmsLabel != null) rmsLabel.Text = $"{fitResult.Sigma:F2}";
                        if (fwhmLabel != null) fwhmLabel.Text = $"{fitResult.FWHM:F2}";
                        if (resLabel != null) resLabel.Text = $"{fitResult.Resolution:F2}%";
                    }
                }
                catch { /* Fitting failed */ }
            }

            plot.Plot.SetAxisLimits(yMin: 0);
            plot.Refresh();
        }

        private void PlotStripHistogram(WpfPlot? plot, double[]? data, string title,
             TextBlock? peak, TextBlock? counts, TextBlock? mean, TextBlock? rms, TextBlock? fwhm, TextBlock? res)
        {
            PlotHistogram(plot, data, title, peak, counts, mean, rms, fwhm, res);
        }

        private void PlotBGOHistogram(WpfPlot? plot, double[]? data, string title,
             TextBlock? peak, TextBlock? mean, TextBlock? rms, TextBlock? fwhm, TextBlock? res)
        {
            if (plot == null) return;

            plot.Plot.Clear();
            plot.Plot.Style(figureBackground: GetBackgroundColor(), dataBackground: GetBackgroundColor());
            plot.Plot.XAxis.Label(label: "Channel", color: GetForegroundColor());
            plot.Plot.YAxis.Label(label: "Counts", color: GetForegroundColor());
            plot.Plot.XAxis.TickLabelStyle(color: GetForegroundColor());
            plot.Plot.YAxis.TickLabelStyle(color: GetForegroundColor());
            plot.Plot.Title(title, color: GetForegroundColor());

            // Reset labels
            if (peak != null) peak.Text = "-";
            if (mean != null) mean.Text = "-";
            if (rms != null) rms.Text = "-";
            if (fwhm != null) fwhm.Text = "-";
            if (res != null) res.Text = "-";

            if (data == null || data.Length == 0)
            {
                plot.Plot.AddText("No data", 0, 0, size: 14, color: System.Drawing.Color.Gray);
                plot.Refresh();
                return;
            }

            var filteredData = data.Where(v => v > 0).ToArray();
            if (filteredData.Length == 0)
            {
                plot.Plot.AddText("No data", 0, 0, size: 14, color: System.Drawing.Color.Gray);
                plot.Refresh();
                return;
            }

            var (hist, binEdges) = ScottPlot.Statistics.Common.Histogram(filteredData, binCount: 1024);
            double[] binMidpoints = new double[hist.Length];
            for (int i = 0; i < hist.Length; i++)
                binMidpoints[i] = (binEdges[i] + binEdges[i + 1]) / 2.0;

            var bar = plot.Plot.AddBar(hist, binMidpoints);
            bar.FillColor = ToDrawingColor(_observationViewModel.SelectedBGOColor);

            if (peak != null) peak.Text = $"{hist.Max()}";
            if (mean != null) mean.Text = $"{filteredData.Average():F2}";

            double avg = filteredData.Average();
            double sumSquares = filteredData.Sum(d => Math.Pow(d - avg, 2));
            double stdDev = Math.Sqrt(sumSquares / filteredData.Length);
            if (rms != null) rms.Text = $"{stdDev:F2}";

            double fwhmVal = 2.355 * stdDev;
            if (fwhm != null) fwhm.Text = $"{fwhmVal:F2}";
            if (res != null) res.Text = $"{(fwhmVal / avg * 100):F2}%";

            plot.Refresh();
        }

        private void UpdateObsStatus(string message)
        {
            TxtStatusBar.Text = message;
        }


        private void ObsPlot_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not WpfPlot plot) return;

            string tag = plot.Tag?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(tag)) return;

            double[]? data = null;
            string title = tag;

            // Determine Layer for DSSD
            // Uses standard names if controls are not accessible, but they should be
            int dssdLayerIndex = 0;
            var dssdCombo = this.FindName("ObsCmbDSSDLayer") as ComboBox;
            if (dssdCombo != null) dssdLayerIndex = dssdCombo.SelectedIndex;

            DetectorLayer dssdLayer = dssdLayerIndex switch
            {
                0 => DetectorLayer.L1,
                1 => DetectorLayer.L2,
                2 => DetectorLayer.L6,
                3 => DetectorLayer.L7,
                _ => DetectorLayer.L1
            };

            // Determine Layer for BGO
            int bgoLayerIndex = 0;
            var bgoCombo = this.FindName("ObsCmbBGOLayer") as ComboBox;
            if (bgoCombo != null) bgoLayerIndex = bgoCombo.SelectedIndex;

            BGOLayer bgoLayer = bgoLayerIndex switch
            {
                0 => BGOLayer.L3,
                1 => BGOLayer.L4,
                2 => BGOLayer.L5,
                _ => BGOLayer.L3
            };

            bool showFit = false;

            if (tag == "PulseHeight_X")
            {
                data = _observationViewModel.GetDSSDLayerData(dssdLayer)?.PulseHeightX?.ToArray();
                title = $"Pulse Height X ({dssdLayer})";
                showFit = ObsChkDSSDFit?.IsChecked == true;
            }
            else if (tag == "PulseHeight_Y")
            {
                data = _observationViewModel.GetDSSDLayerData(dssdLayer)?.PulseHeightY?.ToArray();
                title = $"Pulse Height Y ({dssdLayer})";
                showFit = ObsChkDSSDFit?.IsChecked == true;
            }
            else if (tag.StartsWith("StripX_"))
            {
                if (int.TryParse(tag.Substring(7), out int stripNum))
                {
                    var layerData = _observationViewModel.GetDSSDLayerData(dssdLayer);
                    if (layerData != null && layerData.StripX.ContainsKey(stripNum))
                    {
                        data = layerData.StripX[stripNum].Select(x => (double)x).ToArray();
                        title = $"Strip X{stripNum} ({dssdLayer})";
                        showFit = ObsChkDSSDFit?.IsChecked == true;
                    }
                }
            }
            else if (tag.StartsWith("StripY_"))
            {
                if (int.TryParse(tag.Substring(7), out int stripNum))
                {
                    var layerData = _observationViewModel.GetDSSDLayerData(dssdLayer);
                    if (layerData != null && layerData.StripY.ContainsKey(stripNum))
                    {
                        data = layerData.StripY[stripNum].Select(x => (double)x).ToArray();
                        title = $"Strip Y{stripNum} ({dssdLayer})";
                        showFit = ObsChkDSSDFit?.IsChecked == true;
                    }
                }
            }
            else if (tag == "BGO_High")
            {
                data = _observationViewModel.GetBGOLayerData(bgoLayer)?.HighGain?.ToArray();
                title = $"BGO High Gain ({bgoLayer})";
                showFit = false; // BGO doesn't have a fit checkbox in main window
            }
            else if (tag == "BGO_Low")
            {
                data = _observationViewModel.GetBGOLayerData(bgoLayer)?.LowGain?.ToArray();
                title = $"BGO Low Gain ({bgoLayer})";
                showFit = false; // BGO doesn't have a fit checkbox in main window
            }

            if (data == null || data.Length == 0)
            {
                MessageBox.Show($"No data available for {title}.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var detailWindow = new ObservationDetailWindow(_observationViewModel.FittingService);
            detailWindow.ShowHistogram(data, title, showFit);
            detailWindow.Show();
        }
        #endregion
    }
}
