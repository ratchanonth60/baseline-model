using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaselineMode.WPF.Core.Models;
using BaselineMode.WPF.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BaselineMode.WPF.Core.Models.Baseline;
using BaselineMode.WPF.Core.Models.Shared;
using BaselineMode.WPF.Core.Models.Flux;
using BaselineMode.WPF.Presentation.ViewModels.Shared;

namespace BaselineMode.WPF.Presentation.ViewModels.Baseline
{
    public partial class MainViewModel
    {
        [RelayCommand]
        private void Reset()
        {
            _selectedFiles = [];
            InputFilesInfo = "No files selected";
            ProcessedData = [];
            UpdateDisplayTable();
            InitializeChannels();
            StatusMessage = "Reset complete.";
            StatusColor = System.Windows.Media.Brushes.Gray;
            ProgressValue = 0;
            CurrentPage = 1;
            RequestPlotUpdate?.Invoke(this, new PlotUpdateEventArgs([]));
        }

        [RelayCommand]
        private void Stop()
        {
            _cts?.Cancel();
            StatusMessage = "Stopping...";
            StatusColor = System.Windows.Media.Brushes.Orange;
        }

        [RelayCommand]
        private async Task PreProcessData()
        {
            if (_selectedFiles.Count == 0)
            {
                StatusMessage = "No files selected for processing.";
                StatusColor = System.Windows.Media.Brushes.Red;
                return;
            }

            if (string.IsNullOrWhiteSpace(OutputFileName))
            {
                StatusMessage = "Please provide output filename.";
                StatusColor = System.Windows.Media.Brushes.Red;
                return;
            }

            IsBusy = true;
            StatusMessage = "Processing raw files to Excel...";
            StatusColor = System.Windows.Media.Brushes.Orange;

            await Task.Run(async () =>
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

                        var fileProgress = new Progress<double>(p =>
                        {
                            double baseProgress = (double)currentFile / fileCount * 70;
                            double currentFileContribution = (p / 100.0) * (1.0 / fileCount) * 70;
                            System.Windows.Application.Current.Dispatcher.Invoke(() => ProgressValue = baseProgress + currentFileContribution);
                        });

                        var result = await _fileService.ProcessFileStreamAsync(file, fileProgress);
                        if (result.IsFailure)
                        {
                            _logger.LogError($"Failed to process raw file {file}: {result.Error}");
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                StatusMessage = result.Error;
                                StatusColor = System.Windows.Media.Brushes.Red;
                                MessageBoxService.Show(result.Error, "Process Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                            });
                            return;
                        }

                        allData.AddRange(result.Value);
                        _logger.LogInfo($"Processed {result.Value.Count} events from {file}");
                        currentFile++;
                    }

                    if (allData.Count != 0)
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

                        var saveResult = await _fileService.SaveToExcelAsync(allData, fullPath, saveProgress);
                        if (saveResult.IsFailure)
                        {
                            _logger.LogError($"Failed to save Baseline Excel: {saveResult.Error}");
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                StatusMessage = saveResult.Error;
                                StatusColor = System.Windows.Media.Brushes.Red;
                                MessageBoxService.Show(saveResult.Error, "Save Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                            });
                            return;
                        }

                        _logger.LogInfo($"Baseline data saved to Excel: {fullPath}");

                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = $"Saved {allData.Count} events to {fileName}";
                            StatusColor = System.Windows.Media.Brushes.LimeGreen;
                            MessageBoxService.Show($"Successfully processed {allData.Count} events to Source folder.", "Process Data", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                        });
                    }
                    else
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = "No valid data found in selected files.";
                            StatusColor = System.Windows.Media.Brushes.Red;
                            MessageBoxService.Show("No valid data found in selected files. Please check the input file layout.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogException(ex, "Error in PreProcessData (Baseline)");
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = $"Error: {ex.Message}";
                        StatusColor = System.Windows.Media.Brushes.Red;
                    });
                }
            });

            IsBusy = false;
        }

        [RelayCommand]
        private async Task ProcessData()
        {
            if (_selectedFiles.Count == 0)
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

                    var readResult = await _fileService.ReadExcelFileAsync(fullPath, readProgress);
                    if (readResult.IsFailure)
                    {
                        _logger.LogError($"Failed to read baseline Excel: {readResult.Error}");
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = readResult.Error;
                            MessageBoxService.Show(readResult.Error, "Read Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        });
                        return;
                    }

                    ProcessedData = readResult.Value;
                    _logger.LogInfo($"Successfully read {ProcessedData.Count} events from baseline Excel: {fullPath}");

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        DataCountsStr = ProcessedData.Count.ToString();
                        UpdateDisplayTable();
                    });

                    if (ProcessedData.Count == 0) return;

                    var layerSelector = GetLayerSelector();


                    int processedCount = 0;
                    object processedLock = new();
                    _ = Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = _cts.Token }, i =>
                    {
                        int chIndex = i;
                        double[] rawData = ExtractChannelData(layerSelector, chIndex);

                        if (rawData.Length > 0)
                        {
                            bool subtracted = ApplyBaselineSubtraction(rawData, chIndex, out _);
                            var filteredData = ApplyThresholding(rawData);

                            if (filteredData.Length > 0)
                            {
                                double hMin = 0;
                                double hMax = 16384;

                                // ScottPlot Histogram
                                var (counts, binEdges) = ScottPlot.Statistics.Common.Histogram(filteredData, min: hMin, max: hMax, binCount: 16384);

                                // สร้าง BinCenters
                                double[] binCenters = new double[binEdges.Length - 1];
                                for (int k = 0; k < binCenters.Length; k++)
                                {
                                    double center = binEdges[k] + 0.5;
                                    // Apply X-Axis Conversion ใน Loop เดียว
                                    binCenters[k] = (SelectedXAxisIndex == 1)
                                        ? ((center / 16384.0) * 5) * 1000
                                        : center;
                                }

                                // Prepare Multi-Fit Results
                                var fitResults = new Dictionary<string, ChannelViewModel.FitData>();

                                if (filteredData.Length > 5)
                                {
                                    // 1. Gaussian Fit
                                    if (ShowGaussianFit)
                                    {
                                        var res = _mathService.GaussianFit(binCenters, counts);
                                        if (res.IsValid && res.FitCurve != null && res.FitCurve.Length > 0)
                                        {
                                            fitResults["Gaussian"] = new ChannelViewModel.FitData
                                            {
                                                Curve = res.FitCurve,
                                                Color = System.Drawing.Color.LimeGreen,
                                                Label = "Gaussian"
                                            };
                                        }
                                    }

                                    // 2. HEMG Single
                                    if (ShowHemgSingleFit)
                                    {
                                        var res = _mathService.HyperEMGFit(binCenters, counts, filteredData);
                                        if (res.IsValid && res.FitCurve != null && res.FitCurve.Length > 0)
                                        {
                                            fitResults["HEMG-S"] = new ChannelViewModel.FitData
                                            {
                                                Curve = res.FitCurve,
                                                Color = System.Drawing.Color.Red,
                                                Label = "HEMG single"
                                            };
                                        }
                                    }

                                    // 3. HEMG Double
                                    if (ShowHemgDoubleFit)
                                    {
                                        var res = _mathService.HyperEMGDoubleSidedFit(binCenters, counts, filteredData);
                                        if (res.IsValid && res.FitCurve != null && res.FitCurve.Length > 0)
                                        {
                                            fitResults["HEMG-D"] = new ChannelViewModel.FitData
                                            {
                                                Curve = res.FitCurve,
                                                Color = System.Drawing.Color.Magenta,
                                                Label = "HEMG double"
                                            };
                                        }
                                    }

                                    // 4. Lorentzian
                                    if (ShowLorentzianFit)
                                    {
                                        var res = _mathService.LorentzianFit(binCenters, counts);
                                        if (res.IsValid && res.FitCurve != null && res.FitCurve.Length > 0)
                                        {
                                            fitResults["Lorentzian"] = new ChannelViewModel.FitData
                                            {
                                                Curve = res.FitCurve,
                                                Color = System.Drawing.Color.Cyan,
                                                Label = "Lorentzian"
                                            };
                                        }
                                    }
                                }

                                // UI Update (Assign Results + Render)
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                    ProcessChannelData(chIndex, filteredData, counts, binCenters, fitResults));

                            }
                        }
                        else
                        {
                            UpdateChannelStatsSafe(chIndex, "No Data", []);
                        }

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
                _logger.LogException(ex, "Error in ProcessData (Baseline)");
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
            // ต้องใช้ Dispatcher.Invoke เพื่อ update UI-bound properties จาก background thread
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Channels[chIndex].StatsText = msg;
                if (counts != null)
                {
                    Channels[chIndex].Counts = counts;
                    Channels[chIndex].RawCounts = counts;
                }
            });
        }
        private double[] ApplyThresholding(double[] centeredData)
        {
            if (!UseThresholding) return centeredData;

            // Calculate mean first
            int length = centeredData.Length;
            double sum = 0;
            for (int i = 0; i < length; i++)
            {
                sum += centeredData[i];
            }
            double mean = sum / length;

            // Calculate sigma using mean-centered standard deviation
            double sumSquares = 0;
            for (int i = 0; i < length; i++)
            {
                double diff = centeredData[i] - mean;
                sumSquares += diff * diff;
            }
            double sigma = Math.Sqrt(sumSquares / length);
            double threshold = KFactor * sigma;

            // Optimized: First count, then allocate exact size
            int count = 0;
            for (int i = 0; i < length; i++)
            {
                if (Math.Abs(centeredData[i] - mean) > threshold) count++;
            }

            double[] result = new double[count];
            int index = 0;
            for (int i = 0; i < length; i++)
            {
                if (Math.Abs(centeredData[i] - mean) > threshold)
                {
                    result[index++] = centeredData[i];
                }
            }
            return result;
        }

        // Removed unused PerformFit method
        /* 
        private FittingResult PerformFit(double[] binCenters, double[] counts, double[] filteredData)
        {

        /* 
        private FittingResult PerformFit(double[] binCenters, double[] counts, double[] filteredData)
        {
             // Replaced by inline multi-fit logic in ProcessData
             return null; 
        } 
        */


        private double[,] CalculateCoincidenceMatrix()
        {
            // 8x8 Matrix
            // Cols (X): Ch 0-7 (1-8)
            // Rows (Z): Ch 8-15 (9-16)
            double[,] matrix = new double[8, 8];


            var layerSelector = GetLayerSelector();


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
        public async Task ProcessChannelFitAsync(int chIndex, double[] binCenters, double[] counts, double[] filteredData)
        {
            var chVM = Channels[chIndex];

            // 1. Check if fitting is required
            bool fitGaussian = ShowGaussianFit;
            bool fitHemgS = ShowHemgSingleFit;
            bool fitHemgD = ShowHemgDoubleFit;
            bool fitLorentzian = ShowLorentzianFit;

            if (!fitGaussian && !fitHemgS && !fitHemgD && !fitLorentzian)
            {
                // No fits selected, just render data
                ProcessChannelData(chIndex, filteredData, counts, binCenters, []);
                return;
            }

            // 2. Set Loading State
            chVM.IsFitting = true;

            // 3. Prepare Dictionary for results
            var fitResults = new Dictionary<string, ChannelViewModel.FitData>();

            try
            {
                // 4. Run Fits (Background Thread)
                await Task.Run(() =>
                {
                    // Gaussian
                    if (fitGaussian)
                    {
                        var cached = chVM.GetCachedFit("Gaussian");
                        if (cached != null)
                        {
                            fitResults["Gaussian"] = cached;
                        }
                        else
                        {
                            var res = _mathService.GaussianFit(binCenters, counts);
                            if (res.IsValid && res.FitCurve != null && res.FitCurve.Length > 0)
                            {
                                var data = new ChannelViewModel.FitData
                                {
                                    Curve = res.FitCurve,
                                    Color = System.Drawing.Color.LimeGreen,
                                    Label = "Gaussian"
                                };
                                fitResults["Gaussian"] = data;
                                chVM.CacheFit("Gaussian", data);
                            }
                        }
                    }

                    // HEMG Single
                    if (fitHemgS)
                    {
                        var cached = chVM.GetCachedFit("HEMG-S");
                        if (cached != null)
                        {
                            fitResults["HEMG-S"] = cached;
                        }
                        else
                        {
                            var res = _mathService.HyperEMGFit(binCenters, counts, filteredData);
                            if (res.IsValid && res.FitCurve != null && res.FitCurve.Length > 0)
                            {
                                var data = new ChannelViewModel.FitData
                                {
                                    Curve = res.FitCurve,
                                    Color = System.Drawing.Color.Red,
                                    Label = "HEMG(1)"
                                };
                                fitResults["HEMG-S"] = data;
                                chVM.CacheFit("HEMG-S", data);
                            }
                        }
                    }

                    // HEMG Double
                    if (fitHemgD)
                    {
                        var cached = chVM.GetCachedFit("HEMG-D");
                        if (cached != null)
                        {
                            fitResults["HEMG-D"] = cached;
                        }
                        else
                        {
                            var res = _mathService.HyperEMGDoubleSidedFit(binCenters, counts, filteredData);
                            if (res.IsValid && res.FitCurve != null && res.FitCurve.Length > 0)
                            {
                                var data = new ChannelViewModel.FitData
                                {
                                    Curve = res.FitCurve,
                                    Color = System.Drawing.Color.Magenta,
                                    Label = "HEMG(2)"
                                };
                                fitResults["HEMG-D"] = data;
                                chVM.CacheFit("HEMG-D", data); // Cache it!
                            }
                        }
                    }

                    // Lorentzian
                    if (fitLorentzian)
                    {
                        var cached = chVM.GetCachedFit("Lorentzian");
                        if (cached != null)
                        {
                            fitResults["Lorentzian"] = cached;
                        }
                        else
                        {
                            var res = _mathService.LorentzianFit(binCenters, counts);
                            if (res.IsValid && res.FitCurve != null && res.FitCurve.Length > 0)
                            {
                                var data = new ChannelViewModel.FitData
                                {
                                    Curve = res.FitCurve,
                                    Color = System.Drawing.Color.Cyan,
                                    Label = "Lorentzian"
                                };
                                fitResults["Lorentzian"] = data;
                                chVM.CacheFit("Lorentzian", data);
                            }
                        }
                    }
                });

                // 5. Update UI (Main Thread)
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    ProcessChannelData(chIndex, filteredData, counts, binCenters, fitResults);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Async Fit Ch {chIndex}: {ex.Message}");
            }
            finally
            {
                chVM.IsFitting = false;
            }
        }
    }
}
