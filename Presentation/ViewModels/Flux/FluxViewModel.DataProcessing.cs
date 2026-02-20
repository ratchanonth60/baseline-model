using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using BaselineMode.WPF.Core.Helpers;
using BaselineMode.WPF.Core.Models.Flux;
using BaselineMode.WPF.Infrastructure.Services;

namespace BaselineMode.WPF.Presentation.ViewModels.Flux
{
    /// <summary>
    /// Hex parsing, header processing, flux calculation, and plot updates.
    /// </summary>
    public partial class FluxViewModel
    {
        private void ProcessFluxObservation(string hexString)
        {
            if (hexString.Length < 4064) return; // 2032 bytes * 2 chars

            ReadOnlySpan<char> hexSpan = hexString.AsSpan();

            // Time in seconds from offset bytes 16–17 (hex index 32–35)
            int t0 = int.Parse(hexSpan.Slice(32, 2), NumberStyles.HexNumber);
            int t1 = int.Parse(hexSpan.Slice(34, 2), NumberStyles.HexNumber);
            double milliseconds = (t0 << 8) + t1;
            double timeSeconds = milliseconds / 1000.0;

            // Particle counting: bytes 18–31 (7 layers × 2 bytes), hex start 36
            double[] particleCounting = new double[7];
            for (int i = 0; i < 7; i++)
            {
                int idx = 36 + (i * 4);
                int p0 = int.Parse(hexSpan.Slice(idx, 2), NumberStyles.HexNumber);
                int p1 = int.Parse(hexSpan.Slice(idx + 2, 2), NumberStyles.HexNumber);
                particleCounting[i] = (p0 << 8) + p1;
            }

            // Particle info: bytes 32–2031 (1000 × 2 bytes), hex start 64
            double[] particleLayer = new double[1000];
            double[] particleOffsetTime = new double[1000];
            for (int i = 0; i < 1000; i++)
            {
                int idx = 64 + (i * 4);
                int highByte = int.Parse(hexSpan.Slice(idx, 2), NumberStyles.HexNumber);
                int lowByte = int.Parse(hexSpan.Slice(idx + 2, 2), NumberStyles.HexNumber);
                int value = (highByte << 8) | lowByte;
                particleLayer[i] = (value >> 13) & 0x07;
                particleOffsetTime[i] = value & 0x0FFF;
            }

            _secondsPartList.Add(timeSeconds);
            for (int i = 0; i < 7; i++)
                _particleCountingLists[i].Add(particleCounting[i]);
            _particleLayerList.Add(particleLayer);
            _particleOffsetTimeList.Add(particleOffsetTime);
            _allResults.Add(new FluxDataResult
            {
                TimeSeconds = timeSeconds,
                ParticleCounting = particleCounting,
                ParticleLayer = particleLayer,
                ParticleOffsetTime = particleOffsetTime
            });
        }

        private static DateTime GetDateTimeFromHexData(string hexString)
        {
            try
            {
                ReadOnlySpan<char> hexSpan = hexString.AsSpan();
                if (hexSpan.Length < 28) return DateTime.MinValue;

                byte[] timecodeDec = new byte[6];
                for (int i = 0; i < 6; i++)
                    timecodeDec[i] = byte.Parse(hexSpan.Slice(16 + i * 2, 2), NumberStyles.HexNumber);

                var secondsPart = BitConverter.ToUInt32([.. timecodeDec.Take(4).Reverse()], 0);
                var millisecondsPart = BitConverter.ToUInt16([.. timecodeDec.Skip(4).Take(2).Reverse()], 0);
                return DateTimeOffset.FromUnixTimeSeconds(secondsPart).AddMilliseconds(millisecondsPart).DateTime;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private void ProcessHeader(string hexString)
        {
            try
            {
                if (hexString.Length < 4128) return;
                var hexData = _dataProcessor.SplitHexData(hexString);
                ProcessHeaderInternal(hexData);
            }
            catch
            {
                HeaderInfo = "Error parsing header";
            }
        }

        private void ProcessHeaderInternal(string[] hexData)
        {
            try
            {
                if (hexData.Length < 2064) return;

                string packetSync = $"Packet Synchronization Code: {hexData[0]} {hexData[1]}";
                string packageId = $"Package Identification: {hexData[2]} {hexData[3]}";
                string packetSeq = $"Packet Sequence: {hexData[4]} {hexData[5]}";
                string packetData = $"Packet data length: {hexData[6]} {hexData[7]}";

                var timecodeHex = hexData.Skip(8).Take(6).ToArray();
                var timecodeDec = timecodeHex.Select(h => Convert.ToByte(h, 16)).ToArray();
                uint secPart = BitConverter.ToUInt32([.. timecodeDec.Take(4).Reverse()], 0);
                ushort msPart = BitConverter.ToUInt16([.. timecodeDec.Skip(4).Reverse()], 0);
                DateTime dt = DateTimeOffset.FromUnixTimeSeconds(secPart).AddMilliseconds(msPart).UtcDateTime;
                string timestamp = $"Timestamp: {dt:yyyy-MMM-dd HH:mm:ss.fff}";

                string dataType = $"Data Type: {hexData[14]} {hexData[15]}";
                string checkSumHex = $"Check Sum: {hexData[2062]} {hexData[2063]}";

                int totalSum = hexData.Skip(8).Take(2054).Select(h => Convert.ToInt32(h, 16)).Sum();
                int lastTwoBytes = totalSum % 65536;
                string checksumCalc = lastTwoBytes.ToString("X4");
                string checksumFromData = hexData[2062] + hexData[2063];
                string checksumResult = checksumCalc.Equals(checksumFromData, StringComparison.OrdinalIgnoreCase)
                    ? "Checksum matches!" : "Checksum does not match.";

                string testConditions = $"Test condition:\nDelay Time: {DelayTime}\nThreshold: {Threshold}";

                HeaderInfo = string.Join("\n",
                    packetSync, packageId, packetSeq, packetData,
                    timestamp, dataType, checkSumHex, checksumResult, testConditions);
            }
            catch
            {
                HeaderInfo = "Error parsing header";
            }
        }

        private void CalculateAndPlotFlux()
        {
            if (_secondsPartList.Count == 0) return;

            int count = _secondsPartList.Count;
            double[] cumulativeTime = new double[count];
            double[] timeSeconds = [.. _secondsPartList];

            cumulativeTime[0] = timeSeconds[0];
            for (int i = 1; i < count; i++)
                cumulativeTime[i] = cumulativeTime[i - 1] + timeSeconds[i];

            for (int layer = 0; layer < LayerCount; layer++)
            {
                double[] particleCounting = [.. _particleCountingLists[layer]];
                var xPoints = new List<double>(count);
                var yPoints = new List<double>(count);
                double maxFlux = 0;

                for (int j = 0; j < count; j++)
                {
                    double t = timeSeconds[j];
                    double flux = t > 0 ? particleCounting[j] / (t * DetectorAreaM2) : 0;

                    if (!double.IsNaN(flux) && !double.IsInfinity(flux))
                    {
                        xPoints.Add(cumulativeTime[j]);
                        yPoints.Add(flux);
                        if (flux > maxFlux) maxFlux = flux;
                    }
                }

                if (xPoints.Count > 0)
                {
                    Layers[layer].XData = [.. xPoints];
                    Layers[layer].YData = [.. yPoints];
                    Layers[layer].StatsText = $"Points: {xPoints.Count:N0} | Max: {maxFlux:F2}";
                }
                else
                {
                    Layers[layer].XData = null;
                    Layers[layer].YData = null;
                    Layers[layer].StatsText = "No valid flux data";
                }
            }

            UpdateAllPlots();
        }

        private void UpdateAllPlots()
        {
            if (Layers == null || Layers.Count == 0) return;

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var figBg = ColorHelper.ToDrawingColor(GraphFigureColor, System.Drawing.Color.FromArgb(30, 30, 30));
                var dataBg = ColorHelper.ToDrawingColor(GraphDataColor, System.Drawing.Color.FromArgb(37, 37, 38));
                var fgColor = ColorHelper.ToDrawingColor(GraphTextColor, System.Drawing.Color.White);
                var seriesColor = ColorHelper.ToDrawingColor(GraphSeriesColor, System.Drawing.Color.Cyan);

                for (int i = 0; i < Layers.Count; i++)
                {
                    Layers[i].RenderPlot(
                        figBg, dataBg, fgColor, seriesColor,
                        isLogScale: IsLogScale,
                        xMin: TimeRangeMin,
                        xMax: TimeRangeMax > 0 ? TimeRangeMax : null,
                        widthMultiplier: BarWidthMultiplier);
                }
            });
        }

        private void ResetDataLists()
        {
            _secondsPartList.Clear();
            foreach (var list in _particleCountingLists) list.Clear();
            _particleLayerList.Clear();
            _particleOffsetTimeList.Clear();
            _allResults.Clear();
        }
    }
}
