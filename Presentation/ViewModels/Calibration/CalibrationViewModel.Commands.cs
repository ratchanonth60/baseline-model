using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using BaselineMode.WPF.Core.Models.Shared;
using BaselineMode.WPF.Infrastructure.Services;
using CommunityToolkit.Mvvm.Input;
using ExcelDataReader;

namespace BaselineMode.WPF.Presentation.ViewModels.Calibration;

/// <summary>
/// ProcessData and ReadData commands for Calibration mode.
/// </summary>
public partial class CalibrationViewModel
{
    [RelayCommand]
    private async Task ProcessData()
    {
        if (InputFileList == null || InputFileList.Length == 0)
        {
            MessageBoxService.Show("Please select files first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        ProgressValue = 0;
        StatusMessage = "Processing raw data...";
        _cts = new CancellationTokenSource();

        string outputName = string.IsNullOrWhiteSpace(OutputFileName) ? "CalibrationResult" : OutputFileName;
        if (!outputName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) outputName += ".xlsx";

        try
        {
            await Task.Run(async () =>
            {
                var filteredSegments = new List<string>();
                int processedFiles = 0;

                foreach (var fileName in InputFileList)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    string fileContent = await File.ReadAllTextAsync(fileName);
                    string cleanedData = RegexPatterns.Whitespace().Replace(fileContent, "");
                    var matches = RegexPatterns.E225Header().Matches(cleanedData);

                    foreach (Match match in matches)
                    {
                        string segment = match.Value;
                        int segmentLength = segment.Length;
                        for (int i = 0; i < segmentLength; i += AppConstants.SegmentHexLength)
                        {
                            int length = Math.Min(AppConstants.SegmentHexLength, segmentLength - i);
                            filteredSegments.Add(segment.Substring(i, length));
                        }
                    }

                    processedFiles++;
                    double progress = (double)processedFiles / InputFileList.Length * 50;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        ProgressValue = progress;
                        StatusMessage = $"Processing file {processedFiles}/{InputFileList.Length}... ({filteredSegments.Count:N0} segments)";
                    });
                }

                if (filteredSegments.Count > 0 && filteredSegments[^1].Length < AppConstants.SegmentHexLength)
                    filteredSegments.RemoveAt(filteredSegments.Count - 1);

                if (filteredSegments.Count > 0)
                {
                    await Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        StatusMessage = $"Saving {filteredSegments.Count:N0} segments to Excel...";
                        var saveResult = await _fileHelper.SaveToExcelAsync(filteredSegments, outputName, "Source");
                        if (saveResult.IsFailure)
                        {
                            StatusMessage = saveResult.Error;
                            _logger.LogError($"Failed to save calibration Excel: {saveResult.Error}");
                        }
                        else
                            _logger.LogInfo($"Calibration data saved to Excel: {outputName}");
                    });
                }
                else
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => StatusMessage = "No valid segments found.");
                }
            }, _cts.Token);

            StatusMessage = "Processing complete.";
            ProgressValue = 100;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            _logger.LogException(ex, "ProcessData (Calibration)");
            MessageBoxService.Show($"Error processing data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReadData()
    {
        var filesToRead = new List<string>();

        if (ReadMultipleFiles && InputFileList != null && InputFileList.Length > 0)
        {
            filesToRead.AddRange(InputFileList.Where(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) && File.Exists(f)));
            if (filesToRead.Count == 0)
            {
                MessageBoxService.Show("No valid Excel files found in selection.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        else
        {
            string outputName = OutputFileName;
            if (!outputName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) outputName += ".xlsx";
            string? fileName = _fileHelper.FindExcelFile(Path.GetFileNameWithoutExtension(outputName));
            if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName))
            {
                MessageBoxService.Show($"File not found: {outputName}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            filesToRead.Add(fileName);
        }

        IsBusy = true;
        ProgressValue = 0;
        _cts = new CancellationTokenSource();

        try
        {
            StatusMessage = $"Counting rows in {filesToRead.Count} file(s)...";
            int totalRows = 0;

            for (int fileIndex = 0; fileIndex < filesToRead.Count; fileIndex++)
            {
                if (_cts.Token.IsCancellationRequested) return;

                string currentFile = filesToRead[fileIndex];
                await Task.Run(() =>
                {
                    using var stream = new FileStream(currentFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                    using var reader = ExcelReaderFactory.CreateReader(stream);
                    int fileRows = 0;
                    while (reader.Read())
                    {
                        if (_cts.Token.IsCancellationRequested) break;
                        fileRows++;
                    }
                    totalRows += fileRows;
                    Application.Current.Dispatcher.BeginInvoke(() =>
                        StatusMessage = $"Counting... File {fileIndex + 1}/{filesToRead.Count}: {fileRows:N0} rows (Total: {totalRows:N0})");
                }, _cts.Token);
            }

            if (_cts.Token.IsCancellationRequested) return;

            int exactCapacity = totalRows * DataPointsPerRow;
            ResetDataLists(exactCapacity);
            StatusMessage = $"Found {totalRows:N0} total rows from {filesToRead.Count} file(s). Reading data...";
            HeaderCheckStatus = "Checking...";

            int totalRowsRead = 0;
            bool headerCheckPassed = true;

            for (int fileIndex = 0; fileIndex < filesToRead.Count; fileIndex++)
            {
                if (_cts.Token.IsCancellationRequested) break;

                string currentFile = filesToRead[fileIndex];
                await Task.Run(() =>
                {
                    using (var stream = new FileStream(currentFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
                    using (var reader = ExcelReaderFactory.CreateReader(stream))
                    {
                        int rowCount = 0;
                        var lastUpdateTime = DateTime.Now;

                        while (reader.Read())
                        {
                            if (_cts.Token.IsCancellationRequested) break;

                            string hexString = reader.GetValue(0)?.ToString() ?? "";
                            string[] hexData = _dataProcessor.SplitHexData(hexString);

                            if (fileIndex == 0 && rowCount == 0)
                            {
                                bool isHeaderValid = _dataProcessor.ValidateHeader(hexData);
                                Application.Current.Dispatcher.BeginInvoke(() =>
                                    HeaderCheckStatus = isHeaderValid ? "Checksum OK" : "Checksum Mismatch");
                                if (!isHeaderValid)
                                {
                                    headerCheckPassed = false;
                                    return;
                                }
                            }

                            ProcessCalibration(hexData);

                            if ((DateTime.Now - lastUpdateTime).TotalMilliseconds > 300 || rowCount % 1000 == 0)
                            {
                                int currentTotalRows = totalRowsRead + rowCount;
                                double progress = (double)currentTotalRows / totalRows * 100.0;
                                Application.Current.Dispatcher.BeginInvoke(() =>
                                {
                                    ProgressValue = progress;
                                    StatusMessage = $"File {fileIndex + 1}/{filesToRead.Count}: {rowCount:N0} rows | Total: {currentTotalRows:N0}/{totalRows:N0} ({progress:F1}%)";
                                });
                                lastUpdateTime = DateTime.Now;
                            }
                            rowCount++;
                        }
                        totalRowsRead += rowCount;
                    }
                    Application.Current.Dispatcher.BeginInvoke(() =>
                        StatusMessage = $"Completed file {fileIndex + 1}/{filesToRead.Count} (Total: {totalRowsRead:N0}/{totalRows:N0} rows)");
                }, _cts.Token);

                if (!headerCheckPassed) break;
            }

            if (!headerCheckPassed)
            {
                StatusMessage = "Stopped: Checksum Mismatch";
                MessageBoxService.Show("Checksum Mismatch! Processing Stopped.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (_cts.Token.IsCancellationRequested)
            {
                StatusMessage = "Stopped by user.";
                return;
            }

            StatusMessage = $"Read {totalRowsRead:N0} rows from {filesToRead.Count} file(s). Updating plots...";
            await UpdatePlotsAsync();
            StatusMessage = $"Complete! {totalRowsRead:N0} total rows from {filesToRead.Count} file(s) processed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            _logger.LogException(ex, "ReadData (Calibration)");
            MessageBoxService.Show($"Error reading data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 100;
            ReadMultipleFiles = false;
        }
    }
}
