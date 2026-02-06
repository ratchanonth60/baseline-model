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

        // Services exposed as Interfaces
        public IObservationDataProcessor DataProcessor => _dataProcessor;
        public IObservationExcelHelper ExcelHelper => _excelHelper;
        public IFittingService FittingService => _fittingService;
        public string CombinedOutputFileName { get; set; } = "CombinedData.xlsx";

        public ObservationMainViewModel(IObservationDataProcessor dataProcessor, IObservationExcelHelper excelHelper, IFittingService fittingService, IFileService fileService)
        {
            _dataProcessor = dataProcessor;
            _excelHelper = excelHelper;
            _fittingService = fittingService;
            _fileService = fileService;
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
        private string _dataCountStr = "-";

        [ObservableProperty]
        private string _particleCountStr = "-";

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
                    _excelHelper.SaveToExcel(filteredSegments, outputExcel);
                    StatusMessage = $"Successfully processed {InputFileList.Length} file(s). Saved to {outputExcel}";
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
                    string storageDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CombinedTextFiles");
                    Directory.CreateDirectory(storageDir);

                    string combinedOutputFilePath = Path.Combine(storageDir, CombinedOutputFileName);

                    StatusMessage = $"{fileNames.Length} file(s) selected.";
                    OutputFileName = Path.GetFileNameWithoutExtension(combinedOutputFilePath);

                    var allContents = new List<string>();
                    foreach (var file in fileNames)
                    {
                        allContents.Add(File.ReadAllText(file));
                    }

                    if (allContents.Count > 0)
                    {
                        File.WriteAllText(combinedOutputFilePath, string.Join("\n", allContents));
                        StatusMessage = $"Files combined successfully into {combinedOutputFilePath}";
                        InputFileList = new string[] { combinedOutputFilePath };
                    }
                    else
                    {
                        StatusMessage = "No valid files could be read.";
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error combining files: {ex.Message}";
                }
            }
        }
    }
}
