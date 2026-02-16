using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BaselineMode.WPF.Core.Interfaces;
using BaselineMode.WPF.Core.Interfaces.Observation;
using BaselineMode.WPF.Core.Models;
using BaselineMode.WPF.Core.Helpers;
using BaselineMode.WPF.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExcelDataReader;
using Microsoft.Win32;

using BaselineMode.WPF.Core.Models.Flux;
using BaselineMode.WPF.Presentation.ViewModels.Shared;
using BaselineMode.WPF.Core.Models.Shared;
using System.Reflection;
using System.Windows.Media;

namespace BaselineMode.WPF.Presentation.ViewModels.Flux
{
    public partial class FluxViewModel : SharedViewModelBase
    {
        private readonly IFileHelper _fileHelper;
        private readonly IObservationDataProcessor _dataProcessor;
        private readonly ILoggerService _logger;

        private const int LAYER_COUNT = 7;
        private const double DETECTOR_AREA_M2 = 32 * 32 * 1e-6; // 32mm × 32mm → m^2

        public IAsyncRelayCommand SelectFilesCommand { get; }
        public IAsyncRelayCommand ProcessDataCommand { get; }
        public IAsyncRelayCommand ReadDataCommand { get; }
        public IAsyncRelayCommand HeaderCheckCommand { get; }
        public IRelayCommand StopCommand { get; }
        public IRelayCommand ResetCommand { get; }

        public FluxViewModel(IFileHelper fileHelper, IObservationDataProcessor dataProcessor, ILoggerService logger)
        {
            _fileHelper = fileHelper;
            _dataProcessor = dataProcessor;
            _logger = logger;

            SelectFilesCommand = new AsyncRelayCommand(SelectFiles);
            ProcessDataCommand = new AsyncRelayCommand(ProcessData);
            ReadDataCommand = new AsyncRelayCommand(ReadData);
            HeaderCheckCommand = new AsyncRelayCommand(HeaderCheck);
            StopCommand = new RelayCommand(Stop);
            ResetCommand = new RelayCommand(Reset);

            Layers = [];
            for (int i = 0; i < LAYER_COUNT; i++)
            {
                Layers.Add(new FluxLayerViewModel
                {
                    LayerName = $"L{i + 1}",
                    LayerIndex = i
                });
            }
        }

        // ── Properties ──────────────────────────────────────────────

        [ObservableProperty]
        private string _inputFilesInfo = "No files selected";

        private List<string> _selectedFiles = [];

        private string GetDailyOutputDirectory()
        {
            // Use FileHelper to get a daily output folder, or just the documents folder
            return _fileHelper.GetOutputFolder("DailyFluxOutput");
        }

        [ObservableProperty]
        private string _outputFileName = "FluxResult";

        [ObservableProperty]
        private string _headerCheckStatus = "";

        [ObservableProperty]
        private string _startTimeText = "-";

        [ObservableProperty]
        private string _stopTimeText = "-";

        [ObservableProperty]
        private string _durationText = "-";

        [ObservableProperty]
        private int _dataCount;

        [ObservableProperty]
        private int _delayTime = 50;

        [ObservableProperty]
        private int _threshold = 50;

        [ObservableProperty]
        private double _timeRangeMin = 0;

        [ObservableProperty]
        private double _timeRangeMax = 1000;

        [ObservableProperty]
        private bool _isLogScale;

        [ObservableProperty]
        private string _headerInfo = "";

        // Graph color properties
        [ObservableProperty]
        private System.Windows.Media.Color _graphFigureColor = System.Windows.Media.Color.FromRgb(30, 30, 30);

        [ObservableProperty]
        private System.Windows.Media.Color _graphDataColor = System.Windows.Media.Color.FromRgb(37, 37, 38);

        [ObservableProperty]
        private System.Windows.Media.Color _graphSeriesColor = System.Windows.Media.Colors.Cyan;

        [ObservableProperty]
        private System.Windows.Media.Color _graphTextColor = System.Windows.Media.Colors.White;

        partial void OnGraphFigureColorChanged(System.Windows.Media.Color value) => UpdateAllPlots();
        partial void OnGraphDataColorChanged(System.Windows.Media.Color value) => UpdateAllPlots();
        partial void OnGraphSeriesColorChanged(System.Windows.Media.Color value) => UpdateAllPlots();
        partial void OnGraphTextColorChanged(System.Windows.Media.Color value) => UpdateAllPlots();
        partial void OnIsLogScaleChanged(bool value) => UpdateAllPlots();
        partial void OnTimeRangeMinChanged(double value) => UpdateAllPlots();
        partial void OnTimeRangeMaxChanged(double value) => UpdateAllPlots();

        [ObservableProperty]
        private double _barWidthMultiplier = 1.0;
        partial void OnBarWidthMultiplierChanged(double value) => UpdateAllPlots();

        // ── Data Storage ────────────────────────────────────────────

        private readonly List<double> _secondsPartList = [];
        private readonly List<double>[] _particleCountingLists = [.. Enumerable.Range(0, LAYER_COUNT).Select(_ => new List<double>())];
        private readonly List<double[]> _particleLayerList = [];
        private readonly List<double[]> _particleOffsetTimeList = [];
        private readonly List<FluxDataResult> _allResults = [];

        private CancellationTokenSource? _cts;
        private TimeSpan _duration = TimeSpan.Zero;


        public ObservableCollection<FluxLayerViewModel> Layers { get; }

        // ── Commands ────────────────────────────────────────────────

        private async Task SelectFiles()
        {
            Reset();

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true) return;

            var files = dialog.FileNames.ToList();
            _selectedFiles = files;
            InputFileList = [.. _selectedFiles];

            // กรณีไฟล์เดียว: ไม่ต้องประมวลผลอะไรซับซ้อน
            if (files.Count == 1)
            {
                InputFilesInfo = "1 file selected.";
                OutputFileName = Path.GetFileNameWithoutExtension(files[0]) + ".xlsx";
                StatusMessage = "Files loaded. Ready to process.";
                return;
            }

            // กรณีหลายไฟล์: ใช้ Stream เพื่อรวมไฟล์ (ประหยัด RAM)
            await CombineFilesAsync(files);
        }

        private async Task CombineFilesAsync(List<string> files)
        {
            IsBusy = true;
            StatusMessage = $"Combining {files.Count} files...";
            string combinedFileName = "multiple_file_output.txt";

            try
            {
                var result = await _fileHelper.CombineFilesAsync(files.ToArray(), combinedFileName);
                if (result.IsFailure)
                {
                    StatusMessage = result.Error;
                    _logger.LogError($"Migration failed: {result.Error}");
                    MessageBoxService.Show($"Error combining files: {result.Error}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string combinedFilePath = result.Value;
                _selectedFiles = [combinedFilePath];
                InputFileList = [.. _selectedFiles];
                InputFilesInfo = $"{files.Count} files combined.";
                OutputFileName = "multiple_file_output.xlsx";
                StatusMessage = "Files combined. Ready to process.";
                _logger.LogInfo($"Files combined successfully for flux: {combinedFilePath}");

                MessageBoxService.Show($"Files combined into:\n{combinedFilePath}", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                _logger.LogException(ex, "Error in CombineFilesAsync (Flux)");
                MessageBoxService.Show($"Error combining files: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                ProgressValue = 0;
            }
        }
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

            string outputName = OutputFileName;
            if (string.IsNullOrWhiteSpace(outputName)) outputName = "FluxResult";
            if (!outputName.EndsWith(".xlsx")) outputName += ".xlsx";

            try
            {
                await Task.Run(async () =>
                {
                    var filteredSegments = new List<string>();
                    int processedFiles = 0;

                    foreach (var fileName in InputFileList)
                    {
                        if (_cts.IsCancellationRequested) break;

                        string fileContent = await File.ReadAllTextAsync(fileName);
                        string cleanedData = RegexPatterns.Whitespace().Replace(fileContent, "");
                        var matches = RegexPatterns.E225Header().Matches(cleanedData);

                        foreach (Match match in matches)
                        {
                            string segment = match.Value;
                            int segmentLength = segment.Length;

                            for (int i = 0; i < segmentLength; i += 4128)
                            {
                                int length = Math.Min(4128, segmentLength - i);
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

                    // Remove last segment if incomplete
                    if (filteredSegments.Count > 0 && filteredSegments.Last().Length < 4128)
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
                                _logger.LogError($"Failed to save flux Excel: {saveResult.Error}");
                            }
                            else
                            {
                                _logger.LogInfo($"Flux data saved to Excel: {outputName}");
                            }
                        });
                    }
                    else
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                            StatusMessage = "No valid segments found.");
                    }
                }, _cts.Token);

                StatusMessage = "Processing complete.";
                ProgressValue = 100;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                _logger.LogException(ex, "Error in ProcessData (Flux)");
                MessageBoxService.Show($"Error processing data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBoxService.Show($"File not found: {outputName}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
                    Debug.WriteLine($"[FluxVM] ReadData totalSteps: {totalSteps}");

                    Application.Current.Dispatcher.BeginInvoke(() =>
                        DataCount = totalSteps);

                    bool isFirst = true;
                    var lastUpdateTime = DateTime.Now;

                    for (int data = 0; data < totalSteps && !(_cts?.Token.IsCancellationRequested ?? false); data++)
                    {
                        string hexString = rawData.Rows[data][0].ToString() ?? "";
                        // string[] hexData = _dataProcessor.SplitHexData(hexString); // Optimized out

                        if (isFirst)
                        {
                            startTime = GetDateTimeFromHexData(hexString);
                            Application.Current.Dispatcher.BeginInvoke(() =>
                                StartTimeText = startTime.ToString("yyyy-MMM-dd HH:mm:ss.fff", new CultureInfo("en-US")));
                            isFirst = false;
                        }

                        ProcessFluxObservation(hexString);
                        lastHexString = hexString;

                        if ((DateTime.Now - lastUpdateTime).TotalMilliseconds > 300 || data % 500 == 0)
                        {
                            int currentData = data + 1;
                            double progress = (double)currentData / totalSteps * 100.0;
                            Application.Current.Dispatcher.BeginInvoke(() =>
                            {
                                ProgressValue = progress;
                                StatusMessage = $"Processing... {progress:F1}% ({currentData:N0}/{totalSteps:N0})";
                            });
                            lastUpdateTime = DateTime.Now;
                        }
                    }

                    // Get stop time from last row
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

                // Update time info
                StopTimeText = stopTime.ToString("yyyy-MMM-dd HH:mm:ss.fff", new CultureInfo("en-US"));
                if (startTime != DateTime.MinValue && stopTime != DateTime.MinValue)
                {
                    _duration = stopTime - startTime;
                    DurationText = $"{_duration.TotalSeconds:F3} seconds";
                }

                // Process header info
                if (lastHexString != null)
                    ProcessHeader(lastHexString);

                StatusMessage = "Process Complete";
                ProgressValue = 100;

                // Calculate and plot flux density
                CalculateAndPlotFlux();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBoxService.Show($"Error reading data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBoxService.Show("File not found!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                            Application.Current.Dispatcher.BeginInvoke(() =>
                                HeaderCheckStatus = $"Header is INCORRECT! at data row no. {i + 1}");
                            return;
                        }
                    }

                    Application.Current.Dispatcher.BeginInvoke(() =>
                        HeaderCheckStatus = "Header is correct!");
                });
            }
            catch (Exception ex)
            {
                MessageBoxService.Show($"Error reading file: {ex.Message}", "File Read Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void Stop()
        {
            _cts?.Cancel();
            StatusMessage = "Stopped.";
        }

        public override void Reset()
        {
            base.Reset();
            InputFilesInfo = "No files selected";
            HeaderCheckStatus = "";
            StartTimeText = "-";
            StopTimeText = "-";
            DurationText = "-";
            DataCount = 0;
            HeaderInfo = "";
            _duration = TimeSpan.Zero;
            ResetDataLists();
            foreach (var layer in Layers)
            {
                layer.XData = null;
                layer.YData = null;
                layer.StatsText = "No Data";
                layer.RenderPlot(
                    System.Drawing.Color.FromArgb(30, 30, 30),
                    System.Drawing.Color.FromArgb(37, 37, 38),
                    System.Drawing.Color.White, System.Drawing.Color.Gray);
            }
            ProgressValue = 0;
        }

        // ── Data Processing Methods ─────────────────────────────────

        private void ProcessFluxObservation(string hexString)
        {
            if (hexString.Length < 4064) return; // 2032 bytes * 2 chars

            ReadOnlySpan<char> hexSpan = hexString.AsSpan();

            // Extract time in seconds from offset bytes 16–17 (Index 32-35)
            // hex index = byte index * 2
            int t0 = int.Parse(hexSpan.Slice(32, 2), NumberStyles.HexNumber);
            int t1 = int.Parse(hexSpan.Slice(34, 2), NumberStyles.HexNumber); // 32+2 = 34
            double milliseconds = (t0 << 8) + t1;
            double timeSeconds = milliseconds / 1000.0;

            // Extract particle counting (bytes 18–31, 7 layers × 2 bytes each)
            // Start byte 18 -> char index 36
            double[] particleCounting = new double[7];
            for (int i = 0; i < 7; i++)
            {
                int idx = 36 + (i * 4); // 2 bytes = 4 hex chars
                int p0 = int.Parse(hexSpan.Slice(idx, 2), NumberStyles.HexNumber);
                int p1 = int.Parse(hexSpan.Slice(idx + 2, 2), NumberStyles.HexNumber);
                particleCounting[i] = (p0 << 8) + p1;
            }

            // Extract particle information (bytes 32–2031, 1000 particles × 2 bytes each)
            // Start byte 32 -> char index 64
            // 2000 bytes = 4000 chars
            double[] particleLayer = new double[1000];
            double[] particleOffsetTime = new double[1000];

            for (int i = 0; i < 1000; i++)
            {
                int idx = 64 + (i * 4);
                int highByte = int.Parse(hexSpan.Slice(idx, 2), NumberStyles.HexNumber);
                int lowByte = int.Parse(hexSpan.Slice(idx + 2, 2), NumberStyles.HexNumber);
                int value = (highByte << 8) | lowByte;
                particleLayer[i] = (value >> 13) & 0x07;
                particleOffsetTime[i] = value & 0x0FFF;
            }

            // Store data
            _secondsPartList.Add(timeSeconds);
            for (int i = 0; i < 7; i++)
                _particleCountingLists[i].Add(particleCounting[i]);

            _particleLayerList.Add(particleLayer);
            _particleOffsetTimeList.Add(particleOffsetTime);

            _allResults.Add(new FluxDataResult
            {
                TimeSeconds = timeSeconds,
                ParticleCounting = particleCounting,
                ParticleLayer = particleLayer,
                ParticleOffsetTime = particleOffsetTime
            });
        }

        private static DateTime GetDateTimeFromHexData(string hexString)
        {
            try
            {
                ReadOnlySpan<char> hexSpan = hexString.AsSpan();
                // Timecode is at byte 8 (char 16), length 6 bytes (12 chars)
                if (hexSpan.Length < 28) return DateTime.MinValue;

                // Bytes: 8,9,10,11 (Seconds), 12,13 (Millis)
                // We need to parse bytes.
                byte[] timecodeDec = new byte[6];
                for (int i = 0; i < 6; i++)
                {
                    timecodeDec[i] = byte.Parse(hexSpan.Slice(16 + i * 2, 2), NumberStyles.HexNumber);
                }

                var secondsPart = BitConverter.ToUInt32([.. timecodeDec.Take(4).Reverse()], 0);
                var millisecondsPart = BitConverter.ToUInt16([.. timecodeDec.Skip(4).Take(2).Reverse()], 0);
                return DateTimeOffset.FromUnixTimeSeconds(secondsPart).AddMilliseconds(millisecondsPart).DateTime;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private void ProcessHeader(string hexString)
        {
            // To keep compatibility with original logic but using string
            try
            {
                if (hexString.Length < 4128) return; // 2064 * 2
                var hexData = _dataProcessor.SplitHexData(hexString); // Fallback to split for header as it is called once
                ProcessHeaderInternal(hexData);
            }
            catch { HeaderInfo = "Error parsing header"; }
        }

        private void ProcessHeaderInternal(string[] hexData)
        {
            try
            {
                if (hexData.Length < 2064) return;

                string packetSync = $"Packet Synchronization Code: {hexData[0]} {hexData[1]}";
                string packageId = $"Package Identification: {hexData[2]} {hexData[3]}";
                string packetSeq = $"Packet Sequence: {hexData[4]} {hexData[5]}";
                string packetData = $"Packet data length: {hexData[6]} {hexData[7]}";

                // Timecode
                var timecodeHex = hexData.Skip(8).Take(6).ToArray();
                var timecodeDec = timecodeHex.Select(h => Convert.ToByte(h, 16)).ToArray();
                uint secPart = BitConverter.ToUInt32([.. timecodeDec.Take(4).Reverse()], 0);
                ushort msPart = BitConverter.ToUInt16([.. timecodeDec.Skip(4).Reverse()], 0);
                DateTime dt = DateTimeOffset.FromUnixTimeSeconds(secPart).AddMilliseconds(msPart).UtcDateTime;
                string timestamp = $"Timestamp: {dt:yyyy-MMM-dd HH:mm:ss.fff}";

                string dataType = $"Data Type: {hexData[14]} {hexData[15]}";
                string checkSumHex = $"Check Sum: {hexData[2062]} {hexData[2063]}";

                // Checksum validation
                int totalSum = hexData.Skip(8).Take(2054).Select(h => Convert.ToInt32(h, 16)).Sum();
                int lastTwoBytes = totalSum % 65536;
                string checksumCalc = lastTwoBytes.ToString("X4");
                string checksumFromData = hexData[2062] + hexData[2063];
                string checksumResult = checksumCalc.Equals(checksumFromData, StringComparison.OrdinalIgnoreCase)
                    ? "Checksum matches!" : "Checksum does not match.";

                string testConditions = $"Test condition:\nDelay Time: {DelayTime}\nThreshold: {Threshold}";

                HeaderInfo = string.Join("\n",
                    packetSync, packageId, packetSeq, packetData,
                    timestamp, dataType, checkSumHex, checksumResult, testConditions);
            }
            catch { HeaderInfo = "Error parsing header"; }
        }

        private void CalculateAndPlotFlux()
        {
            Debug.WriteLine($"[FluxVM] CalculateAndPlotFlux called. SecondsPartList count: {_secondsPartList.Count}");
            if (_secondsPartList.Count == 0) return;

            // Compute cumulative time
            int count = _secondsPartList.Count;
            double[] cumulativeTime = new double[count];
            double[] timeSeconds = [.. _secondsPartList];

            cumulativeTime[0] = timeSeconds[0];
            for (int i = 1; i < count; i++)
                cumulativeTime[i] = cumulativeTime[i - 1] + timeSeconds[i];

            // Calculate flux density for each layer
            for (int layer = 0; layer < LAYER_COUNT; layer++)
            {
                double[] particleCounting = [.. _particleCountingLists[layer]];

                // Use list to collect valid points to avoid .Where().ToArray() double allocation
                List<double> xPoints = new(count);
                List<double> yPoints = new(count);
                double maxFlux = 0;

                for (int j = 0; j < count; j++)
                {
                    double t = timeSeconds[j];
                    double flux = t > 0
                        ? particleCounting[j] / (t * DETECTOR_AREA_M2)
                        : 0;

                    if (!double.IsNaN(flux) && !double.IsInfinity(flux))
                    {
                        xPoints.Add(cumulativeTime[j]);
                        yPoints.Add(flux);
                        if (flux > maxFlux) maxFlux = flux;
                    }
                }

                if (xPoints.Count > 0)
                {
                    Layers[layer].XData = [.. xPoints];
                    Layers[layer].YData = [.. yPoints];
                    Layers[layer].StatsText = $"Points: {xPoints.Count:N0} | Max: {maxFlux:F2}";
                }
                else
                {
                    Layers[layer].XData = null;
                    Layers[layer].YData = null;
                    Layers[layer].StatsText = "No valid flux data";
                }
            }

            UpdateAllPlots();
        }

        private void UpdateAllPlots()
        {
            if (Layers == null || Layers.Count == 0) return;

            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                var figBg = ColorHelper.ToDrawingColor(GraphFigureColor, System.Drawing.Color.FromArgb(30, 30, 30));
                var dataBg = ColorHelper.ToDrawingColor(GraphDataColor, System.Drawing.Color.FromArgb(37, 37, 38));
                var fgColor = ColorHelper.ToDrawingColor(GraphTextColor, System.Drawing.Color.White);
                var seriesColor = ColorHelper.ToDrawingColor(GraphSeriesColor, System.Drawing.Color.Cyan);

                // Use different colors for each layer
                var layerColors = new[]
                {
                    System.Drawing.Color.FromArgb(255, 0, 150, 136),   // Teal
                    System.Drawing.Color.FromArgb(255, 33, 150, 243),   // Blue
                    System.Drawing.Color.FromArgb(255, 156, 39, 176),   // Purple
                    System.Drawing.Color.FromArgb(255, 255, 152, 0),    // Orange
                    System.Drawing.Color.FromArgb(255, 76, 175, 80),    // Green
                    System.Drawing.Color.FromArgb(255, 244, 67, 54),    // Red
                    System.Drawing.Color.FromArgb(255, 255, 235, 59),   // Yellow
                };

                for (int i = 0; i < Layers.Count; i++)
                {
                    Layers[i].RenderPlot(
                        figBg, dataBg, fgColor,
                        seriesColor, // Use the selected single color for all graphs
                        isLogScale: IsLogScale,
                        xMin: TimeRangeMin,
                        xMax: TimeRangeMax > 0 ? TimeRangeMax : null,
                        widthMultiplier: BarWidthMultiplier);
                }
            });
        }

        private void ResetDataLists()
        {
            _secondsPartList.Clear();
            foreach (var list in _particleCountingLists) list.Clear();
            _particleLayerList.Clear();
            _particleOffsetTimeList.Clear();
            _allResults.Clear();
        }


    }
}
