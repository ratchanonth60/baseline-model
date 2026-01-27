using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BaselineMode.WPF.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BaselineMode.WPF.ViewModels
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

        partial void OnUseGaussianFitChanged(bool value) => RefreshIfHasData();
        partial void OnSelectedFitMethodChanged(int value) => RefreshIfHasData();
        partial void OnUseThresholdingChanged(bool value) => RefreshIfHasData();
        partial void OnKFactorChanged(double value) => RefreshIfHasData();
        partial void OnSelectedXAxisIndexChanged(int value) => RefreshIfHasData();

        partial void OnSelectedBaselineModeChanged(int value)
        {
            CanSaveMean = value == 0; // 0 = Auto
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
                    return;
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
            double[] logCounts = counts.Select(c => c > 0 ? Math.Log10(c) : 0).ToArray();
            chVM.Counts = logCounts;
            chVM.FitCurve = fitCurve;
            chVM.StatsText = $"P:{peak:F1} M:{mu:F1} S:{sigma:F1}";
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
