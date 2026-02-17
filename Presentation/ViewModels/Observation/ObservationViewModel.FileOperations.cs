using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BaselineMode.WPF.Core.Models.Shared;
using BaselineMode.WPF.Infrastructure.Services;
using CommunityToolkit.Mvvm.Input;

namespace BaselineMode.WPF.Presentation.ViewModels.Observation
{
    /// <summary>
    /// File selection, combining, Excel export and segment filtering for Observation mode.
    /// </summary>
    public partial class ObservationViewModel
    {
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
                return;
            }

            try
            {
                var combineResult = await _fileHelper.CombineFilesAsync(fileNames, CombinedOutputFileName);
                if (combineResult.IsFailure)
                {
                    StatusMessage = combineResult.Error;
                    _logger.LogError($"Error combining files: {combineResult.Error}");
                    return;
                }

                string combinedPath = combineResult.Value;
                StatusMessage = $"{fileNames.Length} file(s) selected.";
                OutputFileName = Path.GetFileNameWithoutExtension(combinedPath);
                StatusMessage = $"Files combined successfully into {combinedPath}";
                InputFileList = [combinedPath];
                _logger.LogInfo($"Files combined for observation: {combinedPath}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error combining files: {ex.Message}";
                _logger.LogException(ex, "LoadFiles (Observation)");
            }
        }

        [RelayCommand]
        private async Task ConvertFilesToExcel()
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

            if (filteredSegments.Count == 0)
            {
                StatusMessage = "No valid segments found in the selected files.";
                return;
            }

            string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
            string fullPath = Path.Combine(OutputDirectoryPath, dateStr);
            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);

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
            _logger.LogInfo($"Observation files converted to Excel: {finalPath}");
        }

        public async Task ExportToPathAsync(string exportFilePath)
        {
            if (InputFileList == null || InputFileList.Length == 0)
            {
                StatusMessage = "No files selected for export.";
                return;
            }

            var filteredSegments = await FilterSegmentsAsync();
            if (filteredSegments.Count == 0)
            {
                StatusMessage = "No valid segments found in the selected files.";
                return;
            }

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

            if (filteredSegments.Count > 0 && filteredSegments[^1].Length < AppConstants.PacketHexLength)
                filteredSegments.RemoveAt(filteredSegments.Count - 1);

            return filteredSegments;
        }
    }
}
