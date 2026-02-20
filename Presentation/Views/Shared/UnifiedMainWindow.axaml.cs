using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ScottPlot.Avalonia;
using BaselineMode.WPF.Presentation.ViewModels;
using BaselineMode.WPF.Presentation.ViewModels.Observation;
using BaselineMode.WPF.Presentation.ViewModels.Shared;
using BaselineMode.WPF.Infrastructure.Services.Observation;
using BaselineMode.WPF.Core.Models;
using BaselineMode.WPF.Core.Models.Shared;
using BaselineMode.WPF.Core.Models.Observation;
using BaselineMode.WPF.Core.Interfaces.Observation;
using BaselineMode.WPF.Views.Observation;
using BaselineMode.WPF.Presentation.ViewModels.Baseline;
using BaselineMode.WPF.Presentation.ViewModels.Flux;
using BaselineMode.WPF.Core.Helpers;
using BaselineMode.WPF.Infrastructure.Services;

namespace BaselineMode.WPF.Views.Shared
{
    public partial class UnifiedMainWindow : Window
    {
        // Baseline Mode fields
        private readonly MainViewModel _mainViewModel;

        // Observation Mode fields
        private readonly ObservationViewModel _observationViewModel;
        private readonly DispatcherTimer _obsDateTimeTimer;

        // Observation state
        private string? _lastSavedFilePath;
        private const string FORMAT_DATE = "yyyy-MMM-dd HH:mm:ss.fff";
        private const string NA = "N/A";
        private const double DEFAULT_X_MAX = 4096;
        private const int DEFAULT_FIT_WINDOW = 100;

        public UnifiedMainWindow()
        {
            InitializeComponent();
            _mainViewModel = null!;
            _observationViewModel = null!;
            _obsDateTimeTimer = null!;
        }

        public UnifiedMainWindow(MainViewModel mainViewModel, ObservationViewModel observationViewModel)
        {
            InitializeComponent();
            _mainViewModel = mainViewModel;
            _observationViewModel = observationViewModel;

            DataContext = mainViewModel;
            mainViewModel.ObservationVM = observationViewModel;

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

        public ObservationViewModel ObservationViewModel => _observationViewModel;

        private void UnifiedMainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            InitializeObservationPlots();
            SwitchToBaselineMode();
            UpdateToolbarVisibility();
        }

        #region Ribbon Tab & Mode Navigation

        private void TabBaseline_Checked(object? sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;
            SwitchToBaselineMode();
            UpdateToolbarVisibility();
        }

        private void TabObservation_Checked(object? sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;
            SwitchToObservationMode();
            UpdateToolbarVisibility();
        }

        private void TabCalibration_Checked(object? sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;
            SwitchToCalibrationMode();
            UpdateToolbarVisibility();
        }

        private void TabFlux_Checked(object? sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;
            SwitchToFluxMode();
            UpdateToolbarVisibility();
        }

        private void TabSettings_Checked(object? sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;
            SwitchToSettingsMode();
            UpdateToolbarVisibility();
        }

        private void UpdateToolbarVisibility()
        {
            if (ToolbarBaseline == null || ToolbarObservation == null || ToolbarCalibration == null || ToolbarFlux == null || ToolbarSettings == null) return;
            ToolbarBaseline.IsVisible = TabBaseline?.IsChecked == true;
            ToolbarObservation.IsVisible = TabObservation?.IsChecked == true;
            ToolbarCalibration.IsVisible = TabCalibration?.IsChecked == true;
            ToolbarFlux.IsVisible = TabFlux?.IsChecked == true;
            ToolbarSettings.IsVisible = TabSettings?.IsChecked == true;
        }

        private void BtnViewHistogram_Click(object? sender, RoutedEventArgs e)
        {
            if (BtnViewHistogram != null && BtnViewDataTable != null)
            {
                BtnViewHistogram.Classes.Set("WorkspaceTabBtnActive", true);
                BtnViewHistogram.Classes.Set("WorkspaceTabBtn", false);
                BtnViewDataTable.Classes.Set("WorkspaceTabBtnActive", false);
                BtnViewDataTable.Classes.Set("WorkspaceTabBtn", true);
            }
            if (HistogramView != null && DataTableView != null)
            {
                HistogramView.IsVisible = true;
                DataTableView.IsVisible = false;
            }
        }

        private void BtnViewDataTable_Click(object? sender, RoutedEventArgs e)
        {
            if (BtnViewHistogram != null && BtnViewDataTable != null)
            {
                BtnViewHistogram.Classes.Set("WorkspaceTabBtnActive", false);
                BtnViewHistogram.Classes.Set("WorkspaceTabBtn", true);
                BtnViewDataTable.Classes.Set("WorkspaceTabBtnActive", true);
                BtnViewDataTable.Classes.Set("WorkspaceTabBtn", false);
            }
            if (HistogramView != null && DataTableView != null)
            {
                HistogramView.IsVisible = false;
                DataTableView.IsVisible = true;
            }
        }

        private void BtnSettings_Click(object? sender, RoutedEventArgs e)
        {
            MessageBoxService.Show("Theme settings will be available in a future update.", "Theme Settings");
        }

        private void BtnModeSelector_Click(object? sender, RoutedEventArgs e)
        {
            // Navigate to mode selector (implementation depends on window management)
        }

        private void SwitchToBaselineMode()
        {
            if (ViewBaseline == null || ViewObservation == null || ViewCalibration == null || ViewFlux == null || ViewSettings == null) return;
            ViewBaseline.IsVisible = true;
            ViewObservation.IsVisible = false;
            ViewCalibration.IsVisible = false;
            ViewFlux.IsVisible = false;
            ViewSettings.IsVisible = false;
        }

        private void SwitchToObservationMode()
        {
            if (ViewBaseline == null || ViewObservation == null || ViewCalibration == null || ViewSettings == null) return;
            ViewBaseline.IsVisible = false;
            ViewObservation.IsVisible = true;
            ViewCalibration.IsVisible = false;
            ViewFlux.IsVisible = false;
            ViewSettings.IsVisible = false;
        }

        private void SwitchToCalibrationMode()
        {
            if (ViewBaseline == null || ViewObservation == null || ViewCalibration == null || ViewFlux == null || ViewSettings == null) return;
            ViewBaseline.IsVisible = false;
            ViewObservation.IsVisible = false;
            ViewCalibration.IsVisible = true;
            ViewFlux.IsVisible = false;
            ViewSettings.IsVisible = false;
        }

        private void SwitchToFluxMode()
        {
            if (ViewBaseline == null || ViewObservation == null || ViewCalibration == null || ViewFlux == null || ViewSettings == null) return;
            ViewBaseline.IsVisible = false;
            ViewObservation.IsVisible = false;
            ViewCalibration.IsVisible = false;
            ViewFlux.IsVisible = true;
            ViewSettings.IsVisible = false;
        }

        private void SwitchToSettingsMode()
        {
            if (ViewBaseline == null || ViewObservation == null || ViewSettings == null || ViewFlux == null || ViewCalibration == null) return;
            ViewBaseline.IsVisible = false;
            ViewObservation.IsVisible = false;
            ViewCalibration.IsVisible = false;
            ViewFlux.IsVisible = false;
            ViewSettings.IsVisible = true;
        }

        #endregion

        #region Baseline Mode - AvaPlot Events

        private void AvaPlot_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is AvaPlot avaPlot && avaPlot.Tag is ChannelViewModel channelVm)
            {
                UpdatePlot(avaPlot, channelVm);
                channelVm.PropertyChanged += (s, args) => UpdatePlot(avaPlot, channelVm);
            }
        }

        private void UpdatePlot(AvaPlot avaPlot, ChannelViewModel channelVm)
        {
            var figBg = ColorHelper.ToDrawingColor(_mainViewModel.GraphFigureColor);
            var dataBg = ColorHelper.ToDrawingColor(_mainViewModel.GraphDataColor);
            var foreColor = ColorHelper.ToDrawingColor(_mainViewModel.GraphTextColor);
            var seriesColor = ColorHelper.ToDrawingColor(_mainViewModel.GraphSeriesColor);

            channelVm.RenderTo(avaPlot, figBg, dataBg, foreColor, seriesColor);
        }

        private void OnBaselinePlotUpdate(object? sender, PlotUpdateEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var figBg = ColorHelper.ToDrawingColor(_mainViewModel.GraphFigureColor);
                var dataBg = ColorHelper.ToDrawingColor(_mainViewModel.GraphDataColor);
                var foreColor = ColorHelper.ToDrawingColor(_mainViewModel.GraphTextColor);
                var seriesColor = ColorHelper.ToDrawingColor(_mainViewModel.GraphSeriesColor);

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
                    // ScottPlot 5
                    plot.Plot.FigureBackground.Color = new ScottPlot.Color(37, 37, 38);
                    plot.Plot.DataBackground.Color = new ScottPlot.Color(40, 40, 40);
                    plot.Refresh();
                }
            }
        }

        private AvaPlot[] GetAllObservationPlots()
        {
            return
            [
                ObsPlotDSSDX, ObsPlotDSSDY, ObsPlotBGOHigh, ObsPlotBGOLow,
                ObsPlotStripX1, ObsPlotStripX2, ObsPlotStripX3, ObsPlotStripX4,
                ObsPlotStripX5, ObsPlotStripX6, ObsPlotStripX7, ObsPlotStripX8,
                ObsPlotStripY1, ObsPlotStripY2, ObsPlotStripY3, ObsPlotStripY4,
                ObsPlotStripY5, ObsPlotStripY6, ObsPlotStripY7, ObsPlotStripY8
            ];
        }

        private void ObsBtnSelectFiles_Click(object? sender, RoutedEventArgs e)
        {
            _observationViewModel.SelectFilesCommand.Execute(null);
            if (_observationViewModel.InputFileList != null && _observationViewModel.InputFileList.Length > 0)
            {
                ObsTxtStatus.Text = $"{_observationViewModel.InputFileList.Length} file(s)";
                ObsTxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                ObsTxtOutputFileName.Text = Path.GetFileNameWithoutExtension(_observationViewModel.InputFileList[0]);
                ObsTxtProgress.Text = "Files loaded";
                UpdateObsStatus(_observationViewModel.StatusMessage);
            }
        }

        private void OnObservationPlotUpdate(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var bgColor = GetBackgroundColor();
                var fgColor = GetForegroundColor();

                var allPlots = GetAllObservationPlots();
                foreach (var plot in allPlots)
                {
                    if (plot == null) continue;
                    plot.Plot.FigureBackground.Color = bgColor;
                    plot.Plot.DataBackground.Color = bgColor;

                    // Style Axes
                    var spFgColor = fgColor;
                    plot.Plot.Axes.Color(spFgColor);
                    plot.Plot.Axes.Title.Label.ForeColor = spFgColor;
                    plot.Plot.Axes.Bottom.Label.ForeColor = spFgColor;
                    plot.Plot.Axes.Left.Label.ForeColor = spFgColor;

                    plot.Refresh();
                }

                RefreshDSSDPlots();
                RefreshBGOPlots();
            });
        }

        private async void ObsBtnExport_Click(object? sender, RoutedEventArgs e)
        {
            if (_observationViewModel.InputFileList == null || _observationViewModel.InputFileList.Length == 0)
            {
                MessageBoxService.Show("Please select files first.", "Warning");
                return;
            }

            try
            {
                var outputName = ObsTxtOutputFileName.Text?.Trim();
                if (string.IsNullOrEmpty(outputName)) outputName = "output";
                var defaultFileName = outputName + ".xlsx";

                var storageProvider = this.StorageProvider;
                var result = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Observation Data",
                    DefaultExtension = ".xlsx",
                    SuggestedFileName = defaultFileName,
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("Excel Files") { Patterns = new[] { "*.xlsx" } }
                    }
                });

                if (result != null)
                {
                    ObsTxtProgress.Text = "Exporting...";
                    ObsProgressBar.IsIndeterminate = true;
                    ObsTxtStatus.Text = "BUSY";
                    ObsTxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x47));

                    _observationViewModel.OutputFileName = outputName;

                    await _observationViewModel.ExportToPathAsync(result.Path.LocalPath);

                    ObsProgressBar.IsIndeterminate = false;
                    ObsTxtProgress.Text = _observationViewModel.StatusMessage;
                    ObsTxtStatus.Text = "DONE";
                    ObsTxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                }
            }
            catch (Exception ex)
            {
                ObsProgressBar.IsIndeterminate = false;
                ObsTxtProgress.Text = $"Export failed: {ex.Message}";
                ObsTxtStatus.Text = "ERROR";
                ObsTxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
            }
        }

        private async void ObsBtnProcessData_Click(object? sender, RoutedEventArgs e)
        {
            if (_observationViewModel.InputFileList == null || _observationViewModel.InputFileList.Length == 0)
            {
                MessageBoxService.Show("Please select files first.", "Warning");
                return;
            }

            try
            {
                _observationViewModel.OutputFileName = ObsTxtOutputFileName.Text;
                ObsTxtProgress.Text = "Processing...";
                ObsProgressBar.IsIndeterminate = true;
                ObsTxtStatus.Text = "BUSY";
                ObsTxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x47));

                if (_observationViewModel.ConvertFilesToExcelCommand.CanExecute(null))
                    await _observationViewModel.ConvertFilesToExcelCommand.ExecuteAsync(null);

                if (_observationViewModel.AnalyzeFilesCommand.CanExecute(null))
                    await _observationViewModel.AnalyzeFilesCommand.ExecuteAsync(null);

                ObsTxtProgress.Text = "Processing complete";
                ObsProgressBar.IsIndeterminate = false;
                ObsProgressBar.Value = 100;
                ObsTxtStatus.Text = "DONE";
                ObsTxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));

                RefreshDSSDPlots();
                RefreshBGOPlots();
                UpdateObsStatus("Processing and saving complete");
            }
            catch (Exception ex)
            {
                ObsTxtProgress.Text = "Error!";
                ObsProgressBar.IsIndeterminate = false;
                ObsTxtStatus.Text = "ERROR";
                ObsTxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
                MessageBoxService.Show($"Processing error: {ex.Message}", "Error");
            }
        }

        private CancellationTokenSource? _obsCts;

        private async void ObsBtnReadData_Click(object? sender, RoutedEventArgs e)
        {
            string outputName = ObsTxtOutputFileName.Text?.Trim() ?? "";

            string? fileName = _observationViewModel.FileHelper.FindExcelFile(outputName);
            if (fileName != null) return;

            var msgResult = MessageBoxService.Show(
                $"File '{outputName}' not found in default locations.\nDo you want to browse for the file manually?",
                "File Not Found");

            if (msgResult != MsgBoxResult.Yes) return;

            var storageProvider = this.StorageProvider;
            var openResult = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Particle Data File",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Excel Files") { Patterns = new[] { "*.xlsx" } },
                    new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
                }
            });

            if (openResult == null || openResult.Count == 0) return;
            fileName = openResult[0].Path.LocalPath;

            _obsCts = new CancellationTokenSource();
            var progress = new Progress<ObservationProcessReport>(report =>
            {
                ObsProgressBar.Maximum = 100;
                ObsProgressBar.Value = report.TotalSteps > 0
                    ? (double)report.CurrentStep / report.TotalSteps * 100.0
                    : 0;
                ObsTxtProgress.Text = report.Message;
                ObsTxtDataCount.Text = $"{report.TotalSteps}";
                ObsTxtParticleCount.Text = $"{report.TotalSteps * 5}";

                if (report.CurrentTime.HasValue)
                {
                    ObsTxtStopTime.Text = report.CurrentTime.Value.ToString(FORMAT_DATE);
                }

                if (report.IsComplete)
                {
                    ObsTxtStatus.Text = "DONE";
                    ObsTxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                    RefreshDSSDPlots();
                    RefreshBGOPlots();
                    _observationViewModel.DataProcessor.AllResults.Clear();
                }
            });

            try
            {
                ObsProgressBar.IsIndeterminate = false;
                ObsTxtStatus.Text = "PROCESSING";
                ObsTxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x47));

                await _observationViewModel.ProcessExcelDataAsync(fileName, progress, _obsCts.Token);

                if (!_obsCts.IsCancellationRequested)
                {
                    var saveResult = await _observationViewModel.ExcelHelper.SaveAllResultsToExcelAsync(
                       ObsTxtOutputFileName.Text ?? "", _observationViewModel.DataProcessor.AllResults);
                    if (saveResult.IsSuccess)
                        _lastSavedFilePath = saveResult.Value;
                    _observationViewModel.DataProcessor.AllResults.Clear();
                    UpdateObsStatus("Processing and saving complete");
                }
            }
            catch (OperationCanceledException)
            {
                UpdateObsStatus("Processing cancelled");
            }
            catch (Exception ex)
            {
                MessageBoxService.Show($"Error: {ex.Message}", "Error");
                UpdateObsStatus("Error");
            }
        }

        private void ObsBtnReset_Click(object? sender, RoutedEventArgs e)
        {
            _obsCts?.Cancel();
            _obsCts = null;

            if (_observationViewModel.ResetCommand.CanExecute(null))
                _observationViewModel.ResetCommand.Execute(null);

            ObsTxtOutputFileName.Text = "";
            ObsTxtProgress.Text = "Ready";
            ObsProgressBar.Maximum = 100;
            ObsProgressBar.Value = 0;
            ObsProgressBar.IsIndeterminate = false;
            ObsTxtStatus.Text = "READY";
            ObsTxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            ObsTxtDataCount.Text = "-";
            ObsTxtParticleCount.Text = "-";
            ObsTxtStartTime.Text = "-";
            ObsTxtStopTime.Text = "-";

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

        private async void ObsBtnHeaderCheck_Click(object? sender, RoutedEventArgs e)
        {
            string outputName = ObsTxtOutputFileName.Text?.Trim() ?? "";
            string? fileName = _observationViewModel.FileHelper.FindExcelFile(outputName);

            if (fileName == null)
            {
                MessageBoxService.Show($"File '{outputName}' not found.", "File Not Found");
                return;
            }

            var (isValid, message, _) = await ObservationViewModel.CheckHeaderAsync(fileName);

            ObsTxtProgress.Text = message;
            ObsTxtStatus.Text = "HEADER ERR";
            ObsTxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
            if (isValid)
            {
                ObsTxtProgress.Text = message;
                ObsTxtStatus.Text = "HEADER OK";
                ObsTxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            }
        }

        private void ObsBtnOpenResults_Click(object? sender, RoutedEventArgs e)
        {
            string? lastSaved = _observationViewModel.LastSavedFilePath;

            if (!string.IsNullOrEmpty(lastSaved) && File.Exists(lastSaved))
            {
                Process.Start(new ProcessStartInfo { FileName = lastSaved, UseShellExecute = true });
            }
            else
            {
                string outputDir = _observationViewModel.OutputDirectoryPath;
                if (Directory.Exists(outputDir))
                {
                    Process.Start(new ProcessStartInfo { FileName = outputDir, UseShellExecute = true });
                }
                else
                {
                    string projectDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    string folderPath = Path.Combine(projectDirectory, AppConstants.SourceFolderName);
                    if (Directory.Exists(folderPath))
                        Process.Start(new ProcessStartInfo { FileName = folderPath, UseShellExecute = true });
                    else
                        MessageBoxService.Show("No result file or output folder found.", "Info");
                }
            }
        }

        #endregion

        #region Observation - DSSD Plot Handlers

        private void ObsCmbDSSDLayer_SelectionChanged(object? sender, SelectionChangedEventArgs e) => RefreshDSSDPlots();

        private void ObsTxtDSSDAxisChanged(object? sender, TextChangedEventArgs e) => RefreshDSSDPlots();

        private void ObsChkDSSDFit_Changed(object? sender, RoutedEventArgs e) => RefreshDSSDPlots();

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

            PlotHistogram(ObsPlotDSSDX, layerData?.PulseHeightX?.ToArray(), "Pulse Height (X)",
                ObsTxtDSSDXPeak, ObsTxtDSSDXCounts, ObsTxtDSSDXMean, ObsTxtDSSDXRMS, ObsTxtDSSDXFWHM, ObsTxtDSSDXRes);
            PlotHistogram(ObsPlotDSSDY, layerData?.PulseHeightY?.ToArray(), "Pulse Height (Y)",
                ObsTxtDSSDYPeak, ObsTxtDSSDYCounts, ObsTxtDSSDYMean, ObsTxtDSSDYRMS, ObsTxtDSSDYFWHM, ObsTxtDSSDYRes);

            if (layerData != null)
            {
                var stripX = new List<int>[8];
                var stripY = new List<int>[8];
                for (int i = 0; i < 8; i++)
                {
                    stripX[i] = layerData.StripX.ContainsKey(i + 1) ? layerData.StripX[i + 1] : [];
                    stripY[i] = layerData.StripY.ContainsKey(i + 1) ? layerData.StripY[i + 1] : [];
                }

                // Note: Strip stat labels are simplified - using the main ObsTxt labels for stats
                PlotStripHistogram(ObsPlotStripX1, stripX[0]?.Select(x => (double)x).ToArray(), "X1", null, null, null, null, null, null);
                PlotStripHistogram(ObsPlotStripX2, stripX[1]?.Select(x => (double)x).ToArray(), "X2", null, null, null, null, null, null);
                PlotStripHistogram(ObsPlotStripX3, stripX[2]?.Select(x => (double)x).ToArray(), "X3", null, null, null, null, null, null);
                PlotStripHistogram(ObsPlotStripX4, stripX[3]?.Select(x => (double)x).ToArray(), "X4", null, null, null, null, null, null);
                PlotStripHistogram(ObsPlotStripX5, stripX[4]?.Select(x => (double)x).ToArray(), "X5", null, null, null, null, null, null);
                PlotStripHistogram(ObsPlotStripX6, stripX[5]?.Select(x => (double)x).ToArray(), "X6", null, null, null, null, null, null);
                PlotStripHistogram(ObsPlotStripX7, stripX[6]?.Select(x => (double)x).ToArray(), "X7", null, null, null, null, null, null);
                PlotStripHistogram(ObsPlotStripX8, stripX[7]?.Select(x => (double)x).ToArray(), "X8", null, null, null, null, null, null);

                PlotStripHistogram(ObsPlotStripY1, stripY[0]?.Select(x => (double)x).ToArray(), "Y1", null, null, null, null, null, null);
                PlotStripHistogram(ObsPlotStripY2, stripY[1]?.Select(x => (double)x).ToArray(), "Y2", null, null, null, null, null, null);
                PlotStripHistogram(ObsPlotStripY3, stripY[2]?.Select(x => (double)x).ToArray(), "Y3", null, null, null, null, null, null);
                PlotStripHistogram(ObsPlotStripY4, stripY[3]?.Select(x => (double)x).ToArray(), "Y4", null, null, null, null, null, null);
                PlotStripHistogram(ObsPlotStripY5, stripY[4]?.Select(x => (double)x).ToArray(), "Y5", null, null, null, null, null, null);
                PlotStripHistogram(ObsPlotStripY6, stripY[5]?.Select(x => (double)x).ToArray(), "Y6", null, null, null, null, null, null);
                PlotStripHistogram(ObsPlotStripY7, stripY[6]?.Select(x => (double)x).ToArray(), "Y7", null, null, null, null, null, null);
                PlotStripHistogram(ObsPlotStripY8, stripY[7]?.Select(x => (double)x).ToArray(), "Y8", null, null, null, null, null, null);
            }
        }

        #endregion

        #region Observation - BGO Plot Handlers

        private void ObsCmbBGOLayer_SelectionChanged(object? sender, SelectionChangedEventArgs e) => RefreshBGOPlots();

        private void ObsChkBGOFit_Changed(object? sender, RoutedEventArgs e) => RefreshBGOPlots();

        private void ObsTxtBGOAxisChanged(object? sender, TextChangedEventArgs e) => RefreshBGOPlots();

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

        private ScottPlot.Color GetBackgroundColor()
        {
            return ColorHelper.ToScottPlotColor(_observationViewModel.SelectedGraphBackground, ScottPlot.Colors.Gray);
        }

        private ScottPlot.Color GetForegroundColor()
        {
            var c = _observationViewModel.SelectedGraphBackground;
            double luma = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            return luma < 128 ? ScottPlot.Colors.White : ScottPlot.Colors.Gray;
        }

        private void PlotHistogram(AvaPlot? plot, double[]? data, string title,
            TextBlock? peakLabel, TextBlock? countsLabel, TextBlock? meanLabel,
            TextBlock? rmsLabel, TextBlock? fwhmLabel, TextBlock? resLabel)
        {
            if (plot == null) return;

            plot.Plot.Clear();
            plot.Plot.FigureBackground.Color = GetBackgroundColor();
            plot.Plot.DataBackground.Color = GetBackgroundColor();

            var fgColor = GetForegroundColor();
            plot.Plot.Axes.Color(fgColor);
            plot.Plot.Axes.Left.Label.Text = "Counts";
            plot.Plot.Axes.Bottom.Label.Text = "Pulse Height (Channel)";
            plot.Plot.Title(title);

            if (peakLabel != null) peakLabel.Text = "-";
            if (countsLabel != null) countsLabel.Text = "-";
            if (meanLabel != null) meanLabel.Text = "-";
            if (rmsLabel != null) rmsLabel.Text = "-";
            if (fwhmLabel != null) fwhmLabel.Text = "-";
            if (resLabel != null) resLabel.Text = "-";

            if (data == null || data.Length == 0 || data.All(v => v == 0))
            {
                plot.Plot.Add.Text("No data", 0, 0);
                plot.Plot.Axes.SetLimits(-1, 1, -1, 1);
                plot.Refresh();
                return;
            }

            var filteredData = data.Where(v => v > 0).ToArray();
            if (filteredData.Length == 0)
            {
                plot.Plot.Add.Text("No data > 0", 0, 0);
                plot.Refresh();
                return;
            }

            if (countsLabel != null) countsLabel.Text = filteredData.Length.ToString("N0");

            double xMax = DEFAULT_X_MAX;
            if (double.TryParse(ObsTxtDSSDXMax?.Text, out double _max)) xMax = _max;

            int binCount = (int)xMax;
            if (binCount > 8192) binCount = 8192;
            if (binCount < DEFAULT_FIT_WINDOW) binCount = DEFAULT_FIT_WINDOW;

            var (hist, binEdges) = _observationViewModel.MathProvider.CalculateHistogram(filteredData, min: 0, max: xMax, binCount: binCount);

            double[] binMidpoints = new double[hist.Length];
            for (int i = 0; i < hist.Length; i++)
                binMidpoints[i] = (binEdges[i] + binEdges[i + 1]) / 2.0;

            var bars = new ScottPlot.Bar[hist.Length];
            for (int i = 0; i < hist.Length; i++)
            {
                bars[i] = new ScottPlot.Bar() { Position = binMidpoints[i], Value = hist[i] };
            }

            var barPlot = plot.Plot.Add.Bars(bars);
            barPlot.Color = ColorHelper.ToScottPlotColor(_observationViewModel.SelectedDSSDColor, ScottPlot.Colors.Orange);

            if (peakLabel != null) peakLabel.Text = $"{hist.Max()}";
            if (meanLabel != null) meanLabel.Text = $"{filteredData.Average():F2}";

            double avg = filteredData.Average();
            double sumSquares = filteredData.Sum(d => Math.Pow(d - avg, 2));
            double stdDev = Math.Sqrt(sumSquares / filteredData.Length);
            if (rmsLabel != null) rmsLabel.Text = $"{stdDev:F2}";

            if (ObsChkDSSDFit?.IsChecked == true)
            {
                try
                {
                    var fitConfigs = new List<(bool isEnabled, string label, ScottPlot.Color color, Func<double[], double[], BaselineMode.WPF.Core.Models.Baseline.FittingResult> fitFunc)>
                    {
                        (_observationViewModel.ShowGaussianFitDSSD, "Gaussian", ScottPlot.Colors.Red, _observationViewModel.MathProvider.GaussianFit),
                        (_observationViewModel.ShowLorentzianFitDSSD, "Lorentzian", ScottPlot.Colors.Cyan, _observationViewModel.MathProvider.LorentzianFit),
                        (_observationViewModel.ShowHemgFitDSSD, "HEMG", ScottPlot.Colors.Lime, _observationViewModel.MathProvider.HemgDoubleSidedFit)
                    };

                    double maxVal = hist.Max();
                    int peakIdx = Array.IndexOf(hist, maxVal);
                    int win = DEFAULT_FIT_WINDOW;
                    int start = Math.Max(0, peakIdx - win);
                    int end = Math.Min(hist.Length - 1, peakIdx + win);
                    int len = end - start;

                    if (len > 3)
                    {
                        double[] xFit = [.. binMidpoints.Skip(start).Take(len)];
                        double[] yFit = [.. hist.Skip(start).Take(len)];

                        foreach (var (isEnabled, label, color, fitFunc) in fitConfigs.Where(c => c.isEnabled))
                        {
                            var fitResult = fitFunc(xFit, yFit);
                            if (fitResult?.IsValid == true && fitResult.FitCurve != null && fitResult.FitCurve.Length == xFit.Length && !fitResult.FitCurve.Any(double.IsNaN))
                            {
                                var sp = plot.Plot.Add.Scatter(xFit, fitResult.FitCurve);
                                sp.LegendText = label;
                                sp.Color = color;
                                sp.LineWidth = 2;

                                var mp = plot.Plot.Add.Marker(fitResult.Mu, fitResult.Peak);
                                mp.Color = ScottPlot.Colors.Yellow;
                                mp.Size = 6;

                                if (peakLabel != null) peakLabel.Text = $"{fitResult.Peak:F2}";
                                if (meanLabel != null) meanLabel.Text = $"{fitResult.Mu:F2}";
                                if (rmsLabel != null) rmsLabel.Text = $"{fitResult.Sigma:F2}";
                                if (fwhmLabel != null) fwhmLabel.Text = $"{fitResult.FWHM:F2}";
                                if (resLabel != null) resLabel.Text = $"{fitResult.Resolution:F2}%";
                            }
                        }

                        if (fitConfigs.Count(c => c.isEnabled) > 1) plot.Plot.ShowLegend(ScottPlot.Alignment.UpperRight);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"DSSD Fitting error: {ex.Message}");
                }
            }

            double xMin = 0;
            if (double.TryParse(ObsTxtDSSDXMin?.Text, out double _min)) xMin = _min;

            plot.Plot.Axes.SetLimits(left: xMin, right: xMax, bottom: 0);
            plot.Refresh();
        }

        private void PlotStripHistogram(AvaPlot? plot, double[]? data, string title,
             TextBlock? peak, TextBlock? counts, TextBlock? mean, TextBlock? rms, TextBlock? fwhm, TextBlock? res)
        {
            PlotHistogram(plot, data, title, peak, counts, mean, rms, fwhm, res);
        }

        private void PlotBGOHistogram(AvaPlot? plot, double[]? data, string title,
             TextBlock? peak, TextBlock? mean, TextBlock? rms, TextBlock? fwhm, TextBlock? res)
        {
            if (plot == null) return;

            plot.Plot.Clear();
            plot.Plot.FigureBackground.Color = GetBackgroundColor();
            plot.Plot.DataBackground.Color = GetBackgroundColor();

            var fgColor = GetForegroundColor();
            plot.Plot.Axes.Color(fgColor);
            plot.Plot.Axes.Left.Label.Text = "Counts";
            plot.Plot.Axes.Bottom.Label.Text = "Channel";
            plot.Plot.Title(title);

            if (peak != null) peak.Text = "-";
            if (mean != null) mean.Text = "-";
            if (rms != null) rms.Text = "-";
            if (fwhm != null) fwhm.Text = "-";
            if (res != null) res.Text = "-";

            if (data == null || data.Length == 0)
            {
                plot.Plot.Add.Text("No data", 0, 0);
                plot.Refresh();
                return;
            }

            var filteredData = data.Where(v => v > 0).ToArray();
            if (filteredData.Length == 0)
            {
                plot.Plot.Add.Text("No data", 0, 0);
                plot.Refresh();
                return;
            }

            double xMax = DEFAULT_X_MAX;
            if (double.TryParse(ObsTxtBGOXMax?.Text, out double _max)) xMax = _max;

            int binCount = (int)xMax;
            if (binCount > 8192) binCount = 8192;
            if (binCount < DEFAULT_FIT_WINDOW) binCount = DEFAULT_FIT_WINDOW;

            var (hist, binEdges) = _observationViewModel.MathProvider.CalculateHistogram(filteredData, min: 0, max: xMax, binCount: binCount);

            double[] binMidpoints = new double[hist.Length];
            for (int i = 0; i < hist.Length; i++)
                binMidpoints[i] = (binEdges[i] + binEdges[i + 1]) / 2.0;

            var bars = new ScottPlot.Bar[hist.Length];
            for (int i = 0; i < hist.Length; i++)
            {
                bars[i] = new ScottPlot.Bar() { Position = binMidpoints[i], Value = hist[i] };
            }

            var barPlot = plot.Plot.Add.Bars(bars);
            barPlot.Color = ColorHelper.ToScottPlotColor(_observationViewModel.SelectedBGOColor, ScottPlot.Colors.Cyan);

            if (peak != null) peak.Text = $"{hist.Max()}";
            if (mean != null) mean.Text = $"{filteredData.Average():F2}";

            double avg = filteredData.Average();
            double sumSquares = filteredData.Sum(d => Math.Pow(d - avg, 2));
            double stdDev = Math.Sqrt(sumSquares / filteredData.Length);
            if (rms != null) rms.Text = $"{stdDev:F2}";

            double fwhmVal = 2.355 * stdDev;
            if (fwhm != null) fwhm.Text = $"{fwhmVal:F2}";
            if (res != null) res.Text = $"{(fwhmVal / avg * 100):F2}%";

            if (ObsChkBGOFit?.IsChecked == true)
            {
                try
                {
                    var fitConfigs = new List<(bool isEnabled, string label, ScottPlot.Color color, Func<double[], double[], BaselineMode.WPF.Core.Models.Baseline.FittingResult> fitFunc)>
                    {
                        (_observationViewModel.ShowGaussianFitBGO, "Gaussian", ScottPlot.Colors.Red, _observationViewModel.MathProvider.GaussianFit),
                        (_observationViewModel.ShowLorentzianFitBGO, "Lorentzian", ScottPlot.Colors.Cyan, _observationViewModel.MathProvider.LorentzianFit),
                        (_observationViewModel.ShowHemgFitBGO, "HEMG", ScottPlot.Colors.Lime, _observationViewModel.MathProvider.HemgDoubleSidedFit)
                    };

                    double maxVal = hist.Max();
                    int peakIdx = Array.IndexOf(hist, maxVal);
                    int win = DEFAULT_FIT_WINDOW;
                    int start = Math.Max(0, peakIdx - win);
                    int end = Math.Min(hist.Length - 1, peakIdx + win);
                    int len = end - start;

                    if (len > 3)
                    {
                        double[] xFit = [.. binMidpoints.Skip(start).Take(len)];
                        double[] yFit = [.. hist.Skip(start).Take(len)];

                        foreach (var (isEnabled, label, color, fitFunc) in fitConfigs.Where(c => c.isEnabled))
                        {
                            var fitResult = fitFunc(xFit, yFit);
                            if (fitResult?.IsValid == true && fitResult.FitCurve != null && fitResult.FitCurve.Length == xFit.Length && !fitResult.FitCurve.Any(double.IsNaN))
                            {
                                var sp = plot.Plot.Add.Scatter(xFit, fitResult.FitCurve);
                                sp.LegendText = label;
                                sp.Color = color;
                                sp.LineWidth = 2;

                                var mp = plot.Plot.Add.Marker(fitResult.Mu, fitResult.Peak);
                                mp.Color = ScottPlot.Colors.Yellow;
                                mp.Size = 6;

                                if (peak != null) peak.Text = $"{fitResult.Peak:F2}";
                                if (mean != null) mean.Text = $"{fitResult.Mu:F2}";
                                if (rms != null) rms.Text = $"{fitResult.Sigma:F2}";
                                if (fwhm != null) fwhm.Text = $"{fitResult.FWHM:F2}";
                                if (res != null) res.Text = $"{fitResult.Resolution:F2}%";
                            }
                        }

                        if (fitConfigs.Count(c => c.isEnabled) > 1) plot.Plot.ShowLegend(ScottPlot.Alignment.UpperRight);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"BGO Fitting error: {ex.Message}");
                }
            }

            plot.Plot.Axes.SetLimits(left: 0, right: xMax, bottom: 0);

            plot.Refresh();
        }

        private void UpdateObsStatus(string message)
        {
            TxtStatusBar.Text = message;
        }

        private void ObsPlot_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
        {
            if (sender is not AvaPlot plot) return;

            string tag = plot.Tag?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(tag)) return;

            double[]? data = null;
            string title = tag;

            int dssdLayerIndex = ObsCmbDSSDLayer?.SelectedIndex ?? 0;
            DetectorLayer dssdLayer = dssdLayerIndex switch
            {
                0 => DetectorLayer.L1,
                1 => DetectorLayer.L2,
                2 => DetectorLayer.L6,
                3 => DetectorLayer.L7,
                _ => DetectorLayer.L1
            };

            int bgoLayerIndex = ObsCmbBGOLayer?.SelectedIndex ?? 0;
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
                if (int.TryParse(tag.AsSpan(7), out int stripNum))
                {
                    var layerData = _observationViewModel.GetDSSDLayerData(dssdLayer);
                    if (layerData != null && layerData.StripX.TryGetValue(stripNum, out List<int>? value))
                    {
                        data = [.. value.Select(x => (double)x)];
                        title = $"Strip X{stripNum} ({dssdLayer})";
                        showFit = ObsChkDSSDFit?.IsChecked == true;
                    }
                }
            }
            else if (tag.StartsWith("StripY_"))
            {
                if (int.TryParse(tag.AsSpan(7), out int stripNum))
                {
                    var layerData = _observationViewModel.GetDSSDLayerData(dssdLayer);
                    if (layerData != null && layerData.StripY.TryGetValue(stripNum, out List<int>? value))
                    {
                        data = [.. value.Select(x => (double)x)];
                        title = $"Strip Y{stripNum} ({dssdLayer})";
                        showFit = ObsChkDSSDFit?.IsChecked == true;
                    }
                }
            }
            else if (tag == "BGO_High")
            {
                data = _observationViewModel.GetBGOLayerData(bgoLayer)?.HighGain?.ToArray();
                title = $"BGO High Gain ({bgoLayer})";
                showFit = false;
            }
            else if (tag == "BGO_Low")
            {
                data = _observationViewModel.GetBGOLayerData(bgoLayer)?.LowGain?.ToArray();
                title = $"BGO Low Gain ({bgoLayer})";
                showFit = false;
            }

            if (data == null || data.Length == 0)
            {
                MessageBoxService.Show($"No data available for {title}.", "Info");
                return;
            }

            var detailWindow = new ObservationDetailWindow(_observationViewModel.MathProvider);

            var bg = GetBackgroundColor();
            var fg = GetForegroundColor();
            // Assuming ObservationDetailWindow now accepts ScottPlot.Color or we need to update it
            // Converting to System.Drawing.Color for now if needed, but optimally update ObservationDetailWindow
            // I'll update ObservationDetailWindow to use ScottPlot.Color in next step.
            // For now, let's assume SetColorTheme accepts ScottPlot.Color or update it.
            // Wait, SetColorTheme likely takes Drawing Color? 

            // Refactoring to pass ScottPlot.Color to SetColorTheme if I change it there.
            // But to be safe, I'm modifying ObservationDetailWindow in next step anyway.
            // So I will pass ScottPlot.Color here and fix it there.
            detailWindow.SetColorTheme(bg, bg, fg);

            ScottPlot.Color? barColor;
            if (tag.Contains("BGO")) barColor = ColorHelper.ToScottPlotColor(_observationViewModel.SelectedBGOColor, ScottPlot.Colors.Cyan);
            else barColor = ColorHelper.ToScottPlotColor(_observationViewModel.SelectedDSSDColor, ScottPlot.Colors.Orange);

            double xMin = 0;
            double xMax = DEFAULT_X_MAX;
            if (tag.Contains("BGO"))
            {
                if (double.TryParse(ObsTxtBGOXMax?.Text, out double _bMax)) xMax = _bMax;
            }
            else
            {
                if (double.TryParse(ObsTxtDSSDXMin?.Text, out double _dMin)) xMin = _dMin;
                if (double.TryParse(ObsTxtDSSDXMax?.Text, out double _dMax)) xMax = _dMax;
            }

            int binCount = (int)xMax;
            if (binCount > 8192) binCount = 8192;
            if (binCount < DEFAULT_FIT_WINDOW) binCount = DEFAULT_FIT_WINDOW;

            var axisConfig = new BaselineMode.WPF.Core.Models.Baseline.AnalysisAxisConfig
            {
                XMin = xMin,
                XMax = xMax,
                BinCount = binCount,
                AxisIndex = _observationViewModel.SelectedXAxisIndex,
                Slope = _observationViewModel.EnergyCalibrationSlope,
                Offset = _observationViewModel.EnergyCalibrationIntercept,
                BarWidthMultiplier = _observationViewModel.BarWidthMultiplier
            };

            detailWindow.ShowHistogram(data, title, showFit, barColor, axisConfig);
            detailWindow.Show();
        }

        #endregion
    }
}
