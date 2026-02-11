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
using BaselineMode.WPF.Core.Interfaces.Observation;
using BaselineMode.WPF.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExcelDataReader;
using Microsoft.Win32;
// Removed unused OfficeOpenXml as we use IFileHelper now

namespace BaselineMode.WPF.Presentation.ViewModels
{
    public partial class CalibrationViewModel : SharedViewModelBase
    {
        private readonly IMathService _mathService;
        private readonly IFileHelper _fileHelper;
        private readonly IObservationDataProcessor _dataProcessor;

        public CalibrationViewModel(IMathService mathService, IFileHelper fileHelper, IObservationDataProcessor dataProcessor)
        {
            _mathService = mathService;
            _fileHelper = fileHelper;
            _dataProcessor = dataProcessor;

            Channels = new ObservableCollection<ChannelViewModel>();
            for (int i = 0; i < 16; i++)
            {
                Channels.Add(new ChannelViewModel { ChannelName = $"CH {i + 1}" });
            }
        }

        [ObservableProperty]
        private string _inputFilesInfo = "No files selected";

        [ObservableProperty]
        private string _outputFileName = "CalibrationResult";

        [ObservableProperty]
        private int _selectedLayerIndex = 0; // 0=L1, 1=L2, 2=L6, 3=L7

        [ObservableProperty]
        private int _selectedXAxisIndex = 0; // 0=ADC, 1=Voltage

        [ObservableProperty]
        private int _delayTime = 50;

        [ObservableProperty]
        private int _threshold = 50;

        [ObservableProperty]
        private string _headerCheckStatus = "";


        private CancellationTokenSource? _cts;

        // Data Storage
        private List<double[]> _calibrationL1List = new();
        private List<double[]> _calibrationL2List = new();
        private List<double[]> _calibrationL6List = new();
        private List<double[]> _calibrationL7List = new();

        private List<double[]> _calibrationL1VoltageList = new();
        private List<double[]> _calibrationL2VoltageList = new();
        private List<double[]> _calibrationL6VoltageList = new();
        private List<double[]> _calibrationL7VoltageList = new();

        public ObservableCollection<ChannelViewModel> Channels { get; }

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
                        // Clean whitespace (as per Form1 logic)
                        string cleanedData = BaselineMode.WPF.Core.Models.RegexPatterns.Whitespace().Replace(fileContent, "");

                        // Find E225 segments
                        var matches = BaselineMode.WPF.Core.Models.RegexPatterns.E225Header().Matches(cleanedData);

                        foreach (Match match in matches)
                        {
                            string segment = match.Value;
                            int segmentLength = segment.Length;

                            // Split into 4128 char chunks
                            for (int i = 0; i < segmentLength; i += 4128)
                            {
                                int length = Math.Min(4128, segmentLength - i);
                                string chunk = segment.Substring(i, length);
                                filteredSegments.Add(chunk);
                            }
                        }

                        processedFiles++;
                        double progress = (double)processedFiles / InputFileList.Length * 50; // First 50% is reading
                        Application.Current.Dispatcher.Invoke(() => ProgressValue = progress);
                    }

                    // Validate last segment length
                    if (filteredSegments.Count > 0 && filteredSegments.Last().Length < 4128)
                    {
                        filteredSegments.RemoveAt(filteredSegments.Count - 1);
                    }

                    if (filteredSegments.Count > 0)
                    {
                        // Use FileHelper to save. Passing "Source" as subfolder to maintain legacy organization if desired.
                        // Or we could let it go to default output folder.
                        // Let's use "Source" to match previous behavior for now.
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            _fileHelper.SaveToExcel(filteredSegments, outputName, "Source");
                        });
                    }
                    else
                    {
                        Application.Current.Dispatcher.Invoke(() => StatusMessage = "No valid segments found.");
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

            // Use FileHelper to finding the file in possible locations
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
                    using (var stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var reader = ExcelReaderFactory.CreateReader(stream))
                    {
                        var result = reader.AsDataSet();
                        var table = result.Tables[0];
                        int totalRows = table.Rows.Count;

                        for (int i = 0; i < totalRows; i++)
                        {
                            if (_cts.Token.IsCancellationRequested) break;

                            var row = table.Rows[i];
                            string hexString = row[0].ToString() ?? "";

                            // Use DataProcessor for hex splitting
                            string[] hexData = _dataProcessor.SplitHexData(hexString);

                            // Header Check on first row
                            if (i == 0)
                            {
                                bool isHeaderValid = _dataProcessor.ValidateHeader(hexData);
                                Application.Current.Dispatcher.Invoke(() =>
                                    HeaderCheckStatus = isHeaderValid ? "Checksum OK" : "Checksum Mismatch");
                            }

                            ProcessCalibration(hexData, i);

                            if (i % 100 == 0)
                            {
                                double progress = (double)i / totalRows * 100;
                                Application.Current.Dispatcher.Invoke(() => ProgressValue = progress);
                            }
                        }
                    }
                }, _cts.Token);

                StatusMessage = "Data read complete. Updating plots...";
                UpdatePlots();
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

        private void ResetDataLists()
        {
            _calibrationL1List.Clear();
            _calibrationL2List.Clear();
            _calibrationL6List.Clear();
            _calibrationL7List.Clear();

            _calibrationL1VoltageList.Clear();
            _calibrationL2VoltageList.Clear();
            _calibrationL6VoltageList.Clear();
            _calibrationL7VoltageList.Clear();
        }



        private void ProcessCalibration(string[] hexData, int packetIndex)
        {
            if (hexData.Length < 18) return;

            for (int i = 0; i < 11; i++)
            {
                int offsetL1L2 = 18 + 64 * i;
                int offsetL6L7 = 722 + 64 * i;

                if (offsetL1L2 + 64 > hexData.Length || offsetL6L7 + 64 > hexData.Length) continue;

                var l1l2Data = hexData.Skip(offsetL1L2).Take(64).ToArray();
                var l6l7Data = hexData.Skip(offsetL6L7).Take(64).ToArray();

                ProcessCalibrationDataSegment(l1l2Data, l6l7Data);
            }
        }

        private void ProcessCalibrationDataSegment(string[] l1l2Data, string[] l6l7Data)
        {
            double[] calL1 = new double[16];
            double[] calL2 = new double[16];
            double[] calL6 = new double[16];
            double[] calL7 = new double[16];

            double[] voltL1 = new double[16];
            double[] voltL2 = new double[16];
            double[] voltL6 = new double[16];
            double[] voltL7 = new double[16];

            var l1l2Dec = l1l2Data.Select(h => Convert.ToInt32(h, 16)).ToArray();
            var l6l7Dec = l6l7Data.Select(h => Convert.ToInt32(h, 16)).ToArray();

            for (int j = 0; j < 16; j++)
            {
                // L1: Bytes 0-31 of l1l2Data (j*2, j*2+1)
                calL1[j] = (l1l2Dec[j * 2] << 8) + l1l2Dec[j * 2 + 1];
                voltL1[j] = ((calL1[j] / 16383.0) * 5.0) * 1000.0;

                // L2: Bytes 32-63 of l1l2Data (j*2+32, j*2+1+32)
                calL2[j] = (l1l2Dec[j * 2 + 32] << 8) + l1l2Dec[j * 2 + 1 + 32];
                voltL2[j] = ((calL2[j] / 16383.0) * 5.0) * 1000.0;

                // L6: Bytes 0-31 of l6l7Data
                calL6[j] = (l6l7Dec[j * 2] << 8) + l6l7Dec[j * 2 + 1];
                voltL6[j] = ((calL6[j] / 16383.0) * 5.0) * 1000.0;

                // L7: Bytes 32-63 of l6l7Data
                calL7[j] = (l6l7Dec[j * 2 + 32] << 8) + l6l7Dec[j * 2 + 1 + 32];
                voltL7[j] = ((calL7[j] / 16383.0) * 5.0) * 1000.0;
            }

            _calibrationL1List.Add(calL1);
            _calibrationL2List.Add(calL2);
            _calibrationL6List.Add(calL6);
            _calibrationL7List.Add(calL7);

            _calibrationL1VoltageList.Add(voltL1);
            _calibrationL2VoltageList.Add(voltL2);
            _calibrationL6VoltageList.Add(voltL6);
            _calibrationL7VoltageList.Add(voltL7);
        }

        private void UpdatePlots()
        {
            List<double[]> sourceList = SelectedXAxisIndex == 1
               ? GetVoltageList(SelectedLayerIndex)
               : GetCalibrationList(SelectedLayerIndex);

            if (sourceList.Count == 0) return;

            int channelCount = 16;
            double xMax = SelectedXAxisIndex == 1 ? 5000 : 16384;

            Parallel.For(0, channelCount, ch =>
            {
                var dataForChannel = sourceList.Select(row => row[ch]).Where(d => d > 0).ToArray();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (dataForChannel.Length > 0)
                    {
                        var (counts, binEdges) = ScottPlot.Statistics.Common.Histogram(dataForChannel, min: 0, max: xMax, binCount: 500);

                        double[] binCenters = new double[binEdges.Length - 1];
                        for (int k = 0; k < binCenters.Length; k++)
                        {
                            binCenters[k] = (binEdges[k] + binEdges[k + 1]) / 2.0;
                        }

                        var channelVM = Channels[ch];
                        channelVM.Counts = counts;
                        channelVM.BinCenters = binCenters;
                        channelVM.StatsText = $"Counts: {dataForChannel.Length}";

                        channelVM.RenderPlot(
                             Color.FromArgb(30, 30, 30),
                             Color.FromArgb(37, 37, 38),
                             Color.White,
                             Color.Cyan
                        );
                    }
                });
            });
        }

        private List<double[]> GetCalibrationList(int layerIndex) => layerIndex switch
        {
            0 => _calibrationL1List,
            1 => _calibrationL2List,
            2 => _calibrationL6List,
            3 => _calibrationL7List,
            _ => _calibrationL1List
        };

        private List<double[]> GetVoltageList(int layerIndex) => layerIndex switch
        {
            0 => _calibrationL1VoltageList,
            1 => _calibrationL2VoltageList,
            2 => _calibrationL6VoltageList,
            3 => _calibrationL7VoltageList,
            _ => _calibrationL1VoltageList
        };
    }
}
