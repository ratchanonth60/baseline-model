using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using BaselineMode.WPF.Models;
using OfficeOpenXml;

namespace BaselineMode.WPF.Services
{
    public class FileService
    {
        // Pre-compiled regex for better performance
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex E225Regex = new Regex(@"E225[0-9A-F]+", RegexOptions.Compiled);

        // Pre-computed constants
        private const double VOLTAGE_FACTOR = (5.0 / 16383.0) * 1000.0;
        private const int CHUNK_SIZE = 4128;
        private const int SAMPLES_PER_SEGMENT = 15;
        private const int CHANNELS = 16;

        public FileService()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        public List<string> ParseRawTextFile(string filePath)
        {
            // Read file with optimized buffer size
            var content = File.ReadAllText(filePath, Encoding.ASCII);

            // Remove whitespace in one pass
            var cleanedData = WhitespaceRegex.Replace(content, string.Empty);

            // Find all matches
            var matches = E225Regex.Matches(cleanedData);

            // Pre-allocate list with estimated capacity
            var segments = new List<string>(matches.Count * 2);

            foreach (Match match in matches)
            {
                var segment = match.Value;
                int segmentLength = segment.Length;

                // Process chunks
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

        public List<BaselineData> ProcessHexSegments(List<string> segments)
        {
            // Pre-allocate with exact capacity
            var results = new List<BaselineData>(segments.Count * SAMPLES_PER_SEGMENT);

            // Reusable buffers to avoid allocations
            int[] l1l2Dec = new int[64];
            int[] l6l7Dec = new int[64];

            foreach (var hexDataStr in segments)
            {
                // Extract sampling packet number once
                int samplingPacket = ExtractSamplingPacket(hexDataStr);

                for (int i = 0; i < SAMPLES_PER_SEGMENT; i++)
                {
                    var data = new BaselineData
                    {
                        SamplingPacketNo = samplingPacket,
                        SamplingNo = i + 1,
                    };

                    // Calculate offsets
                    int l1l2Offset = 18 + 64 * i * 2; // *2 because each byte is 2 hex chars
                    int l6l7Offset = 978 + 64 * i * 2;

                    // Parse hex directly without creating intermediate arrays
                    if (!ParseHexToInts(hexDataStr, l1l2Offset, 64, l1l2Dec) ||
                        !ParseHexToInts(hexDataStr, l6l7Offset, 64, l6l7Dec))
                    {
                        continue;
                    }

                    // Process all 16 channels
                    ProcessChannels(data, l1l2Dec, l6l7Dec);

                    results.Add(data);
                }
            }

            return results;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ExtractSamplingPacket(string hexDataStr)
        {
            // Position 16*2 = 32, take 2 bytes (4 chars)
            int byte1 = HexCharToInt(hexDataStr[32]) * 16 + HexCharToInt(hexDataStr[33]);
            int byte2 = HexCharToInt(hexDataStr[34]) * 16 + HexCharToInt(hexDataStr[35]);
            return (byte1 << 8) | byte2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ParseHexToInts(string hexStr, int startOffset, int byteCount, int[] output)
        {
            if (startOffset + byteCount * 2 > hexStr.Length)
                return false;

            for (int i = 0; i < byteCount; i++)
            {
                int pos = startOffset + i * 2;
                output[i] = HexCharToInt(hexStr[pos]) * 16 + HexCharToInt(hexStr[pos + 1]);
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
        private void ProcessChannels(BaselineData data, int[] l1l2Dec, int[] l6l7Dec)
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

        public void SaveToExcel(List<BaselineData> dataList, string filePath)
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

                // Write data in bulk
                WriteDataRows(ws, dataList);

                package.Save();
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteDataRows(ExcelWorksheet ws, List<BaselineData> dataList)
        {
            int rowCount = dataList.Count;

            for (int i = 0; i < rowCount; i++)
            {
                var item = dataList[i];
                int row = i + 2;

                ws.Cells[row, 1].Value = item.SamplingPacketNo;
                ws.Cells[row, 2].Value = item.SamplingNo;

                int col = 3;

                // Write all L1 values
                for (int j = 0; j < CHANNELS; j++)
                    ws.Cells[row, col++].Value = item.L1[j];

                // Write all L2 values
                for (int j = 0; j < CHANNELS; j++)
                    ws.Cells[row, col++].Value = item.L2[j];

                // Write all L6 values
                for (int j = 0; j < CHANNELS; j++)
                    ws.Cells[row, col++].Value = item.L6[j];

                // Write all L7 values
                for (int j = 0; j < CHANNELS; j++)
                    ws.Cells[row, col++].Value = item.L7[j];
            }
        }

        public List<BaselineData> ReadExcelFile(string filePath)
        {
            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                var ws = package.Workbook.Worksheets[0];
                int rowCount = ws.Dimension.Rows;

                // Pre-allocate with exact capacity
                var results = new List<BaselineData>(rowCount - 1);

                // Parallel processing for large files (optional - remove if ordering is critical)
                // For sequential processing, use regular loop
                for (int row = 2; row <= rowCount; row++)
                {
                    var data = ReadDataRow(ws, row);
                    results.Add(data);
                }

                return results;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private BaselineData ReadDataRow(ExcelWorksheet ws, int row)
        {
            var data = new BaselineData
            {
                SamplingPacketNo = Convert.ToInt32(ws.Cells[row, 1].Value),
                SamplingNo = Convert.ToInt32(ws.Cells[row, 2].Value)
            };

            int col = 3;

            // Read L1
            for (int i = 0; i < CHANNELS; i++)
            {
                int val = Convert.ToInt32(ws.Cells[row, col++].Value);
                data.L1[i] = val;
                data.L1_Voltage[i] = val * VOLTAGE_FACTOR;
            }

            // Read L2
            for (int i = 0; i < CHANNELS; i++)
            {
                int val = Convert.ToInt32(ws.Cells[row, col++].Value);
                data.L2[i] = val;
                data.L2_Voltage[i] = val * VOLTAGE_FACTOR;
            }

            // Read L6
            for (int i = 0; i < CHANNELS; i++)
            {
                int val = Convert.ToInt32(ws.Cells[row, col++].Value);
                data.L6[i] = val;
                data.L6_Voltage[i] = val * VOLTAGE_FACTOR;
            }

            // Read L7
            for (int i = 0; i < CHANNELS; i++)
            {
                int val = Convert.ToInt32(ws.Cells[row, col++].Value);
                data.L7[i] = val;
                data.L7_Voltage[i] = val * VOLTAGE_FACTOR;
            }

            return data;
        }
    }
}