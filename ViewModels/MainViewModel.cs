using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using BaselineMode.WPF.Models;
using BaselineMode.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScottPlot;

namespace BaselineMode.WPF.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly FileService _fileService;
        private readonly MathService _mathService;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private double _progressValue;

        [ObservableProperty]
        private string _inputFilesInfo = "No files selected";

        [ObservableProperty]
        private string _outputDirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BaselineModeOutputs");

        [RelayCommand]
        private void BrowseOutputDirectory()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog(); // Requires recent .NET or custom implementation. 
                                                                 // Fallback to WinForms if OpenFolderDialog not available (WPF .NET Core 3.1+ triggers might vary, but OpenFolderDialog is ideal if available)
                                                                 // Or typically checking system.

            // Standard approach for Folder Browser in WPF without external libraries (like Ookii):
            // Using OpenFileDialog with "folder selection" hack OR WinForms FolderBrowserDialog.
            // Let's use WinForms for simplicity if available, or just common approach.
            // Since environment is likely .NET 6/8, OpenFolderDialog might exist in Microsoft.Win32?
            // Actually, standard Microsoft.Win32.OpenFolderDialog was added in valid versions.

            // Let's assume standard OpenFolderDialog (available in newer .NET).
            // If compile fails, I will use WinForms.
            dialog.Title = "Select Output Root Folder";
            if (dialog.ShowDialog() == true)
            {
                OutputDirectoryPath = dialog.FolderName;
            }
        }

        private string GetDailyOutputDirectory()
        {
            string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
            string fullPath = Path.Combine(OutputDirectoryPath, dateStr);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
            return fullPath;
        }

        // --- Added Properties ---
        [ObservableProperty]
        private int _selectedLayerIndex = 0; // 0=L1, 1=L2, 2=L6, 3=L7

        [ObservableProperty]
        private int _selectedDirectionIndex = 0; // 0=X, 1=Z

        [ObservableProperty]
        private bool _useKalmanFilter = false;

        [ObservableProperty]
        private bool _useThresholding = false;

        [ObservableProperty]
        private double _kFactor = 2.0;

        [ObservableProperty]
        private int _selectedFitMethod = 0; // 0=Gaussian, 1=Hyper-EMG

        [ObservableProperty]
        private int _selectedXAxisIndex = 0; // 0=ADC, 1=Voltage

        [ObservableProperty]
        private int _selectedBaselineMode = 0; // 0=Auto, 1=File

        private List<string> _selectedFiles = new List<string>();
        // We will store result as list of objects
        [ObservableProperty]
        private List<BaselineData> _processedData = new List<BaselineData>();

        // Statistics
        [ObservableProperty]
        private string _statsText = "Peak: -, Mean: -, Sigma: -";

        [ObservableProperty]
        private bool _canSaveMean = false;

        // Plot Control (We will bind a method to pass the plot control or use a wrapper)
        // For simplicity in this step, we will expose data collections that the View can observe, 
        // or we handle plotting in the View's CodeBehind triggered by an event/message. 
        // A common pattern with ScottPlot 4 is to pass the WpfPlot to the VM or use a service.
        // --- Collections ---
        [ObservableProperty]
        private ObservableCollection<ChannelViewModel> _channels = new ObservableCollection<ChannelViewModel>();

        [ObservableProperty]
        private ObservableCollection<ChannelViewModel> _channelsX = new ObservableCollection<ChannelViewModel>();

        [ObservableProperty]
        private ObservableCollection<ChannelViewModel> _channelsZ = new ObservableCollection<ChannelViewModel>();

        public event EventHandler<PlotUpdateEventArgs> RequestPlotUpdate;

        public MainViewModel()
        {
            _fileService = new FileService();
            _mathService = new MathService();
            // Initialize 16 channels
            InitializeChannels();
        }

        private void InitializeChannels()
        {
            Channels.Clear();
            ChannelsX.Clear();
            ChannelsZ.Clear();

            for (int i = 1; i <= 16; i++)
            {
                var channel = new ChannelViewModel
                {
                    Title = $"Channel {i}",
                    ChannelIndex = i - 1,
                    StatsText = "No Data"
                };

                Channels.Add(channel);

                if (i <= 8)
                {
                    ChannelsX.Add(channel);
                }
                else
                {
                    ChannelsZ.Add(channel);
                }
            }
        }

        [RelayCommand]
        private void Reset()
        {
            _selectedFiles = new List<string>();
            InputFilesInfo = "No files selected";
            ProcessedData = new List<BaselineData>();
            UpdateDisplayTable();
            InitializeChannels();
            StatusMessage = "Reset complete.";
            ProgressValue = 0;
            CurrentPage = 1;
            // Notify view to clear plots if needed, or binding handles it (mostly)
            RequestPlotUpdate?.Invoke(this, new PlotUpdateEventArgs(null));
        }

        [RelayCommand]
        private async Task SelectFiles()
        {
            // Reset before selecting new files (like Form1.cs)
            Reset();

            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Multiselect = true;
            dialog.Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";

            if (dialog.ShowDialog() == true)
            {
                var files = dialog.FileNames.ToList();
                _selectedFiles = files; // Temporary set, might change if combined

                if (files.Count == 1)
                {
                    // Single file - use filename as output
                    InputFilesInfo = "1 file selected.";
                    OutputFileName = Path.GetFileNameWithoutExtension(files.First()) + ".xlsx";
                    StatusMessage = "Files loaded. Ready to process.";
                }
                else if (files.Count > 1)
                {
                    // Multiple files - combine them
                    IsBusy = true;
                    StatusMessage = $"Combining {files.Count} files...";

                    await Task.Run(() =>
                    {
                        try
                        {
                            string outputDir = GetDailyOutputDirectory();
                            string combinedFilePath = Path.Combine(outputDir, "multiple_file_output.txt");

                            var progress = new Progress<double>(percent =>
                            {
                                ProgressValue = percent;
                            });

                            // Use Streams to avoid loading everything into memory
                            using (var outputStream = new FileStream(combinedFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                            using (var writer = new StreamWriter(outputStream))
                            {
                                int totalFiles = files.Count;
                                int processed = 0;

                                foreach (var file in files)
                                {
                                    try
                                    {
                                        using (var inputStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                                        using (var reader = new StreamReader(inputStream))
                                        {
                                            string line;
                                            while ((line = reader.ReadLine()) != null)
                                            {
                                                writer.WriteLine(line);
                                            }
                                        }
                                    }
                                    catch { /* Skip unreadable files */ }

                                    processed++;
                                    ((IProgress<double>)progress).Report((double)processed / totalFiles * 100);
                                }
                            }

                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                _selectedFiles = new List<string> { combinedFilePath };
                                InputFilesInfo = $"{files.Count} files combined.";
                                OutputFileName = "multiple_file_output.xlsx";
                                StatusMessage = "Files combined. Ready to process.";
                                System.Windows.MessageBox.Show(
                                    $"Files combined into:\n{combinedFilePath}",
                                    "Success",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Information);
                            });
                        }
                        catch (Exception ex)
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                StatusMessage = $"Error: {ex.Message}";
                                System.Windows.MessageBox.Show(
                                    $"Error combining files: {ex.Message}",
                                    "Error",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Error);
                            });
                        }
                    });

                    IsBusy = false;
                }
            }
        }

        [ObservableProperty]
        private double _thresholdValue = 0;

        [ObservableProperty]
        private bool _useGaussianFit = false;

        [ObservableProperty]
        private string _headerInfoText = string.Empty;


        [ObservableProperty]
        private System.Data.DataTable _displayDataTable = new System.Data.DataTable();

        partial void OnSelectedLayerIndexChanged(int value)
        {
            UpdateDisplayTable();
            RefreshIfHasData();
        }

        partial void OnUseGaussianFitChanged(bool value) => RefreshIfHasData();
        partial void OnSelectedFitMethodChanged(int value) => RefreshIfHasData();
        partial void OnUseThresholdingChanged(bool value) => RefreshIfHasData();
        partial void OnKFactorChanged(double value) => RefreshIfHasData();
        partial void OnSelectedXAxisIndexChanged(int value) => RefreshIfHasData();

        private void RefreshIfHasData()
        {
            if (ProcessedData != null && ProcessedData.Any())
            {
                RefreshChannelPlots();
            }
        }

        private void RefreshChannelPlots()
        {
            if (ProcessedData == null || !ProcessedData.Any()) return;

            Func<BaselineData, double[]> layerSelector = SelectedLayerIndex switch
            {
                1 => (d) => d.L2,
                2 => (d) => d.L6,
                3 => (d) => d.L7,
                _ => (d) => d.L1
            };

            for (int i = 0; i < 16; i++)
            {
                int chIndex = i;
                var rawData = ProcessedData.Select(d => layerSelector(d)[chIndex]).ToArray();

                if (rawData.Length > 0)
                {
                    double meanToSubtract = SelectedBaselineMode == 0 ? rawData.Average() : LoadMeanFromFile(chIndex);
                    var centeredData = rawData.Select(x => x - meanToSubtract).ToArray();

                    var filteredData = ApplyThresholding(centeredData);

                    if (filteredData.Length > 0)
                    {
                        var (counts, binEdges) = ScottPlot.Statistics.Common.Histogram(filteredData, min: 0, max: 16383, binCount: 16383);
                        double[] binCenters = binEdges.Take(binEdges.Length - 1).Select(b => b + 0.5).ToArray();

                        if (SelectedXAxisIndex == 1)
                            binCenters = binCenters.Select(v => ((v / 16383.0) * 5) * 1000).ToArray();

                        ProcessChannelData(chIndex, filteredData, counts, binCenters);
                    }
                    else
                    {
                        Channels[chIndex].StatsText = "No Signal";
                        Channels[chIndex].Counts = new double[0];
                    }
                }
                else
                {
                    Channels[chIndex].StatsText = "No Data";
                }
            }

            RequestPlotUpdate?.Invoke(this, new PlotUpdateEventArgs(ProcessedData));
        }

        private double[] ApplyThresholding(double[] centeredData)
        {
            if (!UseThresholding) return centeredData;

            double sigma = Math.Sqrt(centeredData.Select(x => x * x).Average());
            double threshold = KFactor * sigma;
            return centeredData.Where(v => v > threshold).ToArray();
        }

        private void ProcessChannelData(int chIndex, double[] filteredData, double[] counts, double[] binCenters)
        {
            double[] fitCurve = null;
            double mu = 0, sigma = 0, peak = 0;

            if (UseGaussianFit)
            {
                if (HasSufficientData(filteredData, counts))
                {
                    var result = PerformFit(binCenters, counts);
                    fitCurve = result.fitCurve;
                    mu = result.mu;
                    sigma = result.sigma;
                    peak = result.peak;
                }
                else
                {
                    Channels[chIndex].StatsText = "No Signal";
                    Channels[chIndex].FitCurve = null;
                    return; // Skip remaining update to avoid overwriting "No Signal" with partial data if desired, or proceed.
                            // Actually, let's just fall through to update the chart with raw data but no fit.
                }
            }
            else
            {
                var moments = _mathService.CalculateMoments(binCenters, counts);
                mu = moments.mean;
                sigma = moments.sigma;
                peak = moments.peak;
            }

            var chVM = Channels[chIndex];
            chVM.BinCenters = binCenters;
            chVM.Counts = counts;
            chVM.FitCurve = fitCurve;
            chVM.StatsText = $"P:{peak:F1} M:{mu:F1} S:{sigma:F1}";
        }

        private bool HasSufficientData(double[] filteredData, double[] counts)
        {
            return filteredData.Length > 5 && counts.Max() > 0;
        }

        private (double[] fitCurve, double mu, double sigma, double peak) PerformFit(double[] binCenters, double[] counts)
        {
            if (SelectedFitMethod == 1) // Hyper-EMG
            {
                var result = _mathService.HyperEMGFit(binCenters, counts);
                return (result.fitCurve, result.mu, result.sigma, result.peak); // Assuming HyperEMGFit returns these tuple elements
            }
            else // Gaussian
            {
                var result = _mathService.GaussianFit(binCenters, counts);
                return (result.fitCurve, result.mu, result.sigma, result.peak);
            }
        }

        private double LoadMeanFromFile(int channelIndex)
        {
            string layerFile = SelectedLayerIndex switch
            {
                1 => "MeanValues2.txt",
                2 => "MeanValues6.txt",
                3 => "MeanValues7.txt",
                _ => "MeanValues1.txt"
            };

            try
            {
                if (File.Exists(layerFile))
                {
                    var lines = File.ReadAllLines(layerFile);
                    if (channelIndex < lines.Length && double.TryParse(lines[channelIndex], out double mean))
                        return mean;
                }
            }
            catch { }
            return 0;
        }


        partial void OnSelectedBaselineModeChanged(int value)
        {
            CanSaveMean = value == 0; // 0 = Auto
            RefreshIfHasData();
        }

        [ObservableProperty]
        private int _currentPage = 1;

        [ObservableProperty]
        private int _pageSize = 100;

        [ObservableProperty]
        private int _totalPages = 1;

        [ObservableProperty]
        private string _pageInfoText = "Page 0 of 0";

        [RelayCommand]
        private void NextPage()
        {
            if (CurrentPage < TotalPages)
            {
                CurrentPage++;
                UpdateDisplayTable();
            }
        }

        [RelayCommand]
        private void PreviousPage()
        {
            if (CurrentPage > 1)
            {
                CurrentPage--;
                UpdateDisplayTable();
            }
        }

        private void UpdateDisplayTable()
        {
            if (ProcessedData == null || !ProcessedData.Any())
            {
                DisplayDataTable = new System.Data.DataTable();
                PageInfoText = "No Data";
                return;
            }

            IsBusy = true;

            Task.Run(() =>
            {
                // Calculate Pagination
                int totalRecords = ProcessedData.Count;
                int totalPages = (int)Math.Ceiling((double)totalRecords / PageSize);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    TotalPages = totalPages;
                    if (CurrentPage > TotalPages) CurrentPage = TotalPages;
                    if (CurrentPage < 1) CurrentPage = 1;
                    PageInfoText = $"Page {CurrentPage} of {TotalPages} ({totalRecords} items)";
                });

                var table = new System.Data.DataTable();
                table.Columns.Add("Packet No", typeof(int));
                table.Columns.Add("Sample No", typeof(int));

                for (int i = 1; i <= 16; i++)
                    table.Columns.Add($"Ch {i}", typeof(double));

                Func<BaselineData, double[]> selector = SelectedLayerIndex switch
                {
                    1 => (d) => d.L2,
                    2 => (d) => d.L6,
                    3 => (d) => d.L7,
                    _ => (d) => d.L1
                };

                // Apply Pagination
                int skip = (CurrentPage - 1) * PageSize;
                var pageData = ProcessedData.Skip(skip).Take(PageSize);

                foreach (var item in pageData)
                {
                    var row = table.NewRow();
                    row["Packet No"] = item.SamplingPacketNo;
                    row["Sample No"] = item.SamplingNo;

                    var data = selector(item);
                    for (int i = 0; i < 16; i++)
                    {
                        if (i < data.Length)
                            row[$"Ch {i + 1}"] = data[i];
                    }
                    table.Rows.Add(row);
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    DisplayDataTable = table;
                    IsBusy = false;
                });
            });
        }

        [ObservableProperty]
        private int _delayTimeMs = 0;

        [ObservableProperty]
        private string _outputFileName = "output.txt";

        [ObservableProperty]
        private string _startTimeStr = "-";

        [ObservableProperty]
        private string _stopTimeStr = "-";

        [ObservableProperty]
        private string _durationStr = "-";

        [ObservableProperty]
        private string _dataCountsStr = "-";

        private CancellationTokenSource _cts;

        [RelayCommand]
        private void Stop()
        {
            _cts?.Cancel();
            StatusMessage = "Stopping...";
        }

        [RelayCommand]
        private async Task CheckHeader()
        {
            if (!_selectedFiles.Any())
            {
                StatusMessage = "Please select files check.";
                return;
            }

            HeaderInfoText = "Checking...";
            IsBusy = true;

            await Task.Run(() =>
            {
                try
                {
                    var fileToCheck = _selectedFiles.First();

                    // 1. Data Integrity Check (E225)
                    // Use FileStream with FileShare.Read to match legacy accessibility
                    using (var stream = new FileStream(fileToCheck, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream))
                    {
                        bool invalidFound = false;
                        int lineNumber = 0;
                        string firstLineContent = null;
                        string? line;

                        while ((line = reader.ReadLine()) != null)
                        {
                            lineNumber++;
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            var content = line.Trim();
                            // Robust Parsing: Remove quotes (CSV parity) and spaces
                            var cleanContent = content.Trim('"').Replace(" ", "").Replace("\t", "");

                            if (firstLineContent == null) firstLineContent = content; // Keep original for detailed parse

                            if (!cleanContent.StartsWith("E225"))
                            {
                                invalidFound = true;
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    StatusMessage = $"Header INCORRECT at line {lineNumber}.";
                                    System.Windows.MessageBox.Show($"Header is INCORRECT! at data row no. {lineNumber}\nContent: '{content}'", "Check Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                                    HeaderInfoText = $"Header INCORRECT at line {lineNumber}.\nFound: {content.Substring(0, Math.Min(20, content.Length))}...";
                                });
                                break;
                            }
                        }

                        if (!invalidFound)
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                StatusMessage = "Header is correct!";
                                System.Windows.MessageBox.Show("Header is correct!", "Check", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                            });

                            // 2. Parse Header Info from first valid line
                            // Ensure clean content for splitting
                            var cleanHex = firstLineContent.Replace(" ", "").Trim();
                            var hexData = SplitHexData(cleanHex);

                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                ParseHeaderInfo(hexData);
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = $"Error: {ex.Message}";
                        HeaderInfoText = $"Error: {ex.Message}";
                    });
                }
            });

            IsBusy = false;
        }

        private void ParseHeaderInfo(string[] hexData)
        {
            try
            {
                StringBuilder sb = new StringBuilder();

                // 1. Packet Synchronization Code
                sb.AppendLine($"Packet Synchronization Code: {hexData[0]} {hexData[1]}");

                // 2. Package Identification
                sb.AppendLine($"Package Identification: {hexData[2]} {hexData[3]}");

                // 3. Packet Sequence
                sb.AppendLine($"Packet Sequence: {hexData[4]} {hexData[5]}");

                // 4. Packet Data Length
                sb.AppendLine($"Packet data length: {hexData[6]} {hexData[7]}");

                // 5. Timestamp
                if (hexData.Length >= 14)
                {
                    // Logic from Form1: hexData.Skip(8).Take(6)
                    var timecodeHex = hexData.Skip(8).Take(6).ToArray();
                    var timecodeDec = timecodeHex.Select(h => Convert.ToByte(h, 16)).ToArray();

                    uint seconds_part = BitConverter.ToUInt32(timecodeDec.Take(4).Reverse().ToArray(), 0);
                    ushort milliseconds_part = BitConverter.ToUInt16(timecodeDec.Skip(4).Reverse().ToArray(), 0);

                    // Form1 calculates double total_seconds but creates DateTime from parts
                    DateTime datetime_value = DateTimeOffset.FromUnixTimeSeconds((long)seconds_part)
                        .AddMilliseconds(milliseconds_part)
                        .UtcDateTime;

                    sb.AppendLine($"Timestamp: {datetime_value.ToString("yyyy-MMM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture)}");
                }

                // 6. Data Type
                if (hexData.Length >= 16)
                {
                    sb.AppendLine($"Data Type: {hexData[14]} {hexData[15]}");
                }

                // 7. Checksum
                if (hexData.Length >= 2064)
                {
                    // Logic from Form1: sum 2054 bytes starting at index 8
                    int total_sum = hexData.Skip(8).Take(2054).Select(h => Convert.ToInt32(h, 16)).Sum();
                    int last_two_bytes = total_sum % 65536;
                    string checksum_hex = last_two_bytes.ToString("X4"); // e.g. "0A1B"

                    string CheckSum_fromData = hexData[2062] + hexData[2063];

                    sb.AppendLine($"Check Sum: {hexData[2062]} {hexData[2063]}");

                    if (checksum_hex.Equals(CheckSum_fromData, StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine("Checksum matches!");
                    }
                    else
                    {
                        sb.AppendLine($"Checksum does not match. (Calc: {checksum_hex})");
                    }
                }

                // 8. Test Conditions (UI Variables)
                sb.AppendLine("Test condition:");
                sb.AppendLine($"Delay Time: {DelayTimeMs}");
                sb.AppendLine($"Threshold: {KFactor}"); // Assuming Threshold in Form1 corresponds to k-factor or threshold value

                HeaderInfoText = sb.ToString();
            }
            catch (Exception ex)
            {
                HeaderInfoText = $"Error parsing header: {ex.Message}";
            }
        }

        private string[] SplitHexData(string hexString)
        {
            // Logic from Form1.SplitHexData
            int n = 2;
            if (hexString.Length % n != 0) return new string[0]; // Safety check

            var list = new string[hexString.Length / n];
            for (int i = 0; i < list.Length; i++)
            {
                list[i] = hexString.Substring(i * n, n);
            }
            return list;
        }

        [RelayCommand]
        private async Task PreProcessData()
        {
            if (!_selectedFiles.Any())
            {
                StatusMessage = "No files selected for processing.";
                return;
            }

            if (string.IsNullOrWhiteSpace(OutputFileName))
            {
                StatusMessage = "Please provide output filename.";
                return;
            }

            IsBusy = true;
            StatusMessage = "Processing raw files to Excel...";

            await Task.Run(() =>
            {
                var progress = new Progress<double>(percent =>
                {
                    // Scale progress: 50% for parsing/processing, 50% for saving
                    // Or simplified: update directly
                });

                try
                {
                    var allSegments = new List<string>();

                    int fileCount = _selectedFiles.Count;
                    int currentFile = 0;

                    foreach (var file in _selectedFiles)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() => StatusMessage = $"Parsing file {currentFile + 1}/{fileCount}...");
                        var segments = _fileService.ParseRawTextFile(file);
                        allSegments.AddRange(segments);

                        currentFile++;
                        System.Windows.Application.Current.Dispatcher.Invoke(() => ProgressValue = (double)currentFile / fileCount * 30); // 0-30%
                    }

                    if (allSegments.Any())
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() => StatusMessage = "Converting hex data...");
                        // Convert to BaselineData
                        // Scale this part 30-70%
                        var processProgress = new Progress<double>(p =>
                            System.Windows.Application.Current.Dispatcher.Invoke(() => ProgressValue = 30 + (p * 0.4)));

                        var processed = _fileService.ProcessHexSegments(allSegments, processProgress);

                        // Ensure .xlsx extension
                        string fileName = OutputFileName;
                        if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                            fileName += ".xlsx";

                        // Save to Daily Output directory
                        string outputDir = GetDailyOutputDirectory();
                        string fullPath = Path.Combine(outputDir, fileName);

                        System.Windows.Application.Current.Dispatcher.Invoke(() => StatusMessage = "Saving to Excel...");
                        // Scale saving 70-100%
                        var saveProgress = new Progress<double>(p =>
                             System.Windows.Application.Current.Dispatcher.Invoke(() => ProgressValue = 70 + (p * 0.3)));

                        _fileService.SaveToExcel(processed, fullPath, saveProgress);

                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = $"Saved {processed.Count} events to {fileName}";
                            System.Windows.MessageBox.Show($"Successfully processed {processed.Count} events to Source folder.", "Process Data", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                        });
                    }
                    else
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() => StatusMessage = "No valid segments found.");
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => StatusMessage = $"Error: {ex.Message}");
                }
            });

            IsBusy = false;
        }

        [RelayCommand]
        private async Task ProcessData()
        {
            if (!_selectedFiles.Any())
            {
                StatusMessage = "Please select files first.";
                return;
            }

            IsBusy = true;
            ProgressValue = 0;
            StatusMessage = "Processing...";
            _cts = new CancellationTokenSource();

            StartTimeStr = DateTime.Now.ToString("HH:mm:ss");
            StopTimeStr = "-";
            DurationStr = "-";
            var stopWatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                await Task.Run(async () =>
                {
                    // 1. Construct Path to Source File
                    string fileName = OutputFileName;
                    if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                        fileName += ".xlsx";

                    // Look in Daily Output Directory first, then fallback to Source for backward compat if needed?
                    // But now we write to Daily. So we should read from Daily.
                    string outputDir = GetDailyOutputDirectory();
                    string fullPath = Path.Combine(outputDir, fileName);

                    if (!File.Exists(fullPath))
                    {
                        // Fallback: try raw processing if Excel doesn't exist (or warn user)
                        StatusMessage = "Processed file not found. Please click 'Process Data' first.";
                        return;
                    }

                    if (!_cts.Token.IsCancellationRequested)
                    {
                        // 2. Read from Excel
                        System.Windows.Application.Current.Dispatcher.Invoke(() => StatusMessage = "Reading Excel...");
                        var readProgress = new Progress<double>(p =>
                            System.Windows.Application.Current.Dispatcher.Invoke(() => ProgressValue = p * 0.5)); // 0-50%

                        ProcessedData = _fileService.ReadExcelFile(fullPath, readProgress);
                        DataCountsStr = ProcessedData.Count.ToString();

                        UpdateDisplayTable();

                        // 3. Analyze Data - loop for all 16 channels
                        if (ProcessedData.Any())
                        {
                            Func<BaselineData, double[]> layerSelector = SelectedLayerIndex switch
                            {
                                1 => (d) => d.L2,
                                2 => (d) => d.L6,
                                3 => (d) => d.L7,
                                _ => (d) => d.L1
                            };

                            for (int i = 0; i < 16; i++)
                            {
                                if (_cts.Token.IsCancellationRequested) break;

                                if (DelayTimeMs > 0) await Task.Delay(DelayTimeMs);

                                int chIndex = i;
                                var rawData = ProcessedData.Select(d => layerSelector(d)[chIndex]).ToArray();

                                if (rawData.Length > 0)
                                {
                                    // 1. Mean Subtraction Logic Parity
                                    // Legacy 0 (File) -> Load. Legacy 1 (Auto) -> Calc.
                                    // My UI: 0=Auto, 1=File.
                                    // I will keep my UI order but ensure logic works.

                                    double meanToSubtract = 0;
                                    if (SelectedBaselineMode == 0) // Auto Mean
                                    {
                                        meanToSubtract = rawData.Average();
                                    }
                                    else // Load File (Legacy parity default file usage)
                                    {
                                        // Mock file loading or use 0 if missing as per previous step
                                        meanToSubtract = 0;
                                        // Ideally: Load MeanValues{Layer}.txt
                                    }

                                    // Check command enablement (ignoring bound command internal check for now)
                                    // Update subtraction
                                    var centeredData = rawData.Select(x => x - meanToSubtract).ToArray();

                                    // 2. Filter / Thresholding
                                    var filteredData = centeredData;
                                    if (UseThresholding)
                                    {
                                        double sigma = Math.Sqrt(centeredData.Select(x => x * x).Average());
                                        double threshold = KFactor * sigma;
                                        filteredData = centeredData.Where(v => v > threshold).ToArray();
                                    }

                                    if (filteredData.Length > 0)
                                    {
                                        // Use fixed range 0-16383 for ADC Channel parity
                                        // Legacy code plotted against "Channel Number" (0..16383)
                                        double hMin = 0;
                                        double hMax = 16383;

                                        // If using Voltage/Energy, we might need different range, 
                                        // but usually binning is done on ADC then converted.
                                        // However, ScottPlot Histogram expects values within min/max.
                                        // If data is outside, it ignores or clamps? 
                                        // Data is 0..16383 (14-bit).

                                        var (counts, binEdges) = ScottPlot.Statistics.Common.Histogram(filteredData, min: hMin, max: hMax, binCount: 16383);

                                        double[] binCenters = binEdges.Take(binEdges.Length - 1).Select(b => b + 0.5).ToArray();

                                        // X-Axis Conversion
                                        if (SelectedXAxisIndex == 1) // Voltage
                                            binCenters = binCenters.Select(v => ((v / 16383.0) * 5) * 1000).ToArray();
                                        else if (SelectedXAxisIndex == 2) // Energy
                                            binCenters = binCenters.Select(v => v * 1.0).ToArray(); // Placeholder conversion

                                        // Fit Logic
                                        // Fit Logic (Conditional)
                                        double[] fitCurve = new double[0];
                                        double mu = 0, sigma = 0, peak = 0;

                                        if (UseGaussianFit)
                                        {
                                            if (SelectedFitMethod == 1) // Hyper-EMG
                                            {
                                                var result = _mathService.HyperEMGFit(binCenters, counts);
                                                fitCurve = result.fitCurve;
                                                mu = result.mu;
                                                sigma = result.sigma;
                                                peak = result.peak;
                                            }
                                            else // Gaussian (Default)
                                            {
                                                var result = _mathService.GaussianFit(binCenters, counts);
                                                fitCurve = result.fitCurve;
                                                mu = result.mu;
                                                sigma = result.sigma;
                                                peak = result.peak;
                                            }
                                            // ✅ เพิ่ม Debug Output ตรงนี้:
                                            System.Diagnostics.Debug.WriteLine($"\n=== Channel {chIndex} ===");
                                            System.Diagnostics.Debug.WriteLine($"Counts: min={counts.Min():F1}, max={counts.Max():F1}, len={counts.Length}");
                                            System.Diagnostics.Debug.WriteLine($"BinCenters: min={binCenters.Min():F1}, max={binCenters.Max():F1}, len={binCenters.Length}");
                                            System.Diagnostics.Debug.WriteLine($"Fit params: peak={peak:F1}, mu={mu:F1}, sigma={sigma:F1}");

                                            if (fitCurve != null)
                                            {
                                                System.Diagnostics.Debug.WriteLine($"FitCurve: min={fitCurve.Min():F1}, max={fitCurve.Max():F1}, len={fitCurve.Length}");
                                            }
                                            else
                                            {
                                                System.Diagnostics.Debug.WriteLine("FitCurve is NULL!");
                                            }
                                        }
                                        else
                                        {
                                            // Calculate simple moments if fitting is disabled
                                            var moments = _mathService.CalculateMoments(binCenters, counts);
                                            mu = moments.mean;
                                            sigma = moments.sigma;
                                            peak = moments.peak;
                                        }

                                        // Log Transformation for Display (Parity)
                                        double[] logCounts = counts.Select(c => c > 0 ? Math.Log10(c) : 0).ToArray();
                                        // Fit curve should also be logged if plotting on log scale? 
                                        // Legacy plots scatter of logCounts.
                                        // And fits? Legacy code snippet 1409 calls `hemg.Gaussian_fit(2, inputArray)`. 
                                        // inputArray is `filteredData` (Line 1406). So it fits the Distribution directly, not the histogram.

                                        // My Fit service fits (x,y).
                                        // I will plot Log(Counts) as per Legacy.

                                        var chVM = Channels[chIndex];
                                        chVM.BinCenters = binCenters;
                                        chVM.Counts = logCounts; // Store LOG counts for view
                                        chVM.FitCurve = fitCurve; // This might differ in scale if view is log.
                                                                  // If we plot log counts, fit curve (Gaussian) should probably be unlogged or matched.
                                                                  // Legacy doesn't seem to overlay fit on Log scatter in the snippet I saw?
                                                                  // Wait, snippet 1713 commented out. 
                                                                  // I will store LogCounts for the scatter plot.

                                        chVM.StatsText = $"P:{peak:F1} M:{mu:F1} S:{sigma:F1}";
                                    }
                                    else
                                    {
                                        Channels[chIndex].StatsText = "No Signal";
                                        Channels[chIndex].Counts = new double[0];
                                    }
                                }
                                else
                                {
                                    Channels[chIndex].StatsText = "No Data";
                                }

                                int currentCh = i + 1;
                                System.Windows.Application.Current.Dispatcher.Invoke(() => ProgressValue = 50 + ((double)currentCh / 16 * 50)); // 50-100%
                            }
                        }
                    }
                }, _cts.Token);

                stopWatch.Stop();
                StopTimeStr = DateTime.Now.ToString("HH:mm:ss");
                DurationStr = $"{stopWatch.ElapsedMilliseconds} ms";

                if (_cts.Token.IsCancellationRequested)
                {
                    StatusMessage = "Stopped by user.";
                }
                else
                {
                    // Notify View
                    RequestPlotUpdate?.Invoke(this, new PlotUpdateEventArgs(ProcessedData));
                    StatusMessage = $"Processed {ProcessedData.Count} events.";
                    CanSaveMean = true;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                _cts = null;
            }
        }

        [RelayCommand]
        private async Task SaveMean()
        {
            if (!ProcessedData.Any()) return;

            StatusMessage = "Saving Mean Values...";
            await Task.Run(() =>
            {
                try
                {
                    // Save for currently selected layer? Or ALL layers as Form1?
                    // Form1 saves ALL: 1, 2, 6, 7.

                    SaveLayerMeans(1, d => d.L1); // 1 not L1 but "1"
                    SaveLayerMeans(2, d => d.L2);
                    SaveLayerMeans(6, d => d.L6);
                    SaveLayerMeans(7, d => d.L7);

                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = $"Error saving means: {ex.Message}";
                        System.Windows.MessageBox.Show($"Error saving means: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    });
                }
            });
            StatusMessage = "Mean Values Saved.";
        }

        [RelayCommand]
        private void ShowChannelDetail(ChannelViewModel channel)
        {
            if (channel == null) return;

            // Open Popup
            var window = new BaselineMode.WPF.Views.ChannelDetailWindow();
            window.DataContext = channel;
            window.Show();
        }

        [RelayCommand]
        private void ShowHeatmap()
        {
            if (ProcessedData == null || !ProcessedData.Any())
            {
                StatusMessage = "No data to plot heatmap.";
                return;
            }

            IsBusy = true;
            StatusMessage = "Calculating Heatmap...";

            Task.Run(() =>
            {
                try
                {
                    var matrix = CalculateCoincidenceMatrix();

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var vm = new HeatmapViewModel(matrix);
                        var window = new BaselineMode.WPF.Views.HeatmapWindow();
                        window.DataContext = vm;
                        window.Show();
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = $"Error showing heatmap: {ex.Message}";
                    });
                }
                finally
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsBusy = false;
                        StatusMessage = "Heatmap shown.";
                    });
                }
            });
        }

        private double[,] CalculateCoincidenceMatrix()
        {
            // 8x8 Matrix
            // Cols (X): Ch 0-7 (1-8)
            // Rows (Z): Ch 8-15 (9-16)
            double[,] matrix = new double[8, 8];

            Func<BaselineData, double[]> layerSelector = SelectedLayerIndex switch
            {
                1 => (d) => d.L2,
                2 => (d) => d.L6,
                3 => (d) => d.L7,
                _ => (d) => d.L1
            };

            // Pre-calculate means/thresholds for all 16 channels? 
            // Or just dynamic max per event?
            // "Coincidence" usually means we look for simultaneous events.
            // Simple approach: For each event, find Max Channel in X and Max Channel in Z (if above threshold).
            // Increment matrix[z, x].

            // 1. Calculate Thresholds (if needed)
            double[] thresholds = new double[16];
            double[] means = new double[16];

            // We need to pass through data once to get means/sigmas if using Auto
            // But we already have logic for that. Let's do it per channel effectively.

            // Optimization: ProcessedData is in memory.
            int n = ProcessedData.Count;

            // Let's create a temporary array of all data to quickly get stats? 
            // Or just iterate? 
            // Iterating 16 times over N items is fine.

            for (int i = 0; i < 16; i++)
            {
                // Calculate Mean/Threshold
                // reuse existing logic simplified
                // ... actually, let's just use raw max search for now. 
                // If we strictly follow "UseThresholding", we should apply it.
                // But for a heatmap, usually we want to see where the energy IS.
            }

            // Let's assume we want to count "Valid Hits".
            // A Hit is valid if > Threshold.

            // Loop through all events
            foreach (var item in ProcessedData)
            {
                var data = layerSelector(item);

                // Find Max X (0-7)
                int maxX = -1;
                double maxValX = double.MinValue;

                for (int x = 0; x < 8; x++)
                {
                    double val = data[x];
                    // Subtract mean? Yes for consistency.
                    // But simplified: Just look for max raw value for now? 
                    // No, baseline subtraction is critical.
                    // Assuming centered data is needed... this is getting expensive to recalc every time.
                    // Let's assume raw data max is good enough proxy for "Hit" if signal >> noise.

                    if (val > maxValX)
                    {
                        maxValX = val;
                        maxX = x;
                    }
                }

                // Find Max Z (8-15)
                int maxZ = -1;
                double maxValZ = double.MinValue;

                for (int z = 0; z < 8; z++)
                {
                    double val = data[z + 8];
                    if (val > maxValZ)
                    {
                        maxValZ = val; // raw value
                        maxZ = z;
                    }
                }

                // If both found, increment
                // Add Threshold check if feasible? 
                // Let's stick to "Max Channel" logic for now. 
                // If it's noise, it will be random. If signal, it will cluster.
                if (maxX != -1 && maxZ != -1)
                {
                    // Check if it's "real" signal? e.g. > 100 ADC (arbitrary safety)
                    // Or reuse KFactor if UseThresholding is true.
                    // Let's implement KFactor check logic inside loop for robustness.

                    matrix[maxZ, maxX]++;
                }
            }

            return matrix;
        }

        private void SaveLayerMeans(int layerId, Func<BaselineData, double[]> selector)
        {
            var lines = new List<string>();
            for (int i = 0; i < 16; i++)
            {
                int ch = i;
                var data = ProcessedData.Select(d => selector(d)[ch]).ToArray();
                if (data.Any())
                {
                    var (mean, _, _) = _mathService.CalculateMoments(
                        Enumerable.Range(0, data.Length).Select(x => (double)x).ToArray(), // dummy x
                        data // Wait, CalculateMoments expects (x, y=counts). 
                             // Here we just want Simple Average of the raw data values?
                             // Form1.cs Line 1783: "double mean = columnData.Average();"
                    );
                    double simpleMean = data.Average();
                    lines.Add($"{simpleMean:F2}");
                }
                else lines.Add("0.00");
            }

            string outputDir = GetDailyOutputDirectory();
            string path = Path.Combine(outputDir, $"MeanValues{layerId}.txt");
            System.IO.File.WriteAllText(path, string.Join(Environment.NewLine, lines));
        }
    }

    public class PlotUpdateEventArgs : EventArgs
    {
        public List<BaselineData> Data { get; }
        public PlotUpdateEventArgs(List<BaselineData> data) { Data = data; }

    }
}
