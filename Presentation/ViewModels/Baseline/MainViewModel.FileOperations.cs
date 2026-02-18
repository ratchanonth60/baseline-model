using System;
using System.Collections.Concurrent; // สำหรับ Parallel Partitioner
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Globalization;
using System.Buffers.Binary; // สำหรับการแปลง Byte แบบรวดเร็ว
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BaselineMode.WPF.Core.Models.Baseline;
using BaselineMode.WPF.Infrastructure.Services;
using BaselineMode.WPF.Presentation.ViewModels.Shared; // ปรับ Namespace ตามโครงสร้างโปรเจกต์จริง

namespace BaselineMode.WPF.Presentation.ViewModels.Baseline
{
    public partial class MainViewModel
    {
        // ---------------------------------------------------------
        // 1. Directory & File Selection (Optimized Merging)
        // ---------------------------------------------------------

        [RelayCommand]
        private void BrowseOutputDirectory()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Output Root Folder"
            };
            if (dialog.ShowDialog() == true)
            {
                OutputDirectoryPath = dialog.FolderName;
            }
        }

        private string GetDailyOutputDirectory()
        {
            string fullPath = Path.Combine(OutputDirectoryPath, DateTime.Now.ToString("yyyy-MM-dd"));
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
            return fullPath;
        }

        [RelayCommand]
        private async Task SelectFiles()
        {
            Reset(); // เคลียร์ค่าเก่า
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true) return;

            var files = dialog.FileNames.ToList();

            if (files.Count == 1)
            {
                // กรณีเลือกไฟล์เดียว
                _selectedFiles = files;
                InputFilesInfo = "1 file selected.";
                OutputFileName = Path.GetFileNameWithoutExtension(files[0]) + ".xlsx";
                StatusMessage = "File loaded. Ready.";
            }
            else
            {
                // กรณีเลือกหลายไฟล์ -> รวมไฟล์แบบ High Performance
                IsBusy = true;
                StatusMessage = $"Merging {files.Count} files...";
                InputFilesInfo = $"{files.Count} files selected.";

                await Task.Run(() =>
                {
                    try
                    {
                        string outputDir = GetDailyOutputDirectory();
                        string combinedFilePath = Path.Combine(outputDir, "multiple_file_output.txt");

                        // Optimization: ใช้ FileStream + Buffer ขนาดใหญ่ (1MB) เพื่อ copy ข้อมูลดิบ
                        // วิธีนี้เร็วกว่า StringBuilder หรือ StreamReader/Writer มาก
                        int bufferSize = 1024 * 1024;
                        using (var outputStream = new FileStream(combinedFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize))
                        {
                            double totalFiles = files.Count;
                            int processed = 0;

                            foreach (var file in files)
                            {
                                // SequentialScan บอก OS ว่าเราจะอ่านรวดเดียว เพื่อให้ OS ทำ Read-Ahead Caching
                                using (var inputStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan))
                                {
                                    inputStream.CopyTo(outputStream);
                                }

                                processed++;
                                // Update UI ทุกๆ 5 ไฟล์ เพื่อลด overhead ของการ switch thread
                                if (processed % 5 == 0 || processed == totalFiles)
                                {
                                    ProgressValue = (processed / totalFiles) * 100;
                                }
                            }
                        }

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            _selectedFiles = [combinedFilePath];
                            OutputFileName = "multiple_file_output.xlsx";
                            StatusMessage = "Merge Complete. Ready.";
                            MessageBoxService.Show($"Files merged into:\n{combinedFilePath}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        });
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = $"Merge Error: {ex.Message}";
                            MessageBoxService.Show($"Error merging files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }
                });
                IsBusy = false;
            }
        }

        // ---------------------------------------------------------
        // 2. Header Checking (Head & Tail Scan)
        // ---------------------------------------------------------

        [RelayCommand]
        private async Task CheckHeader()
        {
            if (_selectedFiles == null || _selectedFiles.Count == 0)
            {
                StatusMessage = "Please select files first.";
                return;
            }

            HeaderInfoText = "Analyzing File Structure (Head & Tail)...";
            IsBusy = true;

            await Task.Run(() =>
            {
                try
                {
                    var filePath = _selectedFiles.First();
                    var fileInfo = new FileInfo(filePath);

                    // 1. อ่านส่วนหัว (Head) - เพื่อดู Packet แรก
                    string firstHeaderHex = ReadFileBlock(filePath, isTail: false);
                    byte[]? firstHeaderBytes = ParseHexToBytesSafe(firstHeaderHex);

                    // 2. อ่านส่วนท้าย (Tail) - เพื่อดู Packet สุดท้าย
                    // นี่คือจุดสำคัญ: เราไม่อ่านทั้งไฟล์ แต่ Seek ไปท้ายไฟล์แล้วอ่านย้อนกลับ
                    string lastHeaderHex = ReadFileBlock(filePath, isTail: true);
                    byte[]? lastHeaderBytes = FindLastPacketInHex(lastHeaderHex);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StringBuilder sb = new();
                        sb.AppendLine($"File Size: {fileInfo.Length / (1024.0 * 1024.0):F2} MB");
                        sb.AppendLine("--------------------------------------------------");

                        // แสดงข้อมูล Header แรก
                        sb.AppendLine("[START OF FILE]");
                        if (firstHeaderBytes != null)
                            sb.AppendLine(ParseHeaderSummary(firstHeaderBytes));
                        else
                            sb.AppendLine("Error: Could not read start of file.");

                        sb.AppendLine("--------------------------------------------------");

                        // แสดงข้อมูล Header สุดท้าย
                        sb.AppendLine("[END OF FILE]");
                        if (lastHeaderBytes != null)
                        {
                            sb.AppendLine(ParseHeaderSummary(lastHeaderBytes));
                        }
                        else
                        {
                            sb.AppendLine("Warning: Could not identify a valid packet at the end of file.");
                            sb.AppendLine("(File might be truncated or format is inconsistent)");
                        }

                        // Parameters
                        sb.AppendLine("--------------------------------------------------");
                        sb.AppendLine($"Delay Time: {DelayTimeMs}");
                        sb.AppendLine($"Threshold: {KFactor}");

                        HeaderInfoText = sb.ToString();
                        StatusMessage = "Header Analysis Complete.";
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = "Error Analyzing Header";
                        HeaderInfoText = $"Error: {ex.Message}";
                    });
                }
            });

            IsBusy = false;
        }

        private static string ReadFileBlock(string path, bool isTail)
        {
            // อ่าน Text File ประมาณ 16KB (เพียงพอสำหรับ Header 2064 Bytes แบบ Text Hex)
            const int BUFFER_SIZE = 16384;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (isTail)
            {
                // ถ้าอ่านท้ายไฟล์ ให้ Seek ไปที่ตำแหน่ง (Length - BufferSize)
                long seekPos = Math.Max(0, fs.Length - BUFFER_SIZE);
                fs.Seek(seekPos, SeekOrigin.Begin);
            }

            using var sr = new StreamReader(fs);
            if (isTail) return sr.ReadToEnd(); // อ่านจนจบ

            // ถ้าอ่านหัวไฟล์ อ่านแค่ Buffer
            char[] buffer = new char[BUFFER_SIZE];
            int read = sr.Read(buffer, 0, BUFFER_SIZE);
            return new string(buffer, 0, read);
        }

        private static byte[]? FindLastPacketInHex(string hexContent)
        {
            if (string.IsNullOrEmpty(hexContent)) return null;

            // Clean Hex Content
            string clean = hexContent.Replace(" ", "").Replace("\r", "").Replace("\n", "").Trim();

            // ค้นหา Sync Code "AA55" ตัวสุดท้าย (สมมติว่า Packet เริ่มด้วย AA 55)
            // ถ้า Sync Code เปลี่ยน ให้แก้ตรงนี้
            string syncPattern = "AA55";
            int lastIndex = clean.LastIndexOf(syncPattern, StringComparison.OrdinalIgnoreCase);

            if (lastIndex != -1)
            {
                // ตรวจสอบความยาว: Packet ยาว 2064 bytes = 4128 hex chars
                int expectedHexLength = 4128;

                // ลองตัดมา parse
                if (lastIndex + expectedHexLength <= clean.Length)
                {
                    string packetHex = clean.Substring(lastIndex, expectedHexLength);
                    return HexStringToByteArray(packetHex);
                }
                else
                {
                    // กรณี Packet สุดท้ายมาไม่ครบ (File truncated)
                    // ตัดมาเท่าที่มี
                    string partialHex = clean[lastIndex..];
                    // ต้องมีความยาวเลขคู่ถึงจะแปลงเป็น byte ได้
                    if (partialHex.Length % 2 != 0) partialHex = partialHex[..^1];
                    return HexStringToByteArray(partialHex);
                }
            }
            return null;
        }

        private static byte[]? ParseHexToBytesSafe(string hexContent)
        {
            if (string.IsNullOrEmpty(hexContent)) return null;
            string clean = hexContent.Replace(" ", "").Replace("\r", "").Replace("\n", "").Trim();

            // เอาแค่ Packet แรก (4128 chars)
            if (clean.Length > 4128) clean = clean[..4128];
            if (clean.Length % 2 != 0) clean = clean[..^1];

            return HexStringToByteArray(clean);
        }

        private static byte[]? HexStringToByteArray(string hex)
        {
            try
            {
                int len = hex.Length;
                byte[] bytes = new byte[len / 2];
                // ใช้ Loop ธรรมดา เร็วกว่า LINQ มาก
                for (int i = 0; i < len; i += 2)
                {
                    // ใช้ Span เพื่อลด substring allocation (ใน .NET Core/5+)
                    // ถ้าใช้ .NET Framework เก่า ให้ใช้ substring
                    bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
                }
                return bytes;
            }
            catch { return null; }
        }

        private static string ParseHeaderSummary(byte[] data)
        {
            if (data == null || data.Length < 14) return "Invalid/Incomplete Packet Data";

            try
            {
                // Logic การถอดเวลา (อิงตาม Code เก่าของคุณ)
                // Bytes 8-11: Seconds (Reversed/Little Endian)
                uint seconds_part = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4));

                // Bytes 12-13: Milliseconds
                ushort milliseconds_part = (ushort)((data[13] << 8) | data[12]);

                DateTime dt = DateTimeOffset.FromUnixTimeSeconds(seconds_part)
                    .AddMilliseconds(milliseconds_part)
                    .UtcDateTime; // หรือ .ToLocalTime() ถ้าต้องการ

                return $"Timestamp: {dt.ToString("yyyy-MMM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} (UTC)\n" +
                       $"Sync Code: {data[0]:X2} {data[1]:X2}\n" +
                       $"Seq No:    {data[4]:X2} {data[5]:X2}";
            }
            catch (Exception ex)
            {
                return $"Error parsing timestamp: {ex.Message}";
            }
        }

        // ---------------------------------------------------------
        // 3. Save Mean (Parallel Calculation)
        // ---------------------------------------------------------

        [RelayCommand]
        private async Task SaveMean()
        {
            if (ProcessedData == null || ProcessedData.Count == 0)
            {
                StatusMessage = "No data to process.";
                return;
            }

            StatusMessage = "Calculating Means (Multithreaded)...";
            IsBusy = true;

            await Task.Run(() =>
            {
                try
                {
                    // ยิง 4 Layers พร้อมกัน
                    var t1 = Task.Run(() => CalculateMeanParallel(d => d.L1));
                    var t2 = Task.Run(() => CalculateMeanParallel(d => d.L2));
                    var t6 = Task.Run(() => CalculateMeanParallel(d => d.L6));
                    var t7 = Task.Run(() => CalculateMeanParallel(d => d.L7));

                    Task.WaitAll(t1, t2, t6, t7);

                    WriteMeansToFile(1, t1.Result);
                    WriteMeansToFile(2, t2.Result);
                    WriteMeansToFile(6, t6.Result);
                    WriteMeansToFile(7, t7.Result);

                    Application.Current.Dispatcher.Invoke(() => StatusMessage = "Mean Values Saved Successfully.");
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = $"Error saving means: {ex.Message}";
                        MessageBoxService.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            });
            IsBusy = false;
        }

        // ฟังก์ชันคำนวณแบบ Parallel (รองรับข้อมูลหลักล้าน)
        private double[] CalculateMeanParallel(Func<BaselineData, float[]> selector)
        {
            int dataCount = ProcessedData.Count;
            if (dataCount == 0) return new double[16];

            object lockObj = new();
            double[] finalSums = new double[16];

            // แบ่งงานเป็น Chunks ให้ทุก Core ช่วยกันทำ
            Parallel.ForEach(
                Partitioner.Create(0, dataCount),
                () => new double[16], // Local Storage per thread
                (range, state, localSums) =>
                {
                    // Loop ภายใน Chunk
                    for (int i = range.Item1; i < range.Item2; i++)
                    {
                        float[] values = selector(ProcessedData[i]);
                        // บวกค่า 16 Channel (Loop Unrolling จะทำให้ไวกว่านี้แต่นี่ก็ไวมากแล้ว)
                        for (int ch = 0; ch < 16; ch++)
                        {
                            localSums[ch] += values[ch];
                        }
                    }
                    return localSums;
                },
                (localSums) =>
                {
                    // รวมผลลัพธ์จากแต่ละ Thread (Lock แค่ตอนจบงานย่อย)
                    lock (lockObj)
                    {
                        for (int ch = 0; ch < 16; ch++)
                        {
                            finalSums[ch] += localSums[ch];
                        }
                    }
                }
            );

            // หารจำนวนเพื่อหาค่าเฉลี่ย
            for (int i = 0; i < 16; i++)
            {
                finalSums[i] /= dataCount;
            }

            return finalSums;
        }

        private void WriteMeansToFile(int layerId, double[] means)
        {
            var lines = new List<string>(16);
            for (int i = 0; i < 16; i++)
            {
                lines.Add(means[i].ToString("F2"));
            }
            string path = Path.Combine(GetDailyOutputDirectory(), $"MeanValues{layerId}.txt");
            File.WriteAllLines(path, lines);
        }

        // Helper Load Mean เดิม (ถ้ามีใช้)
        private double LoadMeanFromFile(int channelIndex)
        {
            string fileName = SelectedLayerIndex switch
            {
                1 => "MeanValues2.txt",
                2 => "MeanValues6.txt",
                3 => "MeanValues7.txt",
                _ => "MeanValues1.txt"
            };
            string fullPath = Path.Combine(GetDailyOutputDirectory(), fileName);
            try
            {
                string outputDir = GetDailyOutputDirectory();
                if (File.Exists(fullPath))
                {
                    var lines = File.ReadAllLines(fullPath);
                    if (channelIndex < lines.Length && double.TryParse(lines[channelIndex], out double mean))
                        return mean;
                }
            }
            catch { }
            return 0;
        }
    }
}