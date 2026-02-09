using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BaselineMode.WPF.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BaselineMode.WPF.Views.models
{
    public partial class MainViewModel
    {
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

        partial void OnSelectedLayerIndexChanged(int value)
        {
            UpdateDisplayTable();
            RefreshIfHasData();
        }

        partial void OnSelectedDirectionIndexChanged(int value) => RefreshIfHasData();
        partial void OnUseGaussianFitChanged(bool value) => RefreshIfHasData();
        partial void OnSelectedFitMethodChanged(int value) => RefreshIfHasData();
        partial void OnUseThresholdingChanged(bool value) => RefreshIfHasData();
        partial void OnKFactorChanged(double value) => RefreshIfHasData();
        partial void OnSelectedXAxisIndexChanged(int value) => RefreshIfHasData();

        partial void OnSelectedBaselineModeChanged(int value)
        {
            // Enable save mean only when not using log scale
            CanSaveMean = value < 2;
            RefreshIfHasData();
        }

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

            var layerSelector = GetLayerSelector();

            for (int i = 0; i < 16; i++)
            {
                int chIndex = i;
                var rawData = ExtractChannelData(layerSelector, chIndex);

                if (rawData.Length == 0) continue;

                // Baseline subtraction (modifies rawData in-place)
                bool subtracted = ApplyBaselineSubtraction(rawData, chIndex, out _);

                // Thresholding
                var filteredData = ApplyThresholding(rawData);

                if (filteredData.Length > 5)
                {
                    var (counts, binCenters) = BuildHistogram(filteredData, subtracted);
                    ProcessChannelData(chIndex, filteredData, counts, binCenters);
                }
                else
                {
                    Channels[chIndex].StatsText = "No Signal";
                    Channels[chIndex].Counts = Array.Empty<double>();
                }
            }

            RequestPlotUpdate?.Invoke(this, new PlotUpdateEventArgs(ProcessedData));
        }

        private void ProcessChannelData(int chIndex, double[] filteredData, double[] counts, double[] binCenters)
        {
            double[]? fitCurveLinear = null;
            double mu = 0, sigma = 0, peak = 0;
            double fwhm = 0, resolution = 0;

            if (UseGaussianFit && HasSufficientData(filteredData, counts))
            {
                try
                {
                    FittingResult result;

                    if (SelectedFitMethod == 2)
                    {
                        // Hyper-EMG Double-Sided (left + right tails)
                        result = _mathService.HyperEMGDoubleSidedFit(binCenters, counts);
                    }
                    else if (SelectedFitMethod == 1)
                    {
                        // Hyper-EMG Single tail
                        result = _mathService.HyperEMGFit(binCenters, counts);
                    }
                    else
                    {
                        // Gaussian
                        result = _mathService.GaussianFit(binCenters, counts);
                    }

                    if (result.FitCurve != null && result.FitCurve.Length > 0)
                    {
                        fitCurveLinear = result.FitCurve;
                        mu = result.Mu;
                        sigma = result.Sigma;
                        peak = result.Peak;

                        // คำนวณ FWHM จาก fit curve
                        (fwhm, resolution) = CalculateFWHM(binCenters, fitCurveLinear, mu);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Fitting error Ch{chIndex}: {ex.Message}");
                }
            }
            else
            {
                // No fitting - use moments
                var moments = _mathService.CalculateMoments(binCenters, counts);
                mu = moments.mean;
                sigma = moments.sigma;
                peak = moments.peak;
                fwhm = 2.355 * sigma;
                resolution = mu != 0 ? (fwhm / mu) * 100 : 0;
            }

            // Set channel data
            var chVM = Channels[chIndex];
            chVM.BinCenters = binCenters;
            chVM.RawCounts = counts;

            bool isLogScale = (SelectedBaselineMode == 2 || SelectedBaselineMode == 3);
            chVM.IsLogScale = isLogScale;

            if (isLogScale)
            {
                chVM.Counts = counts.Select(c => c > 0 ? Math.Log10(c) : 0).ToArray();
                if (fitCurveLinear != null)
                    chVM.FitCurve = fitCurveLinear.Select(c => c > 0 ? Math.Log10(c) : 0).ToArray();
            }
            else
            {
                chVM.Counts = counts;
                chVM.FitCurve = fitCurveLinear;
            }

            chVM.Mu = mu;
            chVM.Sigma = sigma;
            chVM.Peak = peak;
            chVM.FWHM = fwhm;
            chVM.Resolution = resolution;
            chVM.StatsText = $"μ={mu:F2}, σ={sigma:F2}, FWHM={fwhm:F2}, Res={resolution:F2}%";
        }

        private (double fwhm, double resolution) CalculateFWHM(double[] binCenters, double[] fitCurve, double mu)
        {
            if (fitCurve.Length == 0) return (0, 0);

            int peakIdx = Array.IndexOf(fitCurve, fitCurve.Max());
            double peakHeight = fitCurve[peakIdx];
            double halfMax = peakHeight / 2.0;

            int leftIdx = -1;
            for (int i = peakIdx; i >= 0; i--)
            {
                if (fitCurve[i] <= halfMax)
                {
                    leftIdx = i;
                    break;
                }
            }

            int rightIdx = -1;
            for (int i = peakIdx; i < fitCurve.Length; i++)
            {
                if (fitCurve[i] <= halfMax)
                {
                    rightIdx = i;
                    break;
                }
            }

            if (leftIdx >= 0 && rightIdx >= 0 && leftIdx < rightIdx)
            {
                double fwhm = binCenters[rightIdx] - binCenters[leftIdx];
                double resolution = mu != 0 ? (fwhm / mu) * 100 : 0;
                return (fwhm, resolution);
            }

            return (0, 0);
        }


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

                Func<BaselineData, double[]> selector = GetLayerSelector();

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

        [RelayCommand]
        private void ShowChannelDetail(ChannelViewModel channel)
        {
            if (channel == null) return;
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
    }
}
