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

namespace BaselineMode.WPF.Presentation.ViewModels.Observation
{
    public partial class ObservationMainViewModel : ObservableObject
    {
        private readonly IObservationDataProcessor _dataProcessor;
        private readonly IObservationExcelHelper _excelHelper;
        private readonly IFittingService _fittingService;
        private readonly IFileService _fileService;
        private readonly IFileHelper _fileHelper;

        // Services exposed as Interfaces
        public IObservationDataProcessor DataProcessor => _dataProcessor;
        public IObservationExcelHelper ExcelHelper => _excelHelper;
        public IFittingService FittingService => _fittingService;
        public IFileHelper FileHelper => _fileHelper;
        public string CombinedOutputFileName { get; set; } = "CombinedData.xlsx";

        public ObservationMainViewModel(IObservationDataProcessor dataProcessor, IObservationExcelHelper excelHelper, IFittingService fittingService, IFileService fileService, IFileHelper fileHelper)
        {
            _dataProcessor = dataProcessor;
            _excelHelper = excelHelper;
            _fittingService = fittingService;
            _fileService = fileService;
            _fileHelper = fileHelper;
        }

        [ObservableProperty]
        private string[]? _inputFileList;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private int _progressValue;

        [ObservableProperty]
        private string? _outputFileName;

        [ObservableProperty]
        private bool _useCustomSavePath = false;

        [ObservableProperty]
        private string _dataCountStr = "-";

        [ObservableProperty]
        private string _particleCountStr = "-";

        // Graph Settings
        [ObservableProperty]
        private System.Windows.Media.Color _selectedGraphBackground = System.Windows.Media.Colors.Black;

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
        private string _startTimeStr = "-";

        [ObservableProperty]
        private string _stopTimeStr = "-";

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
            return _dataProcessor.DSSDData.ContainsKey(layer) ? _dataProcessor.DSSDData[layer] : null;
        }

        public BGOData? GetBGOLayerData(BGOLayer layer)
        {
            return _dataProcessor.BGOData.ContainsKey(layer) ? _dataProcessor.BGOData[layer] : null;
        }

        [RelayCommand]
        private void Reset()
        {
            _dataProcessor.ClearData();
            InputFileList = null;
            StatusMessage = "Ready";
            ProgressValue = 0;
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

                var filteredSegments = new List<string>();

                await Task.Run(() =>
                {
                    foreach (var fileName in InputFileList)
                    {
                        var fileContent = File.ReadAllText(fileName);
                        var cleanedData = Regex.Replace(fileContent, @"\s+", "");
                        var matches = Regex.Matches(cleanedData, $@"{ObservationConstants.HeaderStart}[0-9A-F]+");

                        foreach (Match match in matches)
                        {
                            var segment = match.Value;
                            int segmentLength = segment.Length;

                            for (int i = 0; i < segmentLength; i += ObservationConstants.PacketHexLength)
                            {
                                var chunk = segment.Substring(i, Math.Min(ObservationConstants.PacketHexLength, segmentLength - i));
                                filteredSegments.Add(chunk);
                            }
                        }
                    }
                });

                if (filteredSegments.Count > 0 && filteredSegments.Last().Length < ObservationConstants.PacketHexLength)
                {
                    filteredSegments.RemoveAt(filteredSegments.Count - 1);
                }

                if (filteredSegments.Count > 0)
                {
                    string? savedPath;
                    if (UseCustomSavePath)
                    {
                        // Show dialog to let user choose save location
                        savedPath = _fileHelper.SaveToExcelWithDialog(filteredSegments, outputExcel);
                        if (savedPath == null)
                        {
                            StatusMessage = "Save cancelled by user.";
                            return;
                        }
                    }
                    else
                    {
                        // Save to default location
                        _fileHelper.SaveToExcel(filteredSegments, outputExcel);
                        savedPath = Path.Combine(_fileHelper.GetOutputFolder("Source"), outputExcel);
                    }
                    StatusMessage = $"Successfully processed {InputFileList.Length} file(s). Saved to {savedPath}";
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
                // Log or Show MessageBox via Service
            }
        }

        [ObservableProperty]
        private bool _isBusy;

        public event EventHandler? RequestPlotUpdate;

        public void LoadFiles(string[] fileNames)
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
                    string combinedOutputFilePath = _fileHelper.CombineFiles(fileNames, CombinedOutputFileName);

                    StatusMessage = $"{fileNames.Length} file(s) selected.";
                    OutputFileName = Path.GetFileNameWithoutExtension(combinedOutputFilePath);
                    StatusMessage = $"Files combined successfully into {combinedOutputFilePath}";
                    InputFileList = new string[] { combinedOutputFilePath };
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error combining files: {ex.Message}";
                }
            }
        }
    }
}