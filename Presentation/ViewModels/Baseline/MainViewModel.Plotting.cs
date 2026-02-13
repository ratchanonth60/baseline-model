using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BaselineMode.WPF.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BaselineMode.WPF.Core.Models.Baseline;
using BaselineMode.WPF.Core.Models.Shared;
using BaselineMode.WPF.Core.Models.Flux;
using BaselineMode.WPF.Presentation.ViewModels.Shared;
using BaselineMode.WPF.Presentation.ViewModels.Flux;



namespace BaselineMode.WPF.Presentation.ViewModels.Baseline
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
        // Removed: partial void OnUseGaussianFitChanged(bool value) => RefreshIfHasData();
        // Removed: partial void OnSelectedFitMethodChanged(int value) => RefreshIfHasData();
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
            if (ProcessedData != null && ProcessedData.Count != 0)
            {
                RefreshChannelPlots();
            }
        }

        private void RefreshChannelPlots()
        {
            if (ProcessedData == null || ProcessedData.Count == 0) return;


            var layerSelector = GetLayerSelector();

            for (int i = 0; i < 16; i++)
            {
                int chIndex = i;
                var rawData = ExtractChannelData(layerSelector, chIndex);

                if (rawData.Length > 0)
                {

                    // Use helper (`ApplyBaselineSubtraction` logic is implicitly handled in main via manual calculation, but we use helper in dev)
                    // Wait, logic in main assumes rawData is NOT modified in place?
                    // rawData comes from ExtractChannelData.
                    // In lines 100+ of THIS file, it seemed to do manual subtraction.
                    // We will use logic:
                    bool subtracted = ApplyBaselineSubtraction(rawData, chIndex, out _);
                    var filteredData = ApplyThresholding(rawData);

                    if (filteredData.Length > 5)
                    {
                        var (counts, binCenters) = BuildHistogram(filteredData, subtracted);

                        // Multi-Fit Logic (Synchronous for Refresh)
                        var fitResults = new Dictionary<string, ChannelViewModel.FitData>();

                        if (ShowGaussianFit)
                        {
                            var res = _mathService.GaussianFit(binCenters, counts);
                            if (res.FitCurve != null) fitResults["Gaussian"] = new ChannelViewModel.FitData { Curve = res.FitCurve, Color = System.Drawing.Color.LimeGreen, Label = "Gaussian" };
                        }
                        if (ShowHemgSingleFit)
                        {
                            var res = _mathService.HyperEMGFit(binCenters, counts, filteredData);
                            if (res.FitCurve != null) fitResults["HEMG-S"] = new ChannelViewModel.FitData { Curve = res.FitCurve, Color = System.Drawing.Color.Red, Label = "HEMG(1)" };
                        }
                        if (ShowHemgDoubleFit)
                        {
                            var res = _mathService.HyperEMGDoubleSidedFit(binCenters, counts, filteredData);
                            if (res.FitCurve != null) fitResults["HEMG-D"] = new ChannelViewModel.FitData { Curve = res.FitCurve, Color = System.Drawing.Color.Magenta, Label = "HEMG(2)" };
                        }
                        if (ShowLorentzianFit)
                        {
                            var res = _mathService.LorentzianFit(binCenters, counts);
                            if (res.FitCurve != null) fitResults["Lorentzian"] = new ChannelViewModel.FitData { Curve = res.FitCurve, Color = System.Drawing.Color.Cyan, Label = "Lorentzian" };
                        }

                        ProcessChannelData(chIndex, filteredData, counts, binCenters, fitResults);
                    }
                    else
                    {
                        Channels[chIndex].StatsText = "No Signal";
                        Channels[chIndex].Counts = [];
                    }
                }
            }

            RequestPlotUpdate?.Invoke(this, new PlotUpdateEventArgs(ProcessedData));
        }

        private void ProcessChannelData(int chIndex, double[] filteredData, double[] counts, double[] binCenters, Dictionary<string, ChannelViewModel.FitData> fitResults)
        {
            double mu = 0, sigma = 0, peak = 0;
            double fwhm = 0, resolution = 0;


            // Use Primary Fit for Statistics (Priority: HEMG-D > HEMG-S > Gaussian)
            // Fallback: Calculate basic moments from data
            var moments = _mathService.CalculateMoments(binCenters, counts);
            mu = moments.mean;
            sigma = moments.sigma;
            peak = moments.peak;
            fwhm = 2.355 * sigma;
            resolution = (Math.Abs(mu) > 1e-9) ? (fwhm / mu * 100.0) : 0;

            // Refine stats from the "Best" available fit curve
            double[]? bestFitCurve = null;
            if (fitResults.TryGetValue("HEMG-D", out ChannelViewModel.FitData? value)) bestFitCurve = value.Curve;
            else if (fitResults.TryGetValue("HEMG-S", out ChannelViewModel.FitData? value1)) bestFitCurve = value1.Curve;
            else if (fitResults.TryGetValue("Gaussian", out ChannelViewModel.FitData? value2)) bestFitCurve = value2.Curve;

            if (bestFitCurve != null && bestFitCurve.Length > 0)
            {
                // Refine FWHM from the fit curve
                (fwhm, resolution) = CalculateFWHM(binCenters, bestFitCurve, mu);
            }

            // Set channel data
            var chVM = Channels[chIndex];
            chVM.BinCenters = binCenters;
            chVM.RawCounts = counts;
            chVM.ActiveFits = fitResults; // Assign the multi-fit dictionary

            bool isLogScale = (SelectedBaselineMode == 2 || SelectedBaselineMode == 3);
            chVM.IsLogScale = isLogScale;

            if (isLogScale)
            {
                chVM.Counts = [.. counts.Select(c => c > 0 ? Math.Log10(c) : 0)];
                // Update fit curves to log scale
                foreach (var key in fitResults.Keys.ToList())
                {
                    var fit = fitResults[key];
                    if (fit.Curve != null)
                    {
                        fit.Curve = [.. fit.Curve.Select(c => c > 0 ? Math.Log10(c) : 0)];
                    }
                }
            }
            else
            {
                chVM.Counts = counts;
            }

            chVM.Mu = mu;
            chVM.Sigma = sigma;
            chVM.Peak = peak;
            chVM.FWHM = fwhm;
            chVM.Resolution = resolution;
            chVM.StatsText = $"μ={mu:F2}, σ={sigma:F2}, FWHM={fwhm:F2}, Res={resolution:F2}%";

            // Trigger Plot Refresh
            var figBg = ToDrawingColor(GraphFigureColor);
            var dataBg = ToDrawingColor(GraphDataColor);
            var foreColor = ToDrawingColor(GraphTextColor);
            var seriesColor = ToDrawingColor(GraphSeriesColor);

            chVM.RenderPlot(figBg, dataBg, foreColor, seriesColor);
        }

        private static System.Drawing.Color ToDrawingColor(System.Windows.Media.Color mediaColor)
        {
            return System.Drawing.Color.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B);
        }

        private static (double fwhm, double resolution) CalculateFWHM(double[] binCenters, double[] fitCurve, double mu)
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


        // Helper function - Interpolate fit curve to match bin centers
        private double[] InterpolateFitCurve(double[] fitBins, double[] fitCurve, double[] targetBins)
        {
            if (fitBins.Length != fitCurve.Length || fitBins.Length == 0)
                return fitCurve;

            double[] result = new double[targetBins.Length];

            for (int i = 0; i < targetBins.Length; i++)
            {
                double target = targetBins[i];

                // Find nearest fit bin
                int nearestIdx = 0;
                double minDist = Math.Abs(fitBins[0] - target);

                for (int j = 1; j < fitBins.Length; j++)
                {
                    double dist = Math.Abs(fitBins[j] - target);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearestIdx = j;
                    }
                }

                result[i] = fitCurve[nearestIdx];
            }

            return result;
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
            if (ProcessedData == null || ProcessedData.Count == 0)
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


                var selector = GetLayerSelector();

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
            var window = new BaselineMode.WPF.Views.Baseline.ChannelDetailWindow
            {
                MainVM = this,
                DataContext = channel
            };
            window.Show();
        }

        [RelayCommand]
        private void ShowHeatmap()
        {
            if (ProcessedData == null || ProcessedData.Count == 0)
            {
                StatusMessage = "No data to plot heatmap.";
                return;
            }

            IsBusy = true;
            StatusMessage = "Calculating Heatmap...";

            _ = Task.Run(() =>
            {
                try
                {
                    var matrix = CalculateCoincidenceMatrix();

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var vm = new HeatmapViewModel(matrix);
                        var window = new BaselineMode.WPF.Views.Baseline.HeatmapWindow
                        {
                            DataContext = vm
                        };
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