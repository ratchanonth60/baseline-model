using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BaselineMode.WPF.Services.Observation;
using BaselineMode.WPF.Interfaces.Observation;
using BaselineMode.WPF.Models.Observation;

namespace BaselineMode.WPF.ViewModels.Observation
{
    public partial class ObservationMainViewModel : ObservableObject
    {
        private readonly IObservationDataProcessor _dataProcessor;
        private readonly ObservationExcelHelper _excelHelper;
        private readonly IObservationFittingService _fittingService;

        // Services exposed as Interfaces
        public IObservationDataProcessor DataProcessor => _dataProcessor;
        public ObservationExcelHelper ExcelHelper => _excelHelper;
        public IObservationFittingService FittingService => _fittingService;
        public string CombinedOutputFileName { get; set; } = "CombinedData.xlsx";

        public ObservationMainViewModel()
        {
            _dataProcessor = new ObservationDataProcessor();
            _excelHelper = new ObservationExcelHelper();
            _fittingService = new ObservationFittingService();
        }

        [ObservableProperty]
        private string[]? _inputFileList;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private int _progressValue;

        [ObservableProperty]
        private string? _outputFileName;

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
        private void ProcessData()
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
