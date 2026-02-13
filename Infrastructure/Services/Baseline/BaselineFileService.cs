using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using BaselineMode.WPF.Core.Models;
using BaselineMode.WPF.Core.Models.Baseline;
using BaselineMode.WPF.Core.Models.Shared;
using BaselineMode.WPF.Core.Interfaces;
using OfficeOpenXml;

namespace BaselineMode.WPF.Infrastructure.Services.Baseline
{
    public class BaselineFileService : IFileService
    {
        // Constants
        private const float VOLTAGE_FACTOR = (5.0f / 16383.0f) * 1000.0f;
        private const int CHUNK_SIZE = 4128;
        private const int SAMPLES_PER_SEGMENT = 15;
        private const int CHANNELS = 16;
        private const int BUFFER_SIZE = 64; // size for l1l2Dec and l6l7Dec

        // RegexShared from RegexPatterns
        private static readonly Regex WhitespaceRegex = RegexPatterns.Whitespace();

        private bool _disposed = false;

        public BaselineFileService()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        // ---------------------------------------------------------
        // 1. Parsing + Processing Streaming (True Zero-Allocation Logic)
        // ---------------------------------------------------------

        public List<BaselineData> ProcessFileStream(string filePath, IProgress<double>? progress = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BaselineFileService));

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found", filePath);

            // Estimate initial capacity to reduce List resizing
            long fileSize = new FileInfo(filePath).Length;
            int estimatedCapacity = (int)Math.Min(fileSize / (CHUNK_SIZE * 2), 100000);
            var results = new List<BaselineData>(estimatedCapacity);

            // Use StreamReader
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 131072))
            using (var sr = new StreamReader(fs, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 131072))
            {
                char[] fileBuffer = new char[131072];
                StringBuilder hexAccumulator = new(CHUNK_SIZE * 4);

                int charsRead;
                long totalBytes = fs.Length;
                long processedBytes = 0;

                var arrayPool = System.Buffers.ArrayPool<int>.Shared;
                int[] l1l2Dec = arrayPool.Rent(BUFFER_SIZE);
                int[] l6l7Dec = arrayPool.Rent(BUFFER_SIZE);

                try
                {
                    while ((charsRead = sr.Read(fileBuffer, 0, fileBuffer.Length)) > 0)
                    {
                        processedBytes += charsRead;

                        for (int i = 0; i < charsRead; i++)
                        {
                            char c = fileBuffer[i];
                            if (IsHexChar(c))
                            {
                                hexAccumulator.Append(c);
                            }
                        }

                        ProcessAccumulatedHex(hexAccumulator, results, l1l2Dec, l6l7Dec);

                        if (progress != null && results.Count % 1000 == 0)
                        {
                            progress.Report((double)processedBytes / totalBytes * 100);
                        }
                    }

                    ProcessAccumulatedHex(hexAccumulator, results, l1l2Dec, l6l7Dec, force: true);
                }
                finally
                {
                    Array.Clear(l1l2Dec, 0, l1l2Dec.Length);
                    Array.Clear(l6l7Dec, 0, l6l7Dec.Length);
                    Array.Clear(fileBuffer, 0, fileBuffer.Length);

                    arrayPool.Return(l1l2Dec);
                    arrayPool.Return(l6l7Dec);

                    hexAccumulator.Clear();
                }
            }

            return results;
        }

        private static void ProcessAccumulatedHex(StringBuilder sb, List<BaselineData> results, int[] l1l2Dec, int[] l6l7Dec, bool force = false)
        {
            string bufferStr = sb.ToString();
            int searchIndex = 0;

            while (searchIndex < bufferStr.Length)
            {
                int headerIndex = bufferStr.IndexOf(AppConstants.HeaderStart, searchIndex, StringComparison.OrdinalIgnoreCase);

                if (headerIndex == -1)
                {
                    if (force) sb.Clear();
                    else
                    {
                        sb.Remove(0, searchIndex);
                    }
                    return;
                }

                if (headerIndex + CHUNK_SIZE <= bufferStr.Length)
                {
                    ReadOnlySpan<char> segmentSpan = bufferStr.AsSpan(headerIndex, CHUNK_SIZE);
                    ProcessSingleSegment(segmentSpan, results, l1l2Dec, l6l7Dec);
                    searchIndex = headerIndex + CHUNK_SIZE;
                }
                else
                {
                    sb.Remove(0, searchIndex);
                    return;
                }
            }

            sb.Remove(0, searchIndex);
        }

        private static void ProcessSingleSegment(ReadOnlySpan<char> segmentSpan, List<BaselineData> results, int[] l1l2Dec, int[] l6l7Dec)
        {
            int samplingPacket = ExtractSamplingPacket(segmentSpan);

            for (int i = 0; i < SAMPLES_PER_SEGMENT; i++)
            {
                var data = new BaselineData
                {
                    SamplingPacketNo = samplingPacket,
                    SamplingNo = i + 1,
                };

                int l1l2Offset = 36 + 64 * i * 2;
                int l6l7Offset = 1956 + 64 * i * 2;

                if (!ParseHexToSpan(segmentSpan, l1l2Offset, BUFFER_SIZE, l1l2Dec) ||
                    !ParseHexToSpan(segmentSpan, l6l7Offset, BUFFER_SIZE, l6l7Dec))
                {
                    continue;
                }

                ProcessChannels(data, l1l2Dec, l6l7Dec);
                results.Add(data);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsHexChar(char c)
        {
            return (c >= '0' && c <= '9') ||
                   (c >= 'A' && c <= 'F') ||
                   (c >= 'a' && c <= 'f');
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ExtractSamplingPacket(ReadOnlySpan<char> hexDataSpan)
        {
            int byte1 = HexCharToInt(hexDataSpan[32]) * 16 + HexCharToInt(hexDataSpan[33]);
            int byte2 = HexCharToInt(hexDataSpan[34]) * 16 + HexCharToInt(hexDataSpan[35]);
            return (byte1 << 8) | byte2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ParseHexToSpan(ReadOnlySpan<char> hexDataSpan, int startOffset, int byteCount, Span<int> output)
        {
            if (startOffset + byteCount * 2 > hexDataSpan.Length) return false;
            for (int i = 0; i < byteCount; i++)
            {
                int pos = startOffset + i * 2;
                output[i] = HexCharToInt(hexDataSpan[pos]) * 16 + HexCharToInt(hexDataSpan[pos + 1]);
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HexCharToInt(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ProcessChannels(BaselineData data, Span<int> l1l2Dec, Span<int> l6l7Dec)
        {
            for (int j = 0; j < AppConstants.ChannelsPerLayer; j++)
            {
                int j2 = j * 2;
                int j2_32 = j2 + 32;

                // L1
                int l1Val = (l1l2Dec[j2] << 8) | l1l2Dec[j2 + 1];
                data.L1[j] = l1Val;
                data.L1_Voltage[j] = l1Val * VOLTAGE_FACTOR;

                // L2
                int l2Val = (l1l2Dec[j2_32] << 8) | l1l2Dec[j2_32 + 1];
                data.L2[j] = l2Val;
                data.L2_Voltage[j] = l2Val * VOLTAGE_FACTOR;

                // L6
                int l6Val = (l6l7Dec[j2] << 8) | l6l7Dec[j2 + 1];
                data.L6[j] = l6Val;
                data.L6_Voltage[j] = l6Val * VOLTAGE_FACTOR;

                // L7
                int l7Val = (l6l7Dec[j2_32] << 8) | l6l7Dec[j2_32 + 1];
                data.L7[j] = l7Val;
                data.L7_Voltage[j] = l7Val * VOLTAGE_FACTOR;
            }
        }

        public void SaveToExcel(List<BaselineData> dataList, string filePath, IProgress<double>? progress = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BaselineFileService));

            ArgumentNullException.ThrowIfNull(dataList);

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch
                {
                    // File might be locked
                }
            }

            using var package = new ExcelPackage(new FileInfo(filePath));
            var ws = package.Workbook.Worksheets.Add("Processed Data");

            WriteHeaders(ws);

            int rowCount = dataList.Count;
            if (rowCount > 0)
            {
                int colCount = 2 + (AppConstants.ChannelsPerLayer * 4);
                object[,] dataArray = new object[rowCount, colCount];

                for (int i = 0; i < rowCount; i++)
                {
                    var item = dataList[i];
                    dataArray[i, 0] = item.SamplingPacketNo;
                    dataArray[i, 1] = item.SamplingNo;

                    int c = 2;
                    for (int j = 0; j < AppConstants.ChannelsPerLayer; j++) dataArray[i, c++] = item.L1[j];
                    for (int j = 0; j < AppConstants.ChannelsPerLayer; j++) dataArray[i, c++] = item.L2[j];
                    for (int j = 0; j < AppConstants.ChannelsPerLayer; j++) dataArray[i, c++] = item.L6[j];
                    for (int j = 0; j < AppConstants.ChannelsPerLayer; j++) dataArray[i, c++] = item.L7[j];

                    if (progress != null && i % 1000 == 0)
                    {
                        progress.Report(((double)i / rowCount) * 100);
                    }
                }

                ws.Cells[2, 1].LoadFromArrays(ConvertArrayToEnumerable(dataArray));
            }

            package.Save();
        }

        private static IEnumerable<object[]> ConvertArrayToEnumerable(object[,] array)
        {
            int rows = array.GetLength(0);
            int cols = array.GetLength(1);
            for (int i = 0; i < rows; i++)
            {
                var row = new object[cols];
                for (int j = 0; j < cols; j++)
                {
                    row[j] = array[i, j];
                }
                yield return row;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteHeaders(ExcelWorksheet ws)
        {
            ws.Cells[1, 1].Value = "Sampling Packet No.";
            ws.Cells[1, 2].Value = "Sampling No.";

            int col = 3;

            for (int i = 1; i <= AppConstants.ChannelsPerLayer; i++)
            {
                ws.Cells[1, col++].Value = $"L1 CH{i}";
            }
            for (int i = 1; i <= AppConstants.ChannelsPerLayer; i++)
            {
                ws.Cells[1, col++].Value = $"L2 CH{i}";
            }
            for (int i = 1; i <= AppConstants.ChannelsPerLayer; i++)
            {
                ws.Cells[1, col++].Value = $"L6 CH{i}";
            }
            for (int i = 1; i <= AppConstants.ChannelsPerLayer; i++)
            {
                ws.Cells[1, col++].Value = $"L7 CH{i}";
            }
        }

        public List<BaselineData> ReadExcelFile(string filePath, IProgress<double>? progress = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BaselineFileService));

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Excel file not found", filePath);

            using var package = new ExcelPackage(new FileInfo(filePath));
            var ws = package.Workbook.Worksheets[0];
            if (ws.Dimension == null) return [];

            int rowCount = ws.Dimension.Rows;
            int colCount = ws.Dimension.Columns;
            int dataRows = rowCount - 1;

            if (dataRows <= 0)
            {
                MessageBoxService.Show($"Excel file found but appears empty (Rows: {rowCount}).", "Read Excel Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return [];
            }

            if (ws.Cells[2, 1, rowCount, colCount].Value is not object[,] rawValues)
            {
                MessageBoxService.Show("Unable to read Excel data.", "Read Excel Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return [];
            }

            var results = new List<BaselineData>(dataRows);

            for (int r = 0; r < dataRows; r++)
            {
                var data = new BaselineData
                {
                    SamplingPacketNo = rawValues[r, 0] != null ? Convert.ToInt32(rawValues[r, 0]) : 0,
                    SamplingNo = rawValues[r, 1] != null ? Convert.ToInt32(rawValues[r, 1]) : 0
                };

                int c = 2;
                for (int i = 0; i < AppConstants.ChannelsPerLayer; i++)
                {
                    int val = rawValues[r, c] != null ? Convert.ToInt32(rawValues[r, c]) : 0;
                    data.L1[i] = val;
                    data.L1_Voltage[i] = val * VOLTAGE_FACTOR;
                    c++;
                }
                for (int i = 0; i < AppConstants.ChannelsPerLayer; i++)
                {
                    int val = rawValues[r, c] != null ? Convert.ToInt32(rawValues[r, c]) : 0;
                    data.L2[i] = val;
                    data.L2_Voltage[i] = val * VOLTAGE_FACTOR;
                    c++;
                }
                for (int i = 0; i < AppConstants.ChannelsPerLayer; i++)
                {
                    int val = rawValues[r, c] != null ? Convert.ToInt32(rawValues[r, c]) : 0;
                    data.L6[i] = val;
                    data.L6_Voltage[i] = val * VOLTAGE_FACTOR;
                    c++;
                }
                for (int i = 0; i < AppConstants.ChannelsPerLayer; i++)
                {
                    int val = rawValues[r, c] != null ? Convert.ToInt32(rawValues[r, c]) : 0;
                    data.L7[i] = val;
                    data.L7_Voltage[i] = val * VOLTAGE_FACTOR;
                    c++;
                }

                results.Add(data);

                if (progress != null && (r % 1000 == 0))
                {
                    progress.Report(((double)(r) / dataRows) * 100);
                }
            }
            return results;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public string[]? OpenFileDialog(string filter, bool multiselect)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = filter,
                Multiselect = multiselect
            };

            if (openFileDialog.ShowDialog() == true)
            {
                return openFileDialog.FileNames;
            }
            return null;
        }
    }
}
