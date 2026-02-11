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
using BaselineMode.WPF.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExcelDataReader;
using Microsoft.Win32;

namespace BaselineMode.WPF.Presentation.ViewModels
{
    public partial class FluxViewModel : SharedViewModelBase
    {
        private readonly IFileHelper _fileHelper;
        private readonly IObservationDataProcessor _dataProcessor;

        private const int LAYER_COUNT = 7;
        private const double DETECTOR_AREA_M2 = 32 * 32 * 1e-6; // 32mm × 32mm → m²

        public IRelayCommand SelectFilesCommand { get; }
        public IAsyncRelayCommand ProcessDataCommand { get; }
        public IAsyncRelayCommand ReadDataCommand { get; }
        public IAsyncRelayCommand HeaderCheckCommand { get; }
        public IRelayCommand StopCommand { get; }
        public IRelayCommand ResetCommand { get; }

        public FluxViewModel(IFileHelper fileHelper, IObservationDataProcessor dataProcessor)
        {
            _fileHelper = fileHelper;
            _dataProcessor = dataProcessor;

            SelectFilesCommand = new RelayCommand(SelectFiles);
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
        partial void OnTimeRangeMaxChanged(double value) => UpdateAllPlots();

        // ── Data Storage ────────────────────────────────────────────

        private readonly List<double> _secondsPartList = [];
        private readonly List<double>[] _particleCountingLists = [.. Enumerable.Range(0, LAYER_COUNT).Select(_ => new List<double>())];
        private readonly List<double[]> _particleLayerList = [];
        private readonly List<double[]> _particleOffsetTimeList = [];
        private readonly List<FluxDataResult> _allResults = [];

        private CancellationTokenSource? _cts;
        private TimeSpan _duration = TimeSpan.Zero;
        private string? _combinedOutputFilePath;

        public ObservableCollection<FluxLayerViewModel> Layers { get; }

        // ── Commands ────────────────────────────────────────────────

        private void SelectFiles()
        {
            Reset();

            var openFileDialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                Title = "Select Text Files"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                InputFileList = openFileDialog.FileNames;
                var fileNames = InputFileList.Select(f => Path.GetFileNameWithoutExtension(f)).ToList();

                if (InputFileList.Length == 1)
                {
                    InputFilesInfo = "1 file selected.";
                    OutputFileName = $"{fileNames.First()}.xlsx";
                }
                else
                {
                    try
                    {
                        string storageDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                            "CombinedTextFiles");
                        Directory.CreateDirectory(storageDir);

                        _combinedOutputFilePath = Path.Combine(storageDir, "multiple_file_output.txt");

                        var allContents = new List<string>();
                        foreach (var file in InputFileList)
                        {
                            try { allContents.Add(File.ReadAllText(file)); }
                            catch (Exception ex)
                            {
                                MessageBoxService.Show($"Error reading {file}: {ex.Message}", "Read Error",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }

                        if (allContents.Count > 0)
                        {
                            File.WriteAllText(_combinedOutputFilePath, string.Join("\n", allContents));
                            MessageBoxService.Show(
                                $"Files combined successfully into {_combinedOutputFilePath}",
                                "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                            InputFileList = [_combinedOutputFilePath];
                        }

                        InputFilesInfo = $"{openFileDialog.FileNames.Length} file(s) selected.";
                        OutputFileName = "multiple_file_output.xlsx";
                    }
                    catch (Exception ex)
                    {
                        MessageBoxService.Show($"Error combining files: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                InputFilesInfo = "No files selected.";
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
                await Task.Run(() =>
                {
                    var filteredSegments = new List<string>();
                    int processedFiles = 0;

                    foreach (var fileName in InputFileList)
                    {
                        if (_cts.IsCancellationRequested) break;

                        string fileContent = File.ReadAllText(fileName);
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

                        Application.Current.Dispatcher.BeginInvoke(() =>
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
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = $"Saving {filteredSegments.Count:N0} segments to Excel...";
                            _fileHelper.SaveToExcel(filteredSegments, outputName, "Source");
                        });
                    }
                    else
                    {
                        Application.Current.Dispatcher.BeginInvoke(() =>
                            StatusMessage = "No valid segments found.");
                    }
                }, _cts.Token);

                StatusMessage = "Processing complete.";
                ProgressValue = 100;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
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
                string[]? lastHexData = null;

                await Task.Run(() =>
                {
                    using var stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
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
                        string[] hexData = _dataProcessor.SplitHexData(hexString);

                        if (isFirst)
                        {
                            startTime = GetDateTimeFromHexData(hexData);
                            Application.Current.Dispatcher.BeginInvoke(() =>
                                StartTimeText = startTime.ToString("yyyy-MMM-dd HH:mm:ss.fff", new CultureInfo("en-US")));
                            isFirst = false;
                        }

                        ProcessFluxObservation(hexData);
                        lastHexData = hexData;

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
                        var lastData = _dataProcessor.SplitHexData(lastHex);
                        stopTime = GetDateTimeFromHexData(lastData);
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
                if (lastHexData != null)
                    ProcessHeader(lastHexData);

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
                    using var stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
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
                    Color.FromArgb(30, 30, 30),
                    Color.FromArgb(37, 37, 38),
                    Color.White, Color.Gray);
            }
            ProgressValue = 0;
        }

        // ── Data Processing Methods ─────────────────────────────────

        private void ProcessFluxObservation(string[] hexData)
        {
            if (hexData.Length < 2032) return;

            // Extract time in seconds from offset bytes 16–17
            var timecodeHex = hexData.Skip(16).Take(2).ToArray();
            var timecodeDec = timecodeHex.Select(h => Convert.ToInt32(h, 16)).ToArray();
            double milliseconds = (timecodeDec[0] << 8) + timecodeDec[1];
            double timeSeconds = milliseconds / 1000.0;

            // Extract particle counting (bytes 18–31, 7 layers × 2 bytes each)
            var particleCountingHex = hexData.Skip(18).Take(14).ToArray();
            double[] particleCounting = new double[7];
            for (int i = 0; i < 7; i++)
            {
                var hexPair = particleCountingHex.Skip(i * 2).Take(2).ToArray();
                var decPair = hexPair.Select(hex => Convert.ToInt32(hex, 16)).ToArray();
                particleCounting[i] = (decPair[0] << 8) + decPair[1];
            }

            // Extract particle information (bytes 32–2031, 1000 particles × 2 bytes each)
            var particleInfoHex = hexData.Skip(32).Take(2000).ToArray();
            var particleInfoDec = particleInfoHex.Select(hex => Convert.ToInt32(hex, 16)).ToArray();
            double[] particleLayer = new double[1000];
            double[] particleOffsetTime = new double[1000];

            for (int i = 0; i < 1000; i++)
            {
                int highByte = particleInfoDec[2 * i];
                int lowByte = particleInfoDec[2 * i + 1];
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

        private static DateTime GetDateTimeFromHexData(string[] hexData)
        {
            try
            {
                var timecodeHex = hexData.Skip(8).Take(6).ToArray();
                var timecodeDec = timecodeHex.Select(h => Convert.ToByte(h, 16)).ToArray();
                var secondsPart = BitConverter.ToUInt32([.. timecodeDec.Take(4).Reverse()], 0);
                var millisecondsPart = BitConverter.ToUInt16([.. timecodeDec.Skip(4).Take(2).Reverse()], 0);
                return DateTimeOffset.FromUnixTimeSeconds(secondsPart).AddMilliseconds(millisecondsPart).DateTime;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private void ProcessHeader(string[] hexData)
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

            double[] timeSeconds = [.. _secondsPartList];

            // Compute cumulative time
            double[] cumulativeTime = new double[timeSeconds.Length];
            cumulativeTime[0] = timeSeconds[0];
            for (int i = 1; i < timeSeconds.Length; i++)
                cumulativeTime[i] = cumulativeTime[i - 1] + timeSeconds[i];

            // Calculate flux density for each layer
            for (int layer = 0; layer < LAYER_COUNT; layer++)
            {
                double[] particleCounting = [.. _particleCountingLists[layer]];
                double[] particleFlux = new double[particleCounting.Length];

                for (int j = 0; j < particleCounting.Length; j++)
                {
                    particleFlux[j] = timeSeconds[j] > 0
                        ? particleCounting[j] / (timeSeconds[j] * DETECTOR_AREA_M2)
                        : 0;
                }

                // Filter out NaN/Inf
                var filtered = cumulativeTime.Zip(particleFlux, (t, f) => new { t, f })
                    .Where(p => !double.IsNaN(p.f) && !double.IsInfinity(p.f))
                    .ToArray();

                if (filtered.Length > 0)
                {
                    Layers[layer].XData = [.. filtered.Select(p => p.t)];
                    Layers[layer].YData = [.. filtered.Select(p => p.f)];
                    Layers[layer].StatsText = $"Points: {filtered.Length:N0} | Max: {filtered.Max(p => p.f):F2}";
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
                var figBg = ToDrawingColor(GraphFigureColor, Color.FromArgb(30, 30, 30));
                var dataBg = ToDrawingColor(GraphDataColor, Color.FromArgb(37, 37, 38));
                var fgColor = ToDrawingColor(GraphTextColor, Color.White);
                var seriesColor = ToDrawingColor(GraphSeriesColor, Color.Cyan);

                // Use different colors for each layer
                var layerColors = new[]
                {
                    Color.FromArgb(255, 0, 150, 136),   // Teal
                    Color.FromArgb(255, 33, 150, 243),   // Blue
                    Color.FromArgb(255, 156, 39, 176),   // Purple
                    Color.FromArgb(255, 255, 152, 0),    // Orange
                    Color.FromArgb(255, 76, 175, 80),    // Green
                    Color.FromArgb(255, 244, 67, 54),    // Red
                    Color.FromArgb(255, 255, 235, 59),   // Yellow
                };

                for (int i = 0; i < Layers.Count; i++)
                {
                    Layers[i].RenderPlot(
                        figBg, dataBg, fgColor,
                        layerColors[i % layerColors.Length],
                        isLogScale: IsLogScale,
                        xMax: TimeRangeMax > 0 ? TimeRangeMax : null);
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

        private static Color ToDrawingColor(System.Windows.Media.Color wpfColor, Color fallback)
        {
            try { return Color.FromArgb(wpfColor.A, wpfColor.R, wpfColor.G, wpfColor.B); }
            catch { return fallback; }
        }
    }
}
