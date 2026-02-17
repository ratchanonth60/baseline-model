using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using BaselineMode.WPF.Infrastructure.Services;

namespace BaselineMode.WPF.Presentation.ViewModels.Flux
{
    /// <summary>
    /// File selection and combining for Flux mode.
    /// </summary>
    public partial class FluxViewModel
    {
        private async Task SelectFiles()
        {
            Reset();

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true) return;

            var files = dialog.FileNames.ToList();
            _selectedFiles = files;
            InputFileList = [.. _selectedFiles];

            if (files.Count == 1)
            {
                InputFilesInfo = "1 file selected.";
                OutputFileName = Path.GetFileNameWithoutExtension(files[0]) + ".xlsx";
                StatusMessage = "Files loaded. Ready to process.";
                return;
            }

            await CombineFilesAsync(files);
        }

        private async Task CombineFilesAsync(List<string> files)
        {
            IsBusy = true;
            StatusMessage = $"Combining {files.Count} files...";

            try
            {
                var result = await _fileHelper.CombineFilesAsync([.. files], "multiple_file_output.txt");
                if (result.IsFailure)
                {
                    StatusMessage = result.Error;
                    _logger.LogError($"Combine files failed: {result.Error}");
                    MessageBoxService.Show($"Error combining files: {result.Error}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string combinedFilePath = result.Value;
                _selectedFiles = [combinedFilePath];
                InputFileList = [.. _selectedFiles];
                InputFilesInfo = $"{files.Count} files combined.";
                OutputFileName = "multiple_file_output.xlsx";
                StatusMessage = "Files combined. Ready to process.";
                _logger.LogInfo($"Files combined for flux: {combinedFilePath}");

                MessageBoxService.Show($"Files combined into:\n{combinedFilePath}", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                _logger.LogException(ex, "CombineFilesAsync (Flux)");
                MessageBoxService.Show($"Error combining files: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                ProgressValue = 0;
            }
        }
    }
}
