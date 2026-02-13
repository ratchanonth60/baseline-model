using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BaselineMode.WPF.Core.Interfaces;
using BaselineMode.WPF.Presentation.Views.Calibration;
using BaselineMode.WPF.Core.Interfaces.Observation;
using BaselineMode.WPF.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExcelDataReader;
using Microsoft.Win32;
using BaselineMode.WPF.Core.Models.Shared;
using BaselineMode.WPF.Presentation.ViewModels.Shared;

namespace BaselineMode.WPF.Presentation.ViewModels.Calibration
{
    public partial class CalibrationViewModel : SharedViewModelBase
    {
        private readonly IMathService _mathService;
        private readonly IFileHelper _fileHelper;
        private readonly IObservationDataProcessor _dataProcessor;

        private const int ESTIMATED_ROWS = 10000;
        private const int DATA_POINTS_PER_ROW = 11;
        private const int INITIAL_CAPACITY = ESTIMATED_ROWS * DATA_POINTS_PER_ROW;

        public CalibrationViewModel(IMathService mathService, IFileHelper fileHelper, IObservationDataProcessor dataProcessor)
        {
            _mathService = mathService;
            _fileHelper = fileHelper;
            _dataProcessor = dataProcessor;

            Channels = [];
            for (int i = 0; i < 16; i++)
            {
                string name = i < 8 ? $"X{i + 1}" : $"Z{i - 7}";
                Channels.Add(new ChannelViewModel { ChannelName = name, Title = name, ChannelIndex = i });

                _l1Columns[i] = new List<double>(INITIAL_CAPACITY);
                _l2Columns[i] = new List<double>(INITIAL_CAPACITY);
                _l6Columns[i] = new List<double>(INITIAL_CAPACITY);
                _l7Columns[i] = new List<double>(INITIAL_CAPACITY);

                _l1VoltColumns[i] = new List<double>(INITIAL_CAPACITY);
                _l2VoltColumns[i] = new List<double>(INITIAL_CAPACITY);
                _l6VoltColumns[i] = new List<double>(INITIAL_CAPACITY);
                _l7VoltColumns[i] = new List<double>(INITIAL_CAPACITY);
            }
        }

        [ObservableProperty]
        private string _inputFilesInfo = "No files selected";

        [ObservableProperty]
        private string _outputFileName = "CalibrationResult";

        [ObservableProperty]
        private int _selectedLayerIndex = 0;

        [ObservableProperty]
        private int _selectedXAxisIndex = 0;

        [ObservableProperty]
        private double _xAxisMin = 0;

        [ObservableProperty]
        private double _xAxisMax = 16384;

        [ObservableProperty]
        private int _delayTime = 50;

        [ObservableProperty]
        private int _threshold = 50;

        [ObservableProperty]
        private double _barWidthMultiplier = 1.0;

        partial void OnBarWidthMultiplierChanged(double value)
        {
            foreach (var ch in Channels)
                ch.BarWidthMultiplier = value;
            _ = UpdatePlotsAsync();
        }

        [ObservableProperty]
        private System.Windows.Media.Color _graphFigureColor = System.Windows.Media.Color.FromRgb(30, 30, 30);

        [ObservableProperty]
        private System.Windows.Media.Color _graphDataColor = System.Windows.Media.Color.FromRgb(37, 37, 38);

        [ObservableProperty]
        private System.Windows.Media.Color _graphSeriesColor = System.Windows.Media.Colors.Cyan;

        [ObservableProperty]
        private System.Windows.Media.Color _graphTextColor = System.Windows.Media.Colors.White;

        partial void OnGraphFigureColorChanged(System.Windows.Media.Color value) => _ = UpdatePlotsAsync();
        partial void OnGraphDataColorChanged(System.Windows.Media.Color value) => _ = UpdatePlotsAsync();
        partial void OnGraphSeriesColorChanged(System.Windows.Media.Color value) => _ = UpdatePlotsAsync();
        partial void OnGraphTextColorChanged(System.Windows.Media.Color value) => _ = UpdatePlotsAsync();

        [ObservableProperty]
        private string _headerCheckStatus = "";

        //  เพิ่ม flag สำหรับโหมด multi-file
        [ObservableProperty]
        private bool _readMultipleFiles = false;

        private CancellationTokenSource? _cts;

        private readonly List<double>[] _l1Columns = new List<double>[16];
        private readonly List<double>[] _l2Columns = new List<double>[16];
        private readonly List<double>[] _l6Columns = new List<double>[16];
        private readonly List<double>[] _l7Columns = new List<double>[16];

        private readonly List<double>[] _l1VoltColumns = new List<double>[16];
        private readonly List<double>[] _l2VoltColumns = new List<double>[16];
        private readonly List<double>[] _l6VoltColumns = new List<double>[16];
        private readonly List<double>[] _l7VoltColumns = new List<double>[16];

        public ObservableCollection<ChannelViewModel> Channels { get; }

        public IEnumerable<ChannelViewModel> XChannels => Channels.Take(8);
        public IEnumerable<ChannelViewModel> ZChannels => Channels.Skip(8).Take(8);

        //  SelectFiles สำหรับ ProcessData (txt files)
        [RelayCommand]
        private void SelectFiles()
        {
            var openFileDialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                InputFileList = openFileDialog.FileNames;
                ReadMultipleFiles = false; // Reset flag
                InputFilesInfo = $"{InputFileList.Length} txt file(s) selected";
                StatusMessage = "Files selected.";
            }
        }

        //  SelectExcelFiles สำหรับ ReadData (Excel files)
        [RelayCommand]
        private void SelectExcelFiles()
        {
            var openFileDialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                Title = "Select Excel files to read"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                InputFileList = openFileDialog.FileNames;
                ReadMultipleFiles = true; // Set flag
                InputFilesInfo = $"{InputFileList.Length} Excel file(s) selected for reading";
                StatusMessage = "Excel files selected.";
            }
        }

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

            string outputName = OutputFileName;
            if (string.IsNullOrWhiteSpace(outputName)) outputName = "CalibrationResult";
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
                                string chunk = segment.Substring(i, length);
                                filteredSegments.Add(chunk);
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

                    if (filteredSegments.Count > 0 && filteredSegments.Last().Length < 4128)
                    {
                        filteredSegments.RemoveAt(filteredSegments.Count - 1);
                    }

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
                        Application.Current.Dispatcher.BeginInvoke(() => StatusMessage = "No valid segments found.");
                    }
                }, _cts.Token);

                StatusMessage = "Processing complete.";
                ProgressValue = 100;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBoxService.Show($"Error processing data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        //  ปรับปรุง ReadData ให้รองรับทั้ง single และ multi-file
        [RelayCommand]
        private async Task ReadData()
        {
            List<string> filesToRead = [];

            // กรณีที่ 1: อ่านหลายไฟล์ที่เลือกไว้
            if (ReadMultipleFiles && InputFileList != null && InputFileList.Length > 0)
            {
                filesToRead.AddRange(InputFileList.Where(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) && File.Exists(f)));

                if (filesToRead.Count == 0)
                {
                    MessageBoxService.Show("No valid Excel files found in selection.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            // กรณีที่ 2: หาไฟล์เดียวจาก OutputFileName
            else
            {
                string outputName = OutputFileName;
                if (!outputName.EndsWith(".xlsx")) outputName += ".xlsx";

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
                //  Step 1: นับจำนวนแถวทั้งหมดจากทุกไฟล์
                StatusMessage = $"Counting rows in {filesToRead.Count} file(s)...";
                int totalRows = 0;

                for (int fileIndex = 0; fileIndex < filesToRead.Count; fileIndex++)
                {
                    if (_cts.Token.IsCancellationRequested) return;

                    string currentFile = filesToRead[fileIndex];

                    await Task.Run(() =>
                    {
                        using var stream = File.Open(currentFile, FileMode.Open, FileAccess.Read, FileShare.Read);
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

                //  Step 2: สร้าง Lists ด้วย exact capacity
                int exactCapacity = totalRows * DATA_POINTS_PER_ROW;
                ResetDataLists(exactCapacity);

                StatusMessage = $"Found {totalRows:N0} total rows from {filesToRead.Count} file(s). Reading data...";
                HeaderCheckStatus = "Checking...";

                //  Step 3: อ่านข้อมูลจริงจากทุกไฟล์
                int totalRowsRead = 0;
                bool headerCheckPassed = true;

                for (int fileIndex = 0; fileIndex < filesToRead.Count; fileIndex++)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    string currentFile = filesToRead[fileIndex];

                    await Task.Run(() =>
                    {
                        using (var stream = File.Open(currentFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (var reader = ExcelReaderFactory.CreateReader(stream))
                        {
                            int rowCount = 0;
                            var lastUpdateTime = DateTime.Now;

                            while (reader.Read())
                            {
                                if (_cts.Token.IsCancellationRequested) break;

                                string hexString = reader.GetValue(0)?.ToString() ?? "";
                                string[] hexData = _dataProcessor.SplitHexData(hexString);

                                // Header check เฉพาะไฟล์แรก แถวแรก
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
                                        StatusMessage = $"File {fileIndex + 1}/{filesToRead.Count}: {rowCount:N0} rows | " +
                                                      $"Total: {currentTotalRows:N0}/{totalRows:N0} ({progress:F1}%)";
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
                MessageBoxService.Show($"Error reading data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                ProgressValue = 100;
                ReadMultipleFiles = false; // รีเซ็ตสถานะ
            }
        }



        [RelayCommand]
        private void Stop()
        {
            _cts?.Cancel();
            StatusMessage = "Stopped.";
        }

        [RelayCommand]
        public override void Reset()
        {
            base.Reset();
            InputFilesInfo = "No files selected";
            ReadMultipleFiles = false;
            ResetDataLists();
            foreach (var ch in Channels)
            {
                ch.Counts = null;
                ch.BinCenters = null;
                ch.StatsText = "";
                ch.RenderPlot(Color.FromArgb(30, 30, 30), Color.FromArgb(37, 37, 38), Color.White, Color.Gray);
            }
            StatusMessage = "Reset complete.";
            ProgressValue = 0;
            HeaderCheckStatus = "";
        }

        private void ResetDataLists(int? customCapacity = null)
        {
            int capacity = customCapacity ?? INITIAL_CAPACITY;

            for (int i = 0; i < 16; i++)
            {
                _l1Columns[i]?.Clear();
                _l2Columns[i]?.Clear();
                _l6Columns[i]?.Clear();
                _l7Columns[i]?.Clear();
                _l1VoltColumns[i]?.Clear();
                _l2VoltColumns[i]?.Clear();
                _l6VoltColumns[i]?.Clear();
                _l7VoltColumns[i]?.Clear();

                _l1Columns[i] = new List<double>(capacity);
                _l2Columns[i] = new List<double>(capacity);
                _l6Columns[i] = new List<double>(capacity);
                _l7Columns[i] = new List<double>(capacity);

                _l1VoltColumns[i] = new List<double>(capacity);
                _l2VoltColumns[i] = new List<double>(capacity);
                _l6VoltColumns[i] = new List<double>(capacity);
                _l7VoltColumns[i] = new List<double>(capacity);
            }

            Application.Current.Dispatcher.BeginInvoke(() =>
                StatusMessage = $"Lists initialized with capacity: {capacity:N0} per channel");
        }

        private void ProcessCalibration(string[] hexData)
        {
            if (hexData.Length < 18) return;
            if (_cts?.Token.IsCancellationRequested ?? false) return;

            const double voltageScale = (5.0 / 16383.0) * 1000.0;

            for (int i = 0; i < 11; i++)
            {
                int offsetL1L2 = 18 + 64 * i;
                int offsetL6L7 = 722 + 64 * i;

                if (offsetL1L2 + 64 > hexData.Length || offsetL6L7 + 64 > hexData.Length)
                    continue;

                if (i % 3 == 0 && (_cts?.Token.IsCancellationRequested ?? false))
                    return;

                for (int j = 0; j < 16; j++)
                {
                    int l1Idx = offsetL1L2 + (j * 2);
                    int l1Val = ParseHexPair(hexData, l1Idx);
                    _l1Columns[j].Add(l1Val);
                    _l1VoltColumns[j].Add(l1Val * voltageScale);

                    int l2Idx = offsetL1L2 + 32 + (j * 2);
                    int l2Val = ParseHexPair(hexData, l2Idx);
                    _l2Columns[j].Add(l2Val);
                    _l2VoltColumns[j].Add(l2Val * voltageScale);

                    int l6Idx = offsetL6L7 + (j * 2);
                    int l6Val = ParseHexPair(hexData, l6Idx);
                    _l6Columns[j].Add(l6Val);
                    _l6VoltColumns[j].Add(l6Val * voltageScale);

                    int l7Idx = offsetL6L7 + 32 + (j * 2);
                    int l7Val = ParseHexPair(hexData, l7Idx);
                    _l7Columns[j].Add(l7Val);
                    _l7VoltColumns[j].Add(l7Val * voltageScale);
                }
            }
        }

        private static int ParseHexPair(string[] hexData, int startIndex)
        {
            try
            {
                if (startIndex + 1 >= hexData.Length) return 0;

                int high = Convert.ToInt32(hexData[startIndex], 16);
                int low = Convert.ToInt32(hexData[startIndex + 1], 16);
                return (high << 8) + low;
            }
            catch
            {
                return 0;
            }
        }

        [RelayCommand]
        private void OpenZoomWindow(ChannelViewModel channel)
        {
            if (channel == null) return;

            var sourceColumns = SelectedXAxisIndex == 1
                ? GetVoltageColumns(SelectedLayerIndex)
                : GetCalibrationColumns(SelectedLayerIndex);

            if (channel.ChannelIndex < 0 || channel.ChannelIndex >= sourceColumns.Length) return;

            var rawData = sourceColumns[channel.ChannelIndex].ToArray();
            if (rawData.Length == 0) return;

            if (_mathService is not IFittingService fittingService) return;

            var window = new CalibrationDetailWindow(fittingService);

            string axisLabel = SelectedXAxisIndex == 1 ? "Voltage (mV)" : "ADC Channel";

            var figureBg = ToDrawingColor(GraphFigureColor, System.Drawing.Color.FromArgb(255, 30, 30, 30));
            var dataBg = ToDrawingColor(GraphDataColor, System.Drawing.Color.FromArgb(255, 37, 37, 38));
            var fgColor = ToDrawingColor(GraphTextColor, System.Drawing.Color.White);

            window.SetColorTheme(figureBg, dataBg, fgColor);

            var drawingColor = ToDrawingColor(GraphSeriesColor, System.Drawing.Color.Cyan);
            window.ShowHistogram(rawData, channel.Title, showFit: true, color: drawingColor, xLabel: axisLabel);
            window.Show();
        }

        private static System.Drawing.Color ToDrawingColor(System.Windows.Media.Color wpfColor, System.Drawing.Color fallback)
        {
            try
            {
                return System.Drawing.Color.FromArgb(wpfColor.A, wpfColor.R, wpfColor.G, wpfColor.B);
            }
            catch { return fallback; }
        }

        partial void OnSelectedXAxisIndexChanged(int value)
        {
            if (value == 1) // Voltage
            {
                XAxisMin = 0;
                XAxisMax = 5000;
            }
            else // ADC
            {
                XAxisMin = 0;
                XAxisMax = 16384;
            }
            _ = UpdatePlotsAsync();
        }

        partial void OnXAxisMinChanged(double value) => _ = UpdatePlotsAsync();
        partial void OnXAxisMaxChanged(double value) => _ = UpdatePlotsAsync();

        partial void OnSelectedLayerIndexChanged(int value) => _ = UpdatePlotsAsync();

        private async Task UpdatePlotsAsync()
        {
            await Task.Run(() => UpdatePlots());
        }

        private void UpdatePlots()
        {
            var sourceColumns = SelectedXAxisIndex == 1
               ? GetVoltageColumns(SelectedLayerIndex)
               : GetCalibrationColumns(SelectedLayerIndex);

            int channelCount = 16;
            double xMax = XAxisMax;
            double xMin = XAxisMin;
            string xLabel = SelectedXAxisIndex == 1 ? "Voltage (mV)" : "ADC Channel";

            var plotResults = new (double[] counts, double[] binCenters, string statsText, int channel)[channelCount];

            Parallel.For(0, channelCount, ch =>
            {
                var columnData = sourceColumns[ch];
                var dataForChannel = columnData.Where(d => d > 0).ToArray();

                if (dataForChannel.Length > 0)
                {
                    // Use dynamic min/max for histogram
                    var (counts, binEdges) = ScottPlot.Statistics.Common.Histogram(
                        dataForChannel, min: xMin, max: xMax, binCount: 500);

                    double[] binCenters = new double[binEdges.Length - 1];
                    for (int k = 0; k < binCenters.Length; k++)
                    {
                        binCenters[k] = (binEdges[k] + binEdges[k + 1]) / 2.0;
                    }


                    plotResults[ch] = (counts, binCenters, $"Counts: {dataForChannel.Length:N0}", ch);
                }
            });

            Application.Current.Dispatcher.Invoke(() =>
            {
                for (int ch = 0; ch < channelCount; ch++)
                {
                    var (counts, binCenters, statsText, channel) = plotResults[ch];
                    if (counts != null)
                    {
                        var channelVM = Channels[ch];
                        channelVM.Counts = counts;
                        channelVM.BinCenters = binCenters;
                        channelVM.StatsText = statsText;

                        channelVM.RenderPlot(
                            ToDrawingColor(GraphFigureColor, Color.FromArgb(30, 30, 30)),
                            ToDrawingColor(GraphDataColor, Color.FromArgb(37, 37, 38)),
                            ToDrawingColor(GraphTextColor, Color.White),
                            ToDrawingColor(GraphSeriesColor, Color.Cyan),
                            xMin: xMin,
                            xMax: xMax,
                            xLabel: xLabel
                        );
                    }
                }
            });
        }

        private List<double>[] GetCalibrationColumns(int layerIndex) => layerIndex switch
        {
            0 => _l1Columns,
            1 => _l2Columns,
            2 => _l6Columns,
            3 => _l7Columns,
            _ => _l1Columns
        };

        private List<double>[] GetVoltageColumns(int layerIndex) => layerIndex switch
        {
            0 => _l1VoltColumns,
            1 => _l2VoltColumns,
            2 => _l6VoltColumns,
            3 => _l7VoltColumns,
            _ => _l1VoltColumns
        };
    }
}