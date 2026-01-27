using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaselineMode.WPF.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BaselineMode.WPF.ViewModels
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
            RequestPlotUpdate?.Invoke(this, new PlotUpdateEventArgs(null));
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

                    string outputDir = GetDailyOutputDirectory();
                    string fullPath = Path.Combine(outputDir, fileName);

                    if (!File.Exists(fullPath))
                    {
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
                                    double meanToSubtract = 0;
                                    if (SelectedBaselineMode == 0) // Auto Mean
                                    {
                                        meanToSubtract = rawData.Average();
                                    }
                                    else // Load File (Legacy parity default file usage)
                                    {
                                        meanToSubtract = 0;
                                    }

                                    var centeredData = rawData.Select(x => x - meanToSubtract).ToArray();

                                    // 2. Filter / Thresholding
                                    var filteredData = ApplyThresholding(centeredData);

                                    if (filteredData.Length > 0)
                                    {
                                        double hMin = 0;
                                        double hMax = 16383;

                                        var (counts, binEdges) = ScottPlot.Statistics.Common.Histogram(filteredData, min: hMin, max: hMax, binCount: 16383);

                                        double[] binCenters = binEdges.Take(binEdges.Length - 1).Select(b => b + 0.5).ToArray();

                                        // X-Axis Conversion
                                        if (SelectedXAxisIndex == 1) // Voltage
                                            binCenters = binCenters.Select(v => ((v / 16383.0) * 5) * 1000).ToArray();
                                        else if (SelectedXAxisIndex == 2) // Energy
                                            binCenters = binCenters.Select(v => v * 1.0).ToArray(); // Placeholder conversion

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

        private double[] ApplyThresholding(double[] centeredData)
        {
            if (!UseThresholding) return centeredData;

            double sigma = Math.Sqrt(centeredData.Select(x => x * x).Average());
            double threshold = KFactor * sigma;
            return centeredData.Where(v => v > threshold).ToArray();
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
                return (result.fitCurve, result.mu, result.sigma, result.peak);
            }
            else // Gaussian
            {
                var result = _mathService.GaussianFit(binCenters, counts);
                return (result.fitCurve, result.mu, result.sigma, result.peak);
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

                if (maxX != -1 && maxZ != -1)
                {
                    matrix[maxZ, maxX]++;
                }
            }

            return matrix;
        }
    }
}
