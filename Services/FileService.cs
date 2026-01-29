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
    public class FileService : IFileService
    {
        // Constants
        private const double VOLTAGE_FACTOR = (5.0 / 16383.0) * 1000.0;
        private const int CHUNK_SIZE = 4128;
        private const int SAMPLES_PER_SEGMENT = 15;
        private const int CHANNELS = 16;
        private const int BUFFER_SIZE = 64; // size for l1l2Dec and l6l7Dec

        // Regex อาจจะไม่จำเป็นถ้าเราใช้ Span Parsing (ซึ่งเร็วกว่า) แต่เก็บไว้สำหรับ clean whitespace ได้
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        private bool _disposed = false;

        public FileService()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        // ---------------------------------------------------------
        // 1. Parsing + Processing แบบ Streaming (True Zero-Allocation Logic)
        // ---------------------------------------------------------

        // รวม Parse และ Process ไว้ด้วยกัน หรือรับเป็น IEnumerable เพื่อไม่ต้องรอโหลดเสร็จทั้งไฟล์
        public List<BaselineData> ProcessFileStream(string filePath, IProgress<double>? progress = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FileService));
            
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));
            
            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found", filePath);
            
            // Estimate initial capacity to reduce List resizing
            long fileSize = new FileInfo(filePath).Length;
            int estimatedCapacity = (int)Math.Min(fileSize / (CHUNK_SIZE * 2), 100000);
            var results = new List<BaselineData>(estimatedCapacity);

            // ใช้ StreamReader เพื่ออ่านทีละส่วน ไม่โหลดทั้งไฟล์
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 131072))
            using (var sr = new StreamReader(fs, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 131072))
            {
                // Buffer สำหรับเก็บข้อมูลที่อ่านมา (ให้ใหญ่พอสมควร)
                char[] fileBuffer = new char[131072];

                // Buffer สำหรับสะสม string hex ที่ clean แล้ว (ต้องใหญ่กว่า CHUNK_SIZE)
                StringBuilder hexAccumulator = new StringBuilder(CHUNK_SIZE * 4);

                int charsRead;
                long totalBytes = fs.Length;
                long processedBytes = 0;

                // Pool สำหรับ ProcessHex (Reused)
                var arrayPool = System.Buffers.ArrayPool<int>.Shared;
                int[] l1l2Dec = arrayPool.Rent(BUFFER_SIZE);
                int[] l6l7Dec = arrayPool.Rent(BUFFER_SIZE);

                try
                {
                    while ((charsRead = sr.Read(fileBuffer, 0, fileBuffer.Length)) > 0)
                    {
                        processedBytes += charsRead;

                        // ลูปกรอง Whitespace และสะสมตัวอักษร
                        for (int i = 0; i < charsRead; i++)
                        {
                            char c = fileBuffer[i];
                            // กรองเอาเฉพาะ 0-9, A-F, a-f
                            if (IsHexChar(c))
                            {
                                hexAccumulator.Append(c);
                            }
                        }

                        // ถ้าสะสมครบ หรือ เกิน CHUNK_SIZE แล้ว ให้ตัดมา Process
                        ProcessAccumulatedHex(hexAccumulator, results, l1l2Dec, l6l7Dec);

                        // Report Progress
                        if (progress != null && results.Count % 1000 == 0)
                        {
                            progress.Report((double)processedBytes / totalBytes * 100);
                        }
                    }

                    // Process ส่วนที่เหลือ (ถ้ามี)
                    ProcessAccumulatedHex(hexAccumulator, results, l1l2Dec, l6l7Dec, force: true);
                }
                finally
                {
                    // Clear sensitive data before returning to pool
                    Array.Clear(l1l2Dec, 0, l1l2Dec.Length);
                    Array.Clear(l6l7Dec, 0, l6l7Dec.Length);
                    Array.Clear(fileBuffer, 0, fileBuffer.Length);
                    
                    arrayPool.Return(l1l2Dec);
                    arrayPool.Return(l6l7Dec);
                    
                    // Clear string builder
                    hexAccumulator.Clear();
                }
            }

            return results;
        }

        private void ProcessAccumulatedHex(StringBuilder sb, List<BaselineData> results, int[] l1l2Dec, int[] l6l7Dec, bool force = false)
        {
            // Pattern: E225... (Length = CHUNK_SIZE)
            // เราจะวนลูปหา E225 แล้วตัดออกมา Process

            string bufferStr = sb.ToString();
            int searchIndex = 0;

            while (searchIndex < bufferStr.Length)
            {
                // หา header "E225"
                int headerIndex = bufferStr.IndexOf("E225", searchIndex, StringComparison.OrdinalIgnoreCase);

                if (headerIndex == -1)
                {
                    // ไม่เจอ Header เลย
                    // ถ้า force (จบไฟล์) ก็เคลียร์ทิ้ง ถ้ายังไม่จบ เก็บเศษไว้รอรอบหน้า
                    if (force) sb.Clear();
                    else
                    {
                        // เก็บส่วนท้ายที่อาจจะเป็น Header ไม่ครบไว้ (เช่นเจอ E2..)
                        // เพื่อความง่าย ตัดทิ้งเหลือ 0 หรือเก็บ 3 ตัวท้าย (กรณี E, 2, 2 อยู่ท้าย)
                        // แต่ Logic นี้ซับซ้อน เอาแบบง่ายคือ remove ส่วนที่ process แล้วออก
                        sb.Remove(0, searchIndex);
                    }
                    return;
                }

                // เจอ Header เช็คว่ามีข้อมูลพอไหม
                if (headerIndex + CHUNK_SIZE <= bufferStr.Length)
                {
                    // **Process ตรงนี้เลย ไม่ต้องสร้าง List<string>**
                    // ใช้ AsSpan เพื่อลด allocation ตอน substring
                    ReadOnlySpan<char> segmentSpan = bufferStr.AsSpan(headerIndex, CHUNK_SIZE);

                    ProcessSingleSegment(segmentSpan, results, l1l2Dec, l6l7Dec);

                    // ขยับ index ไปหาตัวถัดไป
                    searchIndex = headerIndex + CHUNK_SIZE;
                }
                else
                {
                    // เจอ Header แต่ข้อมูลยังไม่ครบ CHUNK_SIZE (รอรอบหน้า)
                    // ลบส่วนที่ process ไปแล้วออก
                    sb.Remove(0, searchIndex);
                    return;
                }
            }

            // ลบทั้งหมดถ้า process จบพอดี
            sb.Remove(0, searchIndex);
        }

        // แยก Logic ออกมาทำทีละ Segment
        private void ProcessSingleSegment(ReadOnlySpan<char> segmentSpan, List<BaselineData> results, int[] l1l2Dec, int[] l6l7Dec)
        {
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
        private bool IsHexChar(char c)
        {
            return (c >= '0' && c <= '9') ||
                   (c >= 'A' && c <= 'F') ||
                   (c >= 'a' && c <= 'f');
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ExtractSamplingPacket(ReadOnlySpan<char> hexDataSpan)
        {
            int byte1 = HexCharToInt(hexDataSpan[32]) * 16 + HexCharToInt(hexDataSpan[33]);
            int byte2 = HexCharToInt(hexDataSpan[34]) * 16 + HexCharToInt(hexDataSpan[35]);
            return (byte1 << 8) | byte2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ParseHexToSpan(ReadOnlySpan<char> hexDataSpan, int startOffset, int byteCount, Span<int> output)
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

        public void SaveToExcel(List<BaselineData> dataList, string filePath, IProgress<double>? progress = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FileService));
            
            if (dataList == null)
                throw new ArgumentNullException(nameof(dataList));
            
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));
            
            // Ensure directory exists
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Delete if exists to overwrite
            if (File.Exists(filePath))
            {
                try 
                { 
                    File.Delete(filePath); 
                }
                catch 
                { 
                    // File might be locked, EPPlus will handle
                }
            }

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

        public List<BaselineData> ReadExcelFile(string filePath, IProgress<double>? progress = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FileService));
            
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));
            
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Excel file not found", filePath);
            
            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                var ws = package.Workbook.Worksheets[0];
                if (ws.Dimension == null) return new List<BaselineData>();

                int rowCount = ws.Dimension.Rows;
                int colCount = ws.Dimension.Columns;
                int dataRows = rowCount - 1;

                if (dataRows <= 0)
                {
                    MessageBoxService.Show($"Excel file found but appears empty (Rows: {rowCount}).", "Read Excel Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return new List<BaselineData>();
                }

                // Debug Message
                // MessageBoxService.Show($"Found {dataRows} data rows in Excel.", "Debug", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

                // Load all data into memory at once
                var rawValues = ws.Cells[2, 1, rowCount, colCount].Value as object[,];
                
                if (rawValues == null)
                {
                    MessageBoxService.Show("Unable to read Excel data.", "Read Excel Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return new List<BaselineData>();
                }

                // Pre-allocate with exact capacity
                var results = new List<BaselineData>(dataRows);
                
                for (int r = 0; r < dataRows; r++)
                {
                    var data = new BaselineData();

                    // Direct array access (High Performance) - with null checks
                    data.SamplingPacketNo = rawValues[r, 0] != null ? Convert.ToInt32(rawValues[r, 0]) : 0;
                    data.SamplingNo = rawValues[r, 1] != null ? Convert.ToInt32(rawValues[r, 1]) : 0;

                    int c = 2;
                    // Optimized: Read all layers in a single loop pass to improve cache locality
                    // Read L1
                    for (int i = 0; i < CHANNELS; i++)
                    {
                        int val = rawValues[r, c] != null ? Convert.ToInt32(rawValues[r, c]) : 0;
                        data.L1[i] = val;
                        data.L1_Voltage[i] = val * VOLTAGE_FACTOR;
                        c++;
                    }

                    // Read L2
                    for (int i = 0; i < CHANNELS; i++)
                    {
                        int val = rawValues[r, c] != null ? Convert.ToInt32(rawValues[r, c]) : 0;
                        data.L2[i] = val;
                        data.L2_Voltage[i] = val * VOLTAGE_FACTOR;
                        c++;
                    }

                    // Read L6
                    for (int i = 0; i < CHANNELS; i++)
                    {
                        int val = rawValues[r, c] != null ? Convert.ToInt32(rawValues[r, c]) : 0;
                        data.L6[i] = val;
                        data.L6_Voltage[i] = val * VOLTAGE_FACTOR;
                        c++;
                    }

                    // Read L7
                    for (int i = 0; i < CHANNELS; i++)
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
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Managed resources cleanup (if any)
                    // Currently no managed resources to dispose
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}