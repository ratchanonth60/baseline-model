using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using BaselineMode.WPF.Models;
using BaselineMode.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BaselineMode.WPF.ViewModels
{
    public partial class MainViewModel
    {
        [RelayCommand]
        private void BrowseOutputDirectory()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            dialog.Title = "Select Output Root Folder";
            if (dialog.ShowDialog() == true)
            {
                OutputDirectoryPath = dialog.FolderName;
            }
        }

        private string GetDailyOutputDirectory()
        {
            string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
            string fullPath = Path.Combine(OutputDirectoryPath, dateStr);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
            return fullPath;
        }

        [RelayCommand]
        private async Task SelectFiles()
        {
            // Reset before selecting new files (like Form1.cs)
            Reset();

            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Multiselect = true;
            dialog.Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";

            if (dialog.ShowDialog() == true)
            {
                var files = dialog.FileNames.ToList();
                _selectedFiles = files; // Temporary set, might change if combined

                if (files.Count == 1)
                {
                    // Single file - use filename as output
                    InputFilesInfo = "1 file selected.";
                    OutputFileName = Path.GetFileNameWithoutExtension(files.First()) + ".xlsx";
                    StatusMessage = "Files loaded. Ready to process.";
                }
                else if (files.Count > 1)
                {
                    // Multiple files - combine them
                    IsBusy = true;
                    StatusMessage = $"Combining {files.Count} files...";

                    await Task.Run(() =>
                    {
                        try
                        {
                            string outputDir = GetDailyOutputDirectory();
                            string combinedFilePath = Path.Combine(outputDir, "multiple_file_output.txt");

                            var progress = new Progress<double>(percent =>
                            {
                                ProgressValue = percent;
                            });

                            // Combine files exactly like the original Form1.cs method
                            var allContents = new List<string>();
                            int totalFiles = files.Count;
                            int processed = 0;

                            foreach (var file in files)
                            {
                                try
                                {
                                    allContents.Add(File.ReadAllText(file));
                                }
                                catch { /* Skip unreadable files */ }

                                processed++;
                                ((IProgress<double>)progress).Report((double)processed / totalFiles * 100);
                            }

                            // Join with single \n like original code
                            File.WriteAllText(combinedFilePath, string.Concat(allContents));

                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                _selectedFiles = new List<string> { combinedFilePath };
                                InputFilesInfo = $"{files.Count} files combined.";
                                OutputFileName = "multiple_file_output.xlsx";
                                StatusMessage = "Files combined. Ready to process.";
                                MessageBoxService.Show(
                                    $"Files combined into:\n{combinedFilePath}",
                                    "Success",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Information);
                            });
                        }
                        catch (Exception ex)
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                StatusMessage = $"Error: {ex.Message}";
                                MessageBoxService.Show(
                                    $"Error combining files: {ex.Message}",
                                    "Error",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Error);
                            });
                        }
                    });

                    IsBusy = false;
                }
            }
        }

        [RelayCommand]
        private async Task CheckHeader()
        {
            if (!_selectedFiles.Any())
            {
                StatusMessage = "Please select files check.";
                return;
            }

            HeaderInfoText = "Checking...";
            IsBusy = true;

            await Task.Run(() =>
            {
                try
                {
                    // Reverting to Legacy Flow: Check the combined file (or single selected file)
                    // This treats the entire selection as one "block" of data.
                    var fileToCheck = _selectedFiles.First();

                    // 1. Data Integrity Check (Shared Logic via HeaderValidator)
                    var result = HeaderValidator.ValidateFile(fileToCheck);

                    if (!result.IsValid)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = result.ErrorMessage;
                            MessageBoxService.Show(
                                $"{result.ErrorMessage}\nContent: '{result.ErrorContent}' \nFile: {result.FilteredFilePath}",
                                "Check Error",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Error);

                            // Safe null check for substring
                            string errContent = result.ErrorContent ?? "null";
                            HeaderInfoText = $"{result.ErrorMessage}\nFound: {errContent.Substring(0, Math.Min(20, errContent.Length))}...";
                        });
                    }
                    else
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = "Header is correct!";
                            MessageBoxService.Show("Header is correct!", "Check", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                        });

                        // 2. Parse Header Info
                        // Ensure clean content for splitting
                        if (!string.IsNullOrEmpty(result.FirstHeaderContent))
                        {
                            var cleanHex = result.FirstHeaderContent.Replace(" ", "").Trim();
                            var hexData = SplitHexData(cleanHex);

                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                ParseHeaderInfo(hexData);
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = $"Error: {ex.Message}";
                        HeaderInfoText = $"Error: {ex.Message}";
                    });
                }
            });

            IsBusy = false;
        }

        private void ParseHeaderInfo(string[] hexData)
        {
            try
            {
                StringBuilder sb = new StringBuilder();

                // 1. Packet Synchronization Code
                sb.AppendLine($"Packet Synchronization Code: {hexData[0]} {hexData[1]}");

                // 2. Package Identification
                sb.AppendLine($"Package Identification: {hexData[2]} {hexData[3]}");

                // 3. Packet Sequence
                sb.AppendLine($"Packet Sequence: {hexData[4]} {hexData[5]}");

                // 4. Packet Data Length
                sb.AppendLine($"Packet data length: {hexData[6]} {hexData[7]}");

                // 5. Timestamp
                if (hexData.Length >= 14)
                {
                    // Logic from Form1: hexData.Skip(8).Take(6)
                    var timecodeHex = hexData.Skip(8).Take(6).ToArray();
                    var timecodeDec = timecodeHex.Select(h => Convert.ToByte(h, 16)).ToArray();

                    uint seconds_part = BitConverter.ToUInt32(timecodeDec.Take(4).Reverse().ToArray(), 0);
                    ushort milliseconds_part = BitConverter.ToUInt16(timecodeDec.Skip(4).Reverse().ToArray(), 0);

                    // Form1 calculates double total_seconds but creates DateTime from parts
                    DateTime datetime_value = DateTimeOffset.FromUnixTimeSeconds((long)seconds_part)
                        .AddMilliseconds(milliseconds_part)
                        .UtcDateTime;

                    sb.AppendLine($"Timestamp: {datetime_value.ToString("yyyy-MMM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture)}");
                }

                // 6. Data Type
                if (hexData.Length >= 16)
                {
                    sb.AppendLine($"Data Type: {hexData[14]} {hexData[15]}");
                }

                // 7. Checksum
                if (hexData.Length >= 2064)
                {
                    // Logic from Form1: sum 2054 bytes starting at index 8
                    int total_sum = hexData.Skip(8).Take(2054).Select(h => Convert.ToInt32(h, 16)).Sum();
                    int last_two_bytes = total_sum % 65536;
                    string checksum_hex = last_two_bytes.ToString("X4"); // e.g. "0A1B"

                    string CheckSum_fromData = hexData[2062] + hexData[2063];

                    sb.AppendLine($"Check Sum: {hexData[2062]} {hexData[2063]}");

                    if (checksum_hex.Equals(CheckSum_fromData, StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine("Checksum matches!");
                    }
                    else
                    {
                        sb.AppendLine($"Checksum does not match. (Calc: {checksum_hex})");
                    }
                }

                // 8. Test Conditions (UI Variables)
                sb.AppendLine("Test condition:");
                sb.AppendLine($"Delay Time: {DelayTimeMs}");
                sb.AppendLine($"Threshold: {KFactor}"); // Assuming Threshold in Form1 corresponds to k-factor or threshold value

                HeaderInfoText = sb.ToString();
            }
            catch (Exception ex)
            {
                HeaderInfoText = $"Error parsing header: {ex.Message}";
            }
        }

        private string[] SplitHexData(string hexString)
        {
            // Logic from Form1.SplitHexData
            int n = 2;
            if (hexString.Length % n != 0) return new string[0]; // Safety check

            var list = new string[hexString.Length / n];
            for (int i = 0; i < list.Length; i++)
            {
                list[i] = hexString.Substring(i * n, n);
            }
            return list;
        }

        [RelayCommand]
        private async Task SaveMean()
        {
            if (!ProcessedData.Any()) return;

            StatusMessage = "Saving Mean Values...";
            await Task.Run(() =>
            {
                try
                {
                    SaveLayerMeans(1, d => d.L1);
                    SaveLayerMeans(2, d => d.L2);
                    SaveLayerMeans(6, d => d.L6);
                    SaveLayerMeans(7, d => d.L7);
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = $"Error saving means: {ex.Message}";
                        MessageBoxService.Show($"Error saving means: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    });
                }
            });
            StatusMessage = "Mean Values Saved.";
        }

        private void SaveLayerMeans(int layerId, Func<BaselineData, double[]> selector)
        {
            var lines = new List<string>();
            for (int i = 0; i < 16; i++)
            {
                int ch = i;
                var data = ProcessedData.Select(d => selector(d)[ch]).ToArray();
                if (data.Any())
                {
                    double simpleMean = data.Average();
                    lines.Add($"{simpleMean:F2}");
                }
                else lines.Add("0.00");
            }

            string outputDir = GetDailyOutputDirectory();
            string path = Path.Combine(outputDir, $"MeanValues{layerId}.txt");
            System.IO.File.WriteAllText(path, string.Join(Environment.NewLine, lines));
        }

        private double LoadMeanFromFile(int channelIndex)
        {
            string layerFile = SelectedLayerIndex switch
            {
                1 => "MeanValues2.txt",
                2 => "MeanValues6.txt",
                3 => "MeanValues7.txt",
                _ => "MeanValues1.txt"
            };

            try
            {
                if (File.Exists(layerFile))
                {
                    var lines = File.ReadAllLines(layerFile);
                    if (channelIndex < lines.Length && double.TryParse(lines[channelIndex], out double mean))
                        return mean;
                }
            }
            catch { }
            return 0;
        }

    }
}
