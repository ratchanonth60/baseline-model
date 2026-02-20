using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using BaselineMode.WPF.Core.Helpers;
using BaselineMode.WPF.Presentation.ViewModels.Shared;
using BaselineMode.WPF.Views.Calibration;
using CommunityToolkit.Mvvm.Input;

namespace BaselineMode.WPF.Presentation.ViewModels.Calibration
{
    /// <summary>
    /// Histogram plotting and zoom window for Calibration mode.
    /// </summary>
    public partial class CalibrationViewModel
    {
        partial void OnSelectedXAxisIndexChanged(int value)
        {
            if (value == 1)
            {
                XAxisMin = 0;
                XAxisMax = 5000;
            }
            else
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
            await Task.Run(UpdatePlots);
        }

        private void UpdatePlots()
        {
            var sourceColumns = SelectedXAxisIndex == 1
                ? GetVoltageColumns(SelectedLayerIndex)
                : GetCalibrationColumns(SelectedLayerIndex);

            const int channelCount = 16;
            double xMax = XAxisMax;
            double xMin = XAxisMin;
            string xLabel = SelectedXAxisIndex == 1 ? "Voltage (mV)" : "ADC Channel";

            var plotResults = new (double[]? counts, double[]? binCenters, string statsText, int channel)[channelCount];

            Parallel.For(0, channelCount, ch =>
            {
                var columnData = sourceColumns[ch];
                var dataForChannel = columnData.Where(d => d > 0).ToArray();

                if (dataForChannel.Length > 0)
                {
                    var (counts, binEdges) = ScottPlot.Statistics.Common.Histogram(
                        dataForChannel, min: xMin, max: xMax, binCount: 500);

                    double[] binCenters = new double[binEdges.Length - 1];
                    for (int k = 0; k < binCenters.Length; k++)
                        binCenters[k] = (binEdges[k] + binEdges[k + 1]) / 2.0;

                    plotResults[ch] = (counts, binCenters, $"Counts: {dataForChannel.Length:N0}", ch);
                }
            });

            Application.Current.Dispatcher.Invoke(() =>
            {
                for (int ch = 0; ch < channelCount; ch++)
                {
                    var (counts, binCenters, statsText, channel) = plotResults[ch];
                    if (counts != null && binCenters != null)
                    {
                        var channelVM = Channels[ch];
                        channelVM.Counts = counts;
                        channelVM.BinCenters = binCenters;
                        channelVM.StatsText = statsText;
                        channelVM.RenderPlot(
                            ColorHelper.ToDrawingColor(GraphFigureColor, Color.FromArgb(30, 30, 30)),
                            ColorHelper.ToDrawingColor(GraphDataColor, Color.FromArgb(37, 37, 38)),
                            ColorHelper.ToDrawingColor(GraphTextColor, Color.White),
                            ColorHelper.ToDrawingColor(GraphSeriesColor, Color.Cyan),
                            xMin: xMin,
                            xMax: xMax,
                            xLabel: xLabel
                        );
                    }
                }
            });
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

            if (_mathService is not Core.Interfaces.IFittingService fittingService) return;

            var window = new CalibrationDetailWindow(fittingService);
            string axisLabel = SelectedXAxisIndex == 1 ? "Voltage (mV)" : "ADC Channel";

            var figureBg = ColorHelper.ToDrawingColor(GraphFigureColor, Color.FromArgb(255, 30, 30, 30));
            var dataBg = ColorHelper.ToDrawingColor(GraphDataColor, Color.FromArgb(255, 37, 37, 38));
            var fgColor = ColorHelper.ToDrawingColor(GraphTextColor, Color.White);
            window.SetColorTheme(figureBg, dataBg, fgColor);

            var drawingColor = ColorHelper.ToDrawingColor(GraphSeriesColor, Color.Cyan);
            window.ShowHistogram(rawData, channel.Title, showFit: true, color: drawingColor, xLabel: axisLabel);
            window.Show();
        }
    }
}
