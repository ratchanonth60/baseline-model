using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaselineMode.WPF.Models;
using BaselineMode.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BaselineMode.WPF.Views.models
{
    public partial class MainViewModel
    {
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
            RequestPlotUpdate?.Invoke(this, new PlotUpdateEventArgs(new List<BaselineData>()));
        }

        [RelayCommand]
        private void Stop()
        {
            _cts?.Cancel();
            StatusMessage = "Stopping...";
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
                });

                try
                {
                    var allData = new List<BaselineData>();

                    int fileCount = _selectedFiles.Count;
                    int currentFile = 0;

                    foreach (var file in _selectedFiles)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() => StatusMessage = $"Processing file {currentFile + 1}/{fileCount}...");

                        // Create a progress reporter for the current file processing
                        var fileProgress = new Progress<double>(p =>
                        {
                            // Calculate global progress: 
                            // Base progress for completed files + fraction of current file
                            // Processing takes up 70% of total progress
                            double baseProgress = (double)currentFile / fileCount * 70;
                            double currentFileContribution = (p / 100.0) * (1.0 / fileCount) * 70;
                            System.Windows.Application.Current.Dispatcher.Invoke(() => ProgressValue = baseProgress + currentFileContribution);
                        });

                        var fileData = _fileService.ProcessFileStream(file, fileProgress);
                        allData.AddRange(fileData);

                        currentFile++;
                    }

                    if (allData.Any())
                    {
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

                        _fileService.SaveToExcel(allData, fullPath, saveProgress);

                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = $"Saved {allData.Count} events to {fileName}";
                            MessageBoxService.Show($"Successfully processed {allData.Count} events to Source folder.", "Process Data", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                        });
                    }
                    else
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = "No valid data found in selected files.";
                            MessageBoxService.Show("No valid data found in selected files. Please check the input file layout.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                        });
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
                await Task.Run(() =>
                {
                    // 1. Construct Path to Source File
                    string fileName = OutputFileName;
                    if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                        fileName += ".xlsx";

                    string outputDir = GetDailyOutputDirectory();
                    string fullPath = Path.Combine(outputDir, fileName);

                    if (!File.Exists(fullPath))
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = $"File not found: {fullPath}";
                            MessageBoxService.Show($"Expected input file not found:\n{fullPath}\n\nPlease ensure you have run 'Process Data' first.", "File Not Found", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        });
                        return;
                    }

                    // Debugging Hint: Show where we are reading from
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBoxService.Show($"Reading from:\n{fullPath}", "Confirm Input File", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    });

                    if (_cts.Token.IsCancellationRequested) return;


                    // 2. Read from Excel
                    System.Windows.Application.Current.Dispatcher.Invoke(() => StatusMessage = "Reading Excel...");
                    var readProgress = new Progress<double>(p =>
                        System.Windows.Application.Current.Dispatcher.Invoke(() => ProgressValue = p * 0.5)); // 0-50%

                    // Restore missing call!
                    ProcessedData = _fileService.ReadExcelFile(fullPath, readProgress);

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        DataCountsStr = ProcessedData.Count.ToString();
                        UpdateDisplayTable();
                    });
                    if (!ProcessedData.Any()) return;
                    // OPTIMIZE: Parallelize processing
                    Func<BaselineData, double[]> layerSelector = SelectedLayerIndex switch
                    {
                        1 => (d) => d.L2,
                        2 => (d) => d.L6,
                        3 => (d) => d.L7,
                        _ => (d) => d.L1
                    };

                    int processedCount = 0;
                    object processedLock = new object();
                    Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = _cts.Token }, i =>
                    {
                        int chIndex = i;
                        // Optimization: ดึงข้อมูลออกมาเป็น Array เดียวเพื่อลดการเข้าถึง Property ซ้ำๆ
                        // การใช้ Loop ธรรมดาเร็วกว่า LINQ .Select().ToArray() ในกรณี Performance Critical
                        int dataCount = ProcessedData.Count;
                        double[] rawData = new double[dataCount];
                        for (int j = 0; j < dataCount; j++)
                        {
                            rawData[j] = layerSelector(ProcessedData[j])[chIndex];
                        }
                        if (rawData.Length > 0)
                        {
                            // Calculate Mean for baseline subtraction (modes 1 and 3)
                            double meanToSubtract = 0;
                            bool applyBaselineSubtraction = (SelectedBaselineMode == 1 || SelectedBaselineMode == 3);
                            if (applyBaselineSubtraction)
                            {
                                double sum = 0;
                                for (int k = 0; k < rawData.Length; k++) sum += rawData[k];
                                meanToSubtract = sum / rawData.Length;
                            }
                            // Apply Mean Subtraction (In-Place เพื้่อประหยัด ram)
                            // เราแก้ค่าใน rawData เลย ไม่ต้องสร้าง centeredData ใหม่
                            if (meanToSubtract != 0)
                            {
                                for (int k = 0; k < rawData.Length; k++)
                                {
                                    rawData[k] -= meanToSubtract;
                                }
                            }

                            // Filter / Thresholding
                            var filteredData = ApplyThresholding(rawData); // ตรวจสอบว่าฟังก์ชันนี้สร้าง Array ใหม่หรือไม่ ถ้าแก้ให้รับ Span หรือ Array ได้จะดีมาก

                            if (filteredData.Length > 0)
                            {
                                double hMin = 0;
                                double hMax = 16383;

                                // ScottPlot Histogram
                                var (counts, binEdges) = ScottPlot.Statistics.Common.Histogram(filteredData, min: hMin, max: hMax, binCount: 16383);

                                // สร้าง BinCenters
                                double[] binCenters = new double[binEdges.Length - 1];
                                for (int k = 0; k < binCenters.Length; k++)
                                {
                                    double center = binEdges[k] + 0.5;
                                    // Apply X-Axis Conversion ใน Loop เดียว
                                    if (SelectedXAxisIndex == 1) // Voltage
                                        binCenters[k] = ((center / 16383.0) * 5) * 1000;
                                    else
                                        binCenters[k] = center;
                                }

                                // UI Update (ต้องระวัง Thread Safety)
                                ProcessChannelData(chIndex, filteredData, counts, binCenters);
                            }
                            else
                            {
                                UpdateChannelStatsSafe(chIndex, "No Signal", new double[0]);
                            }
                        }
                        else
                        {
                            UpdateChannelStatsSafe(chIndex, "No Data", Array.Empty<double>());
                        }

                        // Update Progress (Thread Safe)
                        lock (processedLock)
                        {
                            processedCount++;
                            double progress = 50 + ((double)processedCount / 16 * 50);
                            System.Windows.Application.Current.Dispatcher.Invoke(() => ProgressValue = progress);
                        }

                    });
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
                    StatusMessage = $"Processed {ProcessedData.Count} events. Time: {DurationStr}"; CanSaveMean = true;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                _cts?.Dispose();
                _cts = null!;
            }
        }
        // Helper เพื่อป้องกัน Cross-thread exception เวลา update UI object จาก Parallel Loop
        private void UpdateChannelStatsSafe(int chIndex, string msg, double[] counts)
        {
            // สมมติว่า Channels เป็น ObservableCollection หรือ List ที่ผูกกับ UI
            // การแก้ไขค่าข้างในอาจต้องทำบน UI Thread หรือใช้ lock ถ้า object นั้นไม่ได้ thread-safe
            // แต่ถ้า Channels[i] แยกกันอิสระ มักจะแก้ property พื้นฐานได้ (แต่ระวัง ObservableCollection จะเด้ง event)

            // ทางที่ดีที่สุด:
            lock (Channels)
            {
                Channels[chIndex].StatsText = msg;
                if (counts != null)
                {
                    Channels[chIndex].Counts = counts;
                    Channels[chIndex].RawCounts = counts;
                }
            }
        }
        private double[] ApplyThresholding(double[] centeredData)
        {
            if (!UseThresholding) return centeredData;

            // Optimized: Calculate sigma without LINQ
            double sumSquares = 0;
            int length = centeredData.Length;
            for (int i = 0; i < length; i++)
            {
                sumSquares += centeredData[i] * centeredData[i];
            }
            double sigma = Math.Sqrt(sumSquares / length);
            double threshold = KFactor * sigma;

            // Optimized: First count, then allocate exact size
            int count = 0;
            for (int i = 0; i < length; i++)
            {
                if (centeredData[i] > threshold) count++;
            }

            double[] result = new double[count];
            int index = 0;
            for (int i = 0; i < length; i++)
            {
                if (centeredData[i] > threshold)
                {
                    result[index++] = centeredData[i];
                }
            }
            return result;
        }

        private bool HasSufficientData(double[] filteredData, double[] counts)
        {
            return filteredData.Length > 5 && counts.Max() > 0;
        }

        private FittingResult PerformFit(double[] binCenters, double[] counts)
        {
            if (SelectedFitMethod == 1) // Hyper-EMG
            {
                return _mathService.HyperEMGFit(binCenters, counts);
            }
            else // Gaussian
            {
                return _mathService.GaussianFit(binCenters, counts);
            }
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

            // Optimized: Loop through all events with reduced property access
            int dataCount = ProcessedData.Count;
            for (int idx = 0; idx < dataCount; idx++)
            {
                var data = layerSelector(ProcessedData[idx]);

                // Find Max X (0-7)
                int maxX = 0;
                double maxValX = data[0];
                for (int x = 1; x < 8; x++)
                {
                    if (data[x] > maxValX)
                    {
                        maxValX = data[x];
                        maxX = x;
                    }
                }

                // Find Max Z (8-15)
                int maxZ = 0;
                double maxValZ = data[8];
                for (int z = 1; z < 8; z++)
                {
                    if (data[z + 8] > maxValZ)
                    {
                        maxValZ = data[z + 8];
                        maxZ = z;
                    }
                }

                matrix[maxZ, maxX]++;
            }

            return matrix;
        }
    }
}
