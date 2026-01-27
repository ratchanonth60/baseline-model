using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using BaselineMode.WPF.Models;
using OfficeOpenXml;

namespace BaselineMode.WPF.Services
{
    public class FileService
    {
        // Constants
        private const double VOLTAGE_FACTOR = (5.0 / 16383.0) * 1000.0;
        private const int CHUNK_SIZE = 4128;
        private const int SAMPLES_PER_SEGMENT = 15;
        private const int CHANNELS = 16;
        private const int BUFFER_SIZE = 64; // size for l1l2Dec and l6l7Dec

        // Regex อาจจะไม่จำเป็นถ้าเราใช้ Span Parsing (ซึ่งเร็วกว่า) แต่เก็บไว้สำหรับ clean whitespace ได้
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        public FileService()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        // ---------------------------------------------------------
        // 1. Parsing แบบ Zero-Allocation (สไตล์ Rust)
        // ---------------------------------------------------------

        // เปลี่ยน Return Type เป็น List<string> เหมือนเดิมเพื่อให้เข้ากับโค้ดส่วนอื่น 
        // แต่ภายในเราจะลด allocation ให้น้อยที่สุด
        public List<string> ParseRawTextFile(string filePath)
        {
            // Read file with optimized buffer size
            // Note: For extremely large files, FileStream with buffer is better, but ReadAllText is simplified here.
            string content = File.ReadAllText(filePath, Encoding.ASCII);

            // Manual whitespace cleaning logic could be here for zero-allocation,
            // but keeping Regex + Substring for now as it's not the crash cause.
            // The user focused on ProcessHexSegments optimization.
            var cleanedData = WhitespaceRegex.Replace(content, string.Empty);
            var matches = Regex.Matches(cleanedData, @"E225[0-9A-F]+");

            var segments = new List<string>(matches.Count * 2);

            foreach (Match match in matches)
            {
                var segment = match.Value;
                int segmentLength = segment.Length;

                for (int i = 0; i < segmentLength; i += CHUNK_SIZE)
                {
                    int remainingLength = segmentLength - i;
                    if (remainingLength >= CHUNK_SIZE)
                    {
                        segments.Add(segment.Substring(i, CHUNK_SIZE));
                    }
                }
            }

            return segments;
        }

        public List<BaselineData> ProcessHexSegments(List<string> segments, IProgress<double> progress = null)
        {
            // Pre-allocate with exact capacity
            var results = new List<BaselineData>(segments.Count * SAMPLES_PER_SEGMENT);

            // ✅ Zero-Allocation Optimization: Allocate ONCE on stack, reuse for all iterations.
            // This prevents Stack Overflow while maintaining high performance.
            // SAFE: Rent from ArrayPool<int> is thread-safe and re-entrant.
            var arrayPool = System.Buffers.ArrayPool<int>.Shared;
            int[] l1l2Dec = arrayPool.Rent(BUFFER_SIZE);
            int[] l6l7Dec = arrayPool.Rent(BUFFER_SIZE);
            try
            {
                int segmentIndex = 0;
                foreach (var segmentStr in segments)
                {
                    // Zero-copy span from string
                    ReadOnlySpan<char> segmentSpan = segmentStr.AsSpan();
                    // Extract Packet No
                    int samplingPacket = ExtractSamplingPacket(segmentSpan);

                    for (int i = 0; i < SAMPLES_PER_SEGMENT; i++)
                    {
                        var data = new BaselineData
                        {
                            SamplingPacketNo = samplingPacket,
                            SamplingNo = i + 1,
                        };

                        int l1l2Offset = 18 + 64 * i * 2;
                        int l6l7Offset = 978 + 64 * i * 2;

                        // Create spans for rented buffers
                        Span<int> l1l2Span = new Span<int>(l1l2Dec, 0, BUFFER_SIZE);
                        Span<int> l6l7Span = new Span<int>(l6l7Dec, 0, BUFFER_SIZE);

                        // Pass the reusable stack buffers to be filled
                        if (!ParseHexToSpan(segmentSpan, l1l2Offset, BUFFER_SIZE, l1l2Dec) ||
                            !ParseHexToSpan(segmentSpan, l6l7Offset, BUFFER_SIZE, l6l7Dec))
                        {
                            continue;
                        }

                        ProcessChannels(data, l1l2Dec, l6l7Dec);
                        results.Add(data);
                    }

                    if (progress != null && (++segmentIndex % 10 == 0))
                    {
                        double currentProgress = (double)segmentIndex / segments.Count * 100;
                        progress.Report(currentProgress);
                    }
                }
            }
            finally
            {
                arrayPool.Return(l1l2Dec);
                arrayPool.Return(l6l7Dec);
            }
            return results;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ExtractSamplingPacket(ReadOnlySpan<char> hexDataSpan)
        {
            // Position 16*2 = 32, take 2 bytes (4 chars)
            int byte1 = HexCharToInt(hexDataSpan[32]) * 16 + HexCharToInt(hexDataSpan[33]);
            int byte2 = HexCharToInt(hexDataSpan[34]) * 16 + HexCharToInt(hexDataSpan[35]);
            return (byte1 << 8) | byte2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ParseHexToSpan(ReadOnlySpan<char> hexDataSpan, int startOffset, int byteCount, Span<int> output)
        {
            if (startOffset + byteCount * 2 > hexDataSpan.Length)
                return false;

            for (int i = 0; i < byteCount; i++)
            {
                int pos = startOffset + i * 2;
                output[i] = HexCharToInt(hexDataSpan[pos]) * 16 + HexCharToInt(hexDataSpan[pos + 1]);
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int HexCharToInt(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessChannels(BaselineData data, Span<int> l1l2Dec, Span<int> l6l7Dec)
        {
            for (int j = 0; j < CHANNELS; j++)
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

        public void SaveToExcel(List<BaselineData> dataList, string filePath, IProgress<double> progress = null)
        {
            // Ensure directory exists
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Delete if exists to overwrite
            if (File.Exists(filePath))
                File.Delete(filePath);

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                var ws = package.Workbook.Worksheets.Add("Processed Data");

                // Build headers efficiently
                WriteHeaders(ws);

                // Write data in bulk using LoadFromArrays (High Performance)
                int rowCount = dataList.Count;
                if (rowCount > 0)
                {
                    // Create object array in memory
                    // Columns: 2 (Packet/Sample) + 64 (4 * 16 Channels) = 66
                    int colCount = 2 + (CHANNELS * 4);
                    object[,] dataArray = new object[rowCount, colCount];

                    for (int i = 0; i < rowCount; i++)
                    {
                        var item = dataList[i];
                        dataArray[i, 0] = item.SamplingPacketNo;
                        dataArray[i, 1] = item.SamplingNo;

                        int c = 2;
                        for (int j = 0; j < CHANNELS; j++) dataArray[i, c++] = item.L1[j];
                        for (int j = 0; j < CHANNELS; j++) dataArray[i, c++] = item.L2[j];
                        for (int j = 0; j < CHANNELS; j++) dataArray[i, c++] = item.L6[j];
                        for (int j = 0; j < CHANNELS; j++) dataArray[i, c++] = item.L7[j];

                        if (progress != null && i % 1000 == 0) // Report progress periodically
                        {
                            progress.Report(((double)i / rowCount) * 100);
                        }
                    }

                    // Write to Excel in one go
                    ws.Cells[2, 1].LoadFromArrays(ConvertArrayToEnumerable(dataArray));
                }

                package.Save();
            }
        }

        private IEnumerable<object[]> ConvertArrayToEnumerable(object[,] array)
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
        private void WriteHeaders(ExcelWorksheet ws)
        {
            ws.Cells[1, 1].Value = "Sampling Packet No.";
            ws.Cells[1, 2].Value = "Sampling No.";

            int col = 3;

            // Use StringBuilder for better performance with string concatenation
            for (int i = 1; i <= CHANNELS; i++)
            {
                ws.Cells[1, col++].Value = $"L1 CH{i}";
            }
            for (int i = 1; i <= CHANNELS; i++)
            {
                ws.Cells[1, col++].Value = $"L2 CH{i}";
            }
            for (int i = 1; i <= CHANNELS; i++)
            {
                ws.Cells[1, col++].Value = $"L6 CH{i}";
            }
            for (int i = 1; i <= CHANNELS; i++)
            {
                ws.Cells[1, col++].Value = $"L7 CH{i}";
            }
        }

        public List<BaselineData> ReadExcelFile(string filePath, IProgress<double> progress = null)
        {
            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                var ws = package.Workbook.Worksheets[0];
                if (ws.Dimension == null) return new List<BaselineData>();

                int rowCount = ws.Dimension.Rows;
                int colCount = ws.Dimension.Columns;
                int dataRows = rowCount - 1;

                // Load all data into memory at once
                var rawValues = ws.Cells[2, 1, rowCount, colCount].Value as object[,];

                // Pre-allocate with exact capacity
                var results = new List<BaselineData>(dataRows);

                for (int r = 0; r < dataRows; r++)
                {
                    var data = new BaselineData();

                    // Direct array access (High Performance)
                    data.SamplingPacketNo = Convert.ToInt32(rawValues[r, 0]);
                    data.SamplingNo = Convert.ToInt32(rawValues[r, 1]);

                    int c = 2;
                    // Read L1
                    for (int i = 0; i < CHANNELS; i++)
                    {
                        int val = Convert.ToInt32(rawValues[r, c++]);
                        data.L1[i] = val;
                        data.L1_Voltage[i] = val * VOLTAGE_FACTOR;
                    }

                    // Read L2
                    for (int i = 0; i < CHANNELS; i++)
                    {
                        int val = Convert.ToInt32(rawValues[r, c++]);
                        data.L2[i] = val;
                        data.L2_Voltage[i] = val * VOLTAGE_FACTOR;
                    }

                    // Read L6
                    for (int i = 0; i < CHANNELS; i++)
                    {
                        int val = Convert.ToInt32(rawValues[r, c++]);
                        data.L6[i] = val;
                        data.L6_Voltage[i] = val * VOLTAGE_FACTOR;
                    }

                    // Read L7
                    for (int i = 0; i < CHANNELS; i++)
                    {
                        int val = Convert.ToInt32(rawValues[r, c++]);
                        data.L7[i] = val;
                        data.L7_Voltage[i] = val * VOLTAGE_FACTOR;
                    }

                    results.Add(data);

                    if (progress != null && (r % 1000 == 0))
                    {
                        progress.Report(((double)(r) / dataRows) * 100);
                    }
                }

                return results;
            }
        }
    }
}