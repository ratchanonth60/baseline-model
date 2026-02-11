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

namespace BaselineMode.WPF.Presentation.ViewModels
{
    public partial class CalibrationViewModel : SharedViewModelBase
    {
        private readonly IMathService _mathService;
        private readonly IFileHelper _fileHelper;
        private readonly IObservationDataProcessor _dataProcessor;

        //  เพิ่มค่าคงที่สำหรับ capacity
        private const int ESTIMATED_ROWS = 10000;  // ประมาณการจำนวนแถว
        private const int DATA_POINTS_PER_ROW = 11; // 11 loops per row
        private const int INITIAL_CAPACITY = ESTIMATED_ROWS * DATA_POINTS_PER_ROW; // 110,000

        public CalibrationViewModel(IMathService mathService, IFileHelper fileHelper, IObservationDataProcessor dataProcessor)
        {
            _mathService = mathService;
            _fileHelper = fileHelper;
            _dataProcessor = dataProcessor;

            Channels = new ObservableCollection<ChannelViewModel>();
            for (int i = 0; i < 16; i++)
            {
                string name = i < 8 ? $"X{i + 1}" : $"Z{i - 7}";
                Channels.Add(new ChannelViewModel { ChannelName = name, Title = name, ChannelIndex = i });

                //  Initialize with capacity
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
        private int _delayTime = 50;

        [ObservableProperty]
        private int _threshold = 50;

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

        private CancellationTokenSource? _cts;

        private List<double>[] _l1Columns = new List<double>[16];
        private List<double>[] _l2Columns = new List<double>[16];
        private List<double>[] _l6Columns = new List<double>[16];
        private List<double>[] _l7Columns = new List<double>[16];

        private List<double>[] _l1VoltColumns = new List<double>[16];
        private List<double>[] _l2VoltColumns = new List<double>[16];
        private List<double>[] _l6VoltColumns = new List<double>[16];
        private List<double>[] _l7VoltColumns = new List<double>[16];

        public ObservableCollection<ChannelViewModel> Channels { get; }

        public IEnumerable<ChannelViewModel> XChannels => Channels.Take(8);
        public IEnumerable<ChannelViewModel> ZChannels => Channels.Skip(8).Take(8);

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
                InputFilesInfo = $"{InputFileList.Length} files selected";
                StatusMessage = "Files selected.";
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
                        string cleanedData = BaselineMode.WPF.Core.Models.RegexPatterns.Whitespace().Replace(fileContent, "");
                        var matches = BaselineMode.WPF.Core.Models.RegexPatterns.E225Header().Matches(cleanedData);

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

                        //  ใช้ BeginInvoke แทน Invoke
                        Application.Current.Dispatcher.BeginInvoke(() => ProgressValue = progress);
                    }

                    if (filteredSegments.Count > 0 && filteredSegments.Last().Length < 4128)
                    {
                        filteredSegments.RemoveAt(filteredSegments.Count - 1);
                    }

                    if (filteredSegments.Count > 0)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
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

        [RelayCommand]
        private async Task ReadData()
        {
            string outputName = OutputFileName;
            if (!outputName.EndsWith(".xlsx")) outputName += ".xlsx";

            string? fileName = _fileHelper.FindExcelFile(Path.GetFileNameWithoutExtension(outputName));

            if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName))
            {
                MessageBoxService.Show($"File not found: {outputName}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            IsBusy = true;
            ProgressValue = 0;
            StatusMessage = "Reading data...";
            _cts = new CancellationTokenSource();

            try
            {
                ResetDataLists();
                HeaderCheckStatus = "Checking...";

                await Task.Run(() =>
                {
                    using var stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var reader = ExcelReaderFactory.CreateReader(stream);
                    int rowCount = 0;
                    var lastUpdateTime = DateTime.Now;

                    while (reader.Read())
                    {
                        if (_cts.Token.IsCancellationRequested) break;

                        string hexString = reader.GetValue(0)?.ToString() ?? "";
                        string[] hexData = _dataProcessor.SplitHexData(hexString);

                        if (rowCount == 0)
                        {
                            bool isHeaderValid = _dataProcessor.ValidateHeader(hexData);

                            //  ใช้ BeginInvoke (non-blocking)
                            Application.Current.Dispatcher.BeginInvoke(() =>
                                HeaderCheckStatus = isHeaderValid ? "Checksum OK" : "Checksum Mismatch");

                            if (!isHeaderValid) return;
                        }

                        ProcessCalibration(hexData, rowCount);

                        //  อัพเดททุก 300ms แทน 200ms เพื่อลด overhead
                        if ((DateTime.Now - lastUpdateTime).TotalMilliseconds > 300 || rowCount % 1000 == 0)
                        {
                            double streamProgress = Math.Min(100, (double)stream.Position / stream.Length * 100.0);
                            int currentRow = rowCount;

                            Application.Current.Dispatcher.BeginInvoke(() =>
                            {
                                ProgressValue = streamProgress;
                                StatusMessage = $"Reading... {currentRow:N0} rows";
                            });

                            lastUpdateTime = DateTime.Now;
                        }
                        rowCount++;
                    }
                }, _cts.Token);

                if (HeaderCheckStatus == "Checksum Mismatch")
                {
                    StatusMessage = "Stopped: Checksum Mismatch";
                    MessageBoxService.Show("Checksum Mismatch! Processing Stopped.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                StatusMessage = "Data read complete. Updating plots...";

                //  อัพเดท plots ใน background แต่ใช้วิธีที่ปรับปรุงแล้ว
                await UpdatePlotsAsync();
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

        //  ปรับปรุง ResetDataLists ให้สร้าง List ใหม่พร้อม Capacity
        private void ResetDataLists()
        {
            for (int i = 0; i < 16; i++)
            {
                // เคลียร์ก่อนถ้ามีข้อมูลเก่า
                _l1Columns[i]?.Clear();
                _l2Columns[i]?.Clear();
                _l6Columns[i]?.Clear();
                _l7Columns[i]?.Clear();
                _l1VoltColumns[i]?.Clear();
                _l2VoltColumns[i]?.Clear();
                _l6VoltColumns[i]?.Clear();
                _l7VoltColumns[i]?.Clear();

                //  สร้างใหม่ด้วย Capacity เพื่อป้องกัน resize
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

        private void ProcessCalibration(string[] hexData, int packetIndex)
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

                //  เช็ค cancellation เบาๆ ทุก 3 รอบ
                if (i % 3 == 0 && (_cts?.Token.IsCancellationRequested ?? false))
                    return;

                for (int j = 0; j < 16; j++)
                {
                    // L1
                    int l1Idx = offsetL1L2 + (j * 2);
                    int l1Val = ParseHexPair(hexData, l1Idx);
                    _l1Columns[j].Add(l1Val);
                    _l1VoltColumns[j].Add(l1Val * voltageScale);

                    // L2
                    int l2Idx = offsetL1L2 + 32 + (j * 2);
                    int l2Val = ParseHexPair(hexData, l2Idx);
                    _l2Columns[j].Add(l2Val);
                    _l2VoltColumns[j].Add(l2Val * voltageScale);

                    // L6
                    int l6Idx = offsetL6L7 + (j * 2);
                    int l6Val = ParseHexPair(hexData, l6Idx);
                    _l6Columns[j].Add(l6Val);
                    _l6VoltColumns[j].Add(l6Val * voltageScale);

                    // L7
                    int l7Idx = offsetL6L7 + 32 + (j * 2);
                    int l7Val = ParseHexPair(hexData, l7Idx);
                    _l7Columns[j].Add(l7Val);
                    _l7VoltColumns[j].Add(l7Val * voltageScale);
                }
            }
        }

        private int ParseHexPair(string[] hexData, int startIndex)
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

            // MathService implements both IMathService and IFittingService
            if (_mathService is not IFittingService fittingService) return;

            var window = new CalibrationDetailWindow(fittingService);

            // Sync with current calibration view settings
            string axisLabel = SelectedXAxisIndex == 1 ? "Voltage (mV)" : "ADC Channel";

            // Prepare colors (from VM properties)
            var figureBg = ToDrawingColor(GraphFigureColor, System.Drawing.Color.FromArgb(255, 30, 30, 30));
            var dataBg = ToDrawingColor(GraphDataColor, System.Drawing.Color.FromArgb(255, 37, 37, 38));
            var fgColor = ToDrawingColor(GraphTextColor, System.Drawing.Color.White);

            window.SetColorTheme(figureBg, dataBg, fgColor);

            // Setup and show
            var drawingColor = ToDrawingColor(GraphSeriesColor, System.Drawing.Color.Cyan);
            window.ShowHistogram(rawData, channel.Title, showFit: true, color: drawingColor, xLabel: axisLabel);
            window.Show();
        }

        private System.Drawing.Color ToDrawingColor(System.Windows.Media.Color wpfColor, System.Drawing.Color fallback)
        {
            try
            {
                return System.Drawing.Color.FromArgb(wpfColor.A, wpfColor.R, wpfColor.G, wpfColor.B);
            }
            catch { return fallback; }
        }

        //  เปลี่ยนเป็น async version
        partial void OnSelectedXAxisIndexChanged(int value) => _ = UpdatePlotsAsync();
        partial void OnSelectedLayerIndexChanged(int value) => _ = UpdatePlotsAsync();

        //  สร้าง async version ของ UpdatePlots
        private async Task UpdatePlotsAsync()
        {
            await Task.Run(() => UpdatePlots());
        }

        //  ปรับปรุง UpdatePlots ให้ batch UI updates
        private void UpdatePlots()
        {
            var sourceColumns = SelectedXAxisIndex == 1
               ? GetVoltageColumns(SelectedLayerIndex)
               : GetCalibrationColumns(SelectedLayerIndex);

            if (sourceColumns[0].Count == 0) return;

            int channelCount = 16;
            double xMax = SelectedXAxisIndex == 1 ? 5000 : 16384;
            double xMin = 0;
            string xLabel = SelectedXAxisIndex == 1 ? "Voltage (mV)" : "ADC Channel (0-16383)";

            //  เก็บผลลัพธ์ก่อน แล้วค่อย update UI ทีเดียว
            var plotResults = new (double[] counts, double[] binCenters, string statsText, int channel)[channelCount];

            Parallel.For(0, channelCount, ch =>
            {
                var columnData = sourceColumns[ch];
                var dataForChannel = columnData.Where(d => d > 0).ToArray();

                if (dataForChannel.Length > 0)
                {
                    var (counts, binEdges) = ScottPlot.Statistics.Common.Histogram(
                        dataForChannel, min: 0, max: xMax, binCount: 500);

                    double[] binCenters = new double[binEdges.Length - 1];
                    for (int k = 0; k < binCenters.Length; k++)
                    {
                        binCenters[k] = (binEdges[k] + binEdges[k + 1]) / 2.0;
                    }

                    plotResults[ch] = (counts, binCenters, $"Counts: {dataForChannel.Length:N0}", ch);
                }
            });

            //  Update UI ครั้งเดียวแทนที่จะเป็น 16 ครั้ง
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