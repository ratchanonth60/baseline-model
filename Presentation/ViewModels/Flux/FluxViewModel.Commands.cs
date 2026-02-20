using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BaselineMode.WPF.Core.Models.Shared;
using BaselineMode.WPF.Infrastructure.Services;
using ExcelDataReader;

namespace BaselineMode.WPF.Presentation.ViewModels.Flux
{
    /// <summary>
    /// Processing commands: ProcessData, ReadData, HeaderCheck.
    /// </summary>
    public partial class FluxViewModel
    {
        private async Task ProcessData()
        {
            if (InputFileList == null || InputFileList.Length == 0)
            {
                StatusMessage = "No files selected for processing.";
                return;
            }

            IsBusy = true;
            ProgressValue = 0;
            StatusMessage = "Processing raw data...";
            _cts = new CancellationTokenSource();

            string outputName = string.IsNullOrWhiteSpace(OutputFileName) ? "FluxResult" : OutputFileName;
            if (!outputName.EndsWith(".xlsx")) outputName += ".xlsx";

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
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            ProgressValue = progress;
                            StatusMessage = $"Processing file {processedFiles}/{InputFileList.Length}... ({filteredSegments.Count:N0} segments)";
                        });
                    }

                    if (filteredSegments.Count > 0 && filteredSegments[^1].Length < AppConstants.SegmentHexLength)
                        filteredSegments.RemoveAt(filteredSegments.Count - 1);

                    if (filteredSegments.Count > 0)
                    {
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            StatusMessage = $"Saving {filteredSegments.Count:N0} segments to Excel...";
                            var saveResult = await _fileHelper.SaveToExcelAsync(filteredSegments, outputName, "Source");
                            if (saveResult.IsFailure)
                            {
                                StatusMessage = saveResult.Error;
                                _logger.LogError($"Failed to save flux Excel: {saveResult.Error}");
                            }
                            else
                                _logger.LogInfo($"Flux data saved to Excel: {outputName}");
                        });
                    }
                    else
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                            StatusMessage = "No valid segments found.");
                    }
                }, _cts.Token);

                StatusMessage = "Processing complete.";
                ProgressValue = 100;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                _logger.LogException(ex, "ProcessData (Flux)");
                await _dialogService.ShowMessageAsync($"Error processing data: {ex.Message}", "Error",
                    MsgBoxButton.OK, MsgBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ReadData()
        {
            string outputName = OutputFileName;
            if (!outputName.EndsWith(".xlsx")) outputName += ".xlsx";

            string? fileName = _fileHelper.FindExcelFile(Path.GetFileNameWithoutExtension(outputName));
            if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName))
            {
                await _dialogService.ShowMessageAsync($"File not found: {outputName}", "Error",
                    MsgBoxButton.OK, MsgBoxImage.Error);
                return;
            }

            IsBusy = true;
            ProgressValue = 0;
            _cts = new CancellationTokenSource();
            ResetDataLists();

            try
            {
                int totalSteps = 0;
                DateTime startTime = DateTime.MinValue;
                DateTime stopTime = DateTime.MinValue;
                string? lastHexString = null;

                await Task.Run(() =>
                {
                    using var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                    using var reader = ExcelReaderFactory.CreateReader(stream);
                    var result = reader.AsDataSet();
                    var rawData = result.Tables[0];
                    totalSteps = rawData.Rows.Count;

                    Dispatcher.UIThread.InvokeAsync(() => DataCount = totalSteps);

                    bool isFirst = true;
                    var lastUpdateTime = DateTime.Now;

                    for (int data = 0; data < totalSteps && !(_cts?.Token.IsCancellationRequested ?? false); data++)
                    {
                        string hexString = rawData.Rows[data][0].ToString() ?? "";

                        if (isFirst)
                        {
                            startTime = GetDateTimeFromHexData(hexString);
                            Dispatcher.UIThread.InvokeAsync(() =>
                                StartTimeText = startTime.ToString("yyyy-MMM-dd HH:mm:ss.fff", new CultureInfo("en-US")));
                            isFirst = false;
                        }

                        ProcessFluxObservation(hexString);
                        lastHexString = hexString;

                        if ((DateTime.Now - lastUpdateTime).TotalMilliseconds > 300 || data % 500 == 0)
                        {
                            int currentData = data + 1;
                            double progress = (double)currentData / totalSteps * 100.0;
                            Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                ProgressValue = progress;
                                StatusMessage = $"Processing... {progress:F1}% ({currentData:N0}/{totalSteps:N0})";
                            });
                            lastUpdateTime = DateTime.Now;
                        }
                    }

                    if (totalSteps > 0)
                    {
                        string lastHex = rawData.Rows[totalSteps - 1][0].ToString() ?? "";
                        stopTime = GetDateTimeFromHexData(lastHex);
                    }
                }, _cts?.Token ?? CancellationToken.None);

                if (_cts?.Token.IsCancellationRequested ?? false)
                {
                    StatusMessage = "Stopped by user.";
                    return;
                }

                StopTimeText = stopTime.ToString("yyyy-MMM-dd HH:mm:ss.fff", new CultureInfo("en-US"));
                if (startTime != DateTime.MinValue && stopTime != DateTime.MinValue)
                {
                    _duration = stopTime - startTime;
                    DurationText = $"{_duration.TotalSeconds:F3} seconds";
                }

                if (lastHexString != null)
                    ProcessHeader(lastHexString);

                StatusMessage = "Process Complete";
                ProgressValue = 100;
                CalculateAndPlotFlux();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                await _dialogService.ShowMessageAsync($"Error reading data: {ex.Message}", "Error",
                    MsgBoxButton.OK, MsgBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task HeaderCheck()
        {
            string outputName = OutputFileName;
            if (!outputName.EndsWith(".xlsx")) outputName += ".xlsx";

            string? fileName = _fileHelper.FindExcelFile(Path.GetFileNameWithoutExtension(outputName));
            if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName))
            {
                await _dialogService.ShowMessageAsync("File not found!", "Error", MsgBoxButton.OK, MsgBoxImage.Warning);
                return;
            }

            IsBusy = true;
            HeaderCheckStatus = "Checking...";

            try
            {
                await Task.Run(() =>
                {
                    using var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                    using var reader = ExcelReaderFactory.CreateReader(stream);
                    var result = reader.AsDataSet();
                    var rawData = result.Tables[0];
                    int totalRows = rawData.Rows.Count;

                    for (int i = 0; i < totalRows; i++)
                    {
                        string hexString = rawData.Rows[i][0].ToString() ?? "";
                        if (!hexString.StartsWith(AppConstants.HeaderStart))
                        {
                            Dispatcher.UIThread.InvokeAsync(() =>
                                HeaderCheckStatus = $"Header is INCORRECT! at data row no. {i + 1}");
                            return;
                        }
                    }

                    Dispatcher.UIThread.InvokeAsync(() =>
                        HeaderCheckStatus = "Header is correct!");
                });
            }
            catch (Exception ex)
            {
                await _dialogService.ShowMessageAsync($"Error reading file: {ex.Message}", "File Read Error",
                    MsgBoxButton.OK, MsgBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
