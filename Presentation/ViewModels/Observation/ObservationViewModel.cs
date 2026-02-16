using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using BaselineMode.WPF.Infrastructure.Services;
using BaselineMode.WPF.Infrastructure.Services.Observation;
using BaselineMode.WPF.Core.Interfaces;
using BaselineMode.WPF.Core.Interfaces.Observation;
using BaselineMode.WPF.Core.Models.Observation;
using BaselineMode.WPF.Core.Models;
using BaselineMode.WPF.Presentation.ViewModels;
using BaselineMode.WPF.Presentation.ViewModels.Shared;
using ExcelDataReader;
using BaselineMode.WPF.Core.Models.Shared;

namespace BaselineMode.WPF.Presentation.ViewModels.Observation
{
    public partial class ObservationViewModel(IObservationDataProcessor dataProcessor, IObservationExcelHelper excelHelper, IFittingService fittingService, IMathService mathService, IFileService fileService, IFileHelper fileHelper, ILoggerService logger) : SharedViewModelBase
    {
        private readonly IObservationDataProcessor _dataProcessor = dataProcessor;
        private readonly IObservationExcelHelper _excelHelper = excelHelper;
        private readonly IFittingService _fittingService = fittingService;
        private readonly IMathService _mathService = mathService;
        private readonly IFileService _fileService = fileService;
        private readonly IFileHelper _fileHelper = fileHelper;
        private readonly ILoggerService _logger = logger;

        // Services exposed as Interfaces
        public IObservationDataProcessor DataProcessor => _dataProcessor;
        public IObservationExcelHelper ExcelHelper => _excelHelper;
        public IFittingService FittingService => _fittingService;
        public IMathService MathProvider => _mathService;
        public IFileHelper FileHelper => _fileHelper;
        public string CombinedOutputFileName { get; set; } = "CombinedData.xlsx";

        [ObservableProperty]
        private string? _outputFileName;

        [ObservableProperty]
        private string _dataCountStr = "-";

        [ObservableProperty]
        private string _particleCountStr = "-";

        [ObservableProperty]
        private string? _lastSavedFilePath;

        [ObservableProperty]
        private string _outputDirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BaselineModeOutputs");

        [RelayCommand]
        private void BrowseOutputDirectory()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Output Root Folder"
            };
            if (dialog.ShowDialog() == true)
            {
                OutputDirectoryPath = dialog.FolderName;
            }
        }

        // Graph Settings
        [ObservableProperty]
        private System.Windows.Media.Color _selectedGraphBackground = System.Windows.Media.Colors.Gray;

        [ObservableProperty]
        private System.Windows.Media.Color _selectedDSSDColor = System.Windows.Media.Colors.Orange;

        [ObservableProperty]
        private System.Windows.Media.Color _selectedBGOColor = System.Windows.Media.Colors.Cyan;

        partial void OnSelectedGraphBackgroundChanged(System.Windows.Media.Color value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);
        partial void OnSelectedDSSDColorChanged(System.Windows.Media.Color value)
        {
            RequestPlotUpdate?.Invoke(this, EventArgs.Empty);
        }
        partial void OnSelectedBGOColorChanged(System.Windows.Media.Color value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);

        [ObservableProperty]
        private double _barWidthMultiplier = 1.0;
        partial void OnBarWidthMultiplierChanged(double value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);

        [ObservableProperty]
        private string _stopTimeStr = "-";

        // Fit Selection Flags (DSSD)
        [ObservableProperty]
        private bool _showGaussianFitDSSD = true;

        [ObservableProperty]
        private bool _showLorentzianFitDSSD = false;

        [ObservableProperty]
        private bool _showHemgFitDSSD = false;

        // Fit Selection Flags (BGO)
        [ObservableProperty]
        private bool _showGaussianFitBGO = true;

        [ObservableProperty]
        private bool _showLorentzianFitBGO = false;

        [ObservableProperty]
        private bool _showHemgFitBGO = false;

        partial void OnShowGaussianFitDSSDChanged(bool value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);
        partial void OnShowLorentzianFitDSSDChanged(bool value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);
        partial void OnShowHemgFitDSSDChanged(bool value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);
        partial void OnShowGaussianFitBGOChanged(bool value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);
        partial void OnShowLorentzianFitBGOChanged(bool value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);
        partial void OnShowHemgFitBGOChanged(bool value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);

        public Dictionary<string, int[]>? HistogramData { get; private set; }

        [RelayCommand]
        private void SelectFiles()
        {
            var files = _fileService.OpenFileDialog("Text files (*.txt)|*.txt|All files (*.*)|*.*", true);
            if (files != null && files.Length > 0)
            {
                LoadFiles(files);
            }
        }

        // --- Data Access Helpers for View ---
        public LayerData? GetDSSDLayerData(DetectorLayer layer)
        {
            return _dataProcessor.DSSDData.TryGetValue(layer, out LayerData? value) ? value : null;
        }

        public BGOData? GetBGOLayerData(BGOLayer layer)
        {
            return _dataProcessor.BGOData.TryGetValue(layer, out BGOData? value) ? value : null;
        }

        [RelayCommand]
        public override void Reset()
        {
            base.Reset();
            _dataProcessor.ClearData();
            OutputFileName = string.Empty;
        }

        [RelayCommand]
        private async Task ConvertFilesToExcel()
        {
            try
            {
                if (InputFileList == null || InputFileList.Length == 0)
                {
                    StatusMessage = "No files selected for processing.";
                    return;
                }

                var finalOutputName = OutputFileName;
                if (string.IsNullOrWhiteSpace(finalOutputName))
                {
                    StatusMessage = "Please provide a valid output Excel file name.";
                    return;
                }
                var outputExcel = finalOutputName.Trim() + ".xlsx";

                var filteredSegments = await FilterSegmentsAsync();

                if (filteredSegments.Count > 0)
                {
                    // Save to OutputDirectoryPath + Daily Folder
                    string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
                    string fullPath = Path.Combine(OutputDirectoryPath, dateStr);
                    if (!Directory.Exists(fullPath))
                    {
                        Directory.CreateDirectory(fullPath);
                    }

                    string finalPath = Path.Combine(fullPath, outputExcel);
                    var result = await _fileHelper.SaveToExcelAsync(filteredSegments, finalPath);
                    if (result.IsFailure)
                    {
                        StatusMessage = result.Error;
                        _logger.LogError($"Failed to save observation Excel: {result.Error}");
                        return;
                    }

                    LastSavedFilePath = finalPath;
                    StatusMessage = $"Successfully processed {InputFileList.Length} file(s). Saved to {finalPath}";
                    _logger.LogInfo($"Observation files converted to Excel successfully: {finalPath}");
                }
                else
                {
                    StatusMessage = "No valid segments found in the selected files.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Export processed data to a user-specified file path (via SaveFileDialog).
        /// </summary>
        public async Task ExportToPathAsync(string exportFilePath)
        {
            if (InputFileList == null || InputFileList.Length == 0)
            {
                StatusMessage = "No files selected for export.";
                return;
            }

            var filteredSegments = await FilterSegmentsAsync();

            if (filteredSegments.Count > 0)
            {
                var result = await _fileHelper.SaveToExcelAsync(filteredSegments, exportFilePath);
                if (result.IsFailure)
                {
                    StatusMessage = result.Error;
                    _logger.LogError($"Export failed: {result.Error}");
                    return;
                }

                LastSavedFilePath = exportFilePath;
                StatusMessage = $"Exported {InputFileList.Length} file(s) to {exportFilePath}";
                _logger.LogInfo($"Observation data exported to: {exportFilePath}");
            }
            else
            {
                StatusMessage = "No valid segments found in the selected files.";
            }
        }

        /// <summary>
        /// Shared logic: read input files, filter hex segments, and return them.
        /// </summary>
        private async Task<List<string>> FilterSegmentsAsync()
        {
            var filteredSegments = new List<string>();

            foreach (var fileName in InputFileList!)
            {
                var fileContent = await File.ReadAllTextAsync(fileName);
                var cleanedData = RegexPatterns.Whitespace().Replace(fileContent, "");
                var matches = RegexPatterns.E225Header().Matches(cleanedData);

                foreach (Match match in matches)
                {
                    var segment = match.Value;
                    int segmentLength = segment.Length;

                    for (int i = 0; i < segmentLength; i += AppConstants.PacketHexLength)
                    {
                        var chunk = segment.Substring(i, Math.Min(AppConstants.PacketHexLength, segmentLength - i));
                        filteredSegments.Add(chunk);
                    }
                }
            }

            if (filteredSegments.Count > 0 && filteredSegments.Last().Length < AppConstants.PacketHexLength)
            {
                filteredSegments.RemoveAt(filteredSegments.Count - 1);
            }

            return filteredSegments;
        }

        [RelayCommand]
        private async Task AnalyzeFiles()
        {
            if (InputFileList == null || InputFileList.Length == 0)
            {
                StatusMessage = "Please select files first.";
                return;
            }

            try
            {
                StatusMessage = "Processing...";
                ProgressValue = 0; // Indeterminate
                IsBusy = true;

                var result = await _dataProcessor.ProcessFilesAsync(InputFileList);
                HistogramData = result;

                StatusMessage = "Processing complete!";
                IsBusy = false;
                ProgressValue = 100;

                // Notify UI to update plots (via property or event)
                // In a real usage, we might expose the result as a property
                // For now, we assume the View might pull from DataProcessor or we expose specific data

                // TODO: Update Plot Objects or trigger event
                RequestPlotUpdate?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                StatusMessage = "Error!";
                IsBusy = false;
                _logger.LogException(ex, "Error in AnalyzeFiles (Observation)");
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }

        public event EventHandler? RequestPlotUpdate;

        public async void LoadFiles(string[] fileNames)
        {
            if (fileNames == null || fileNames.Length == 0)
            {
                StatusMessage = "No files selected.";
                return;
            }

            InputFileList = fileNames;

            if (fileNames.Length == 1)
            {
                StatusMessage = "1 file selected.";
                OutputFileName = Path.GetFileNameWithoutExtension(fileNames[0]);
            }
            else
            {
                try
                {
                    var combineResult = await _fileHelper.CombineFilesAsync(fileNames, CombinedOutputFileName);
                    if (combineResult.IsFailure)
                    {
                        StatusMessage = combineResult.Error;
                        _logger.LogError($"Error combining files: {combineResult.Error}");
                        return;
                    }

                    string combinedOutputFilePath = combineResult.Value;
                    StatusMessage = $"{fileNames.Length} file(s) selected.";
                    OutputFileName = Path.GetFileNameWithoutExtension(combinedOutputFilePath);
                    StatusMessage = $"Files combined successfully into {combinedOutputFilePath}";
                    InputFileList = [combinedOutputFilePath];
                    _logger.LogInfo($"Files combined successfully for observation: {combinedOutputFilePath}");
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error combining files: {ex.Message}";
                    _logger.LogException(ex, "Error combining files in LoadFiles (Observation)");
                }
            }
        }

        public async Task ProcessExcelDataAsync(string fileName, IProgress<ObservationProcessReport> progress, System.Threading.CancellationToken token)
        {
            if (!File.Exists(fileName))
            {
                throw new FileNotFoundException("The specified file does not exist.", fileName);
            }

            await Task.Run(() =>
            {
                using var stream = File.Open(fileName, FileMode.Open, FileAccess.Read);
                using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream);

                var result = reader.AsDataSet();
                var rawData = result.Tables[0];
                int totalSteps = rawData.Rows.Count;
                int dataIndex = 1;
                bool isFirstData = true;

                while (dataIndex <= totalSteps && !token.IsCancellationRequested)
                {
                    string? hexString = rawData.Rows[dataIndex - 1][0].ToString();
                    if (hexString == null)
                    {
                        dataIndex++;
                        continue;
                    }

                    var hexData = _dataProcessor.SplitHexData(hexString);
                    _dataProcessor.ProcessParticles(hexData);

                    // Report progress
                    if (progress != null)
                    {
                        var report = new ObservationProcessReport
                        {
                            CurrentStep = dataIndex,
                            TotalSteps = totalSteps,
                            Message = $"Processing... {Math.Round((double)dataIndex / totalSteps * 100)}%",
                            IsComplete = false,
                            LastHexData = hexData // Pass for header/timestamp checks if needed
                        };

                        if (isFirstData)
                        {
                            report.CurrentTime = _dataProcessor.GetDateTimeFromHexData(hexData);
                            isFirstData = false;
                        }

                        progress.Report(report);
                    }

                    dataIndex++;
                }

                // Final report
                if (progress != null && totalSteps > 0)
                {
                    string? lastHex = rawData.Rows[totalSteps - 1][0].ToString();
                    string[]? lastHexData = null;
                    DateTime? lastTime = null;

                    if (lastHex != null)
                    {
                        lastHexData = _dataProcessor.SplitHexData(lastHex);
                        lastTime = _dataProcessor.GetDateTimeFromHexData(lastHexData);
                    }

                    progress.Report(new ObservationProcessReport
                    {
                        CurrentStep = totalSteps,
                        TotalSteps = totalSteps,
                        Message = "Process Complete",
                        IsComplete = true,
                        CurrentTime = lastTime,
                        LastHexData = lastHexData
                    });
                }
            }, token);
        }

        public static async Task<(bool IsValid, string Message, int ErrorRow)> CheckHeaderAsync(string fileName)
        {
            if (!File.Exists(fileName))
            {
                return (false, "File not found.", 0);
            }

            return await Task.Run(() =>
            {
                try
                {
                    using var stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream);

                    var result = reader.AsDataSet();
                    var rawData = result.Tables[0];
                    int totalSteps = rawData.Rows.Count;

                    for (int i = 1; i <= totalSteps; i++)
                    {
                        string? hexString = rawData.Rows[i - 1][0].ToString();
                        if (hexString == null || !hexString.StartsWith(AppConstants.HeaderStart))
                        {
                            return (false, $"Header INCORRECT at row {i}", i);
                        }
                    }

                    return (true, "Header is correct!", 0);
                }
                catch (Exception ex)
                {
                    return (false, $"Error: {ex.Message}", 0);
                }
            });
        }
    }
}