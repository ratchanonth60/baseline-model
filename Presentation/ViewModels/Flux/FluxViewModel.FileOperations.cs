using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BaselineMode.WPF.Core.Models.Shared;
using BaselineMode.WPF.Presentation.ViewModels.Flux;

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

            var files = await _dialogService.OpenFilesAsync("Select Flux Files", true, "Text Files (*.txt)|*.txt|All Files (*.*)|*.*");
            if (files == null || files.Length == 0) return;

            _selectedFiles = files.ToList();
            InputFileList = [.. _selectedFiles];

            if (files.Length == 1)
            {
                InputFilesInfo = "1 file selected.";
                OutputFileName = Path.GetFileNameWithoutExtension(files[0]) + ".xlsx";
                StatusMessage = "Files loaded. Ready to process.";
                return;
            }

            await CombineFilesAsync(_selectedFiles);
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
                    await _dialogService.ShowMessageAsync($"Error combining files: {result.Error}", "Error",
                        MsgBoxButton.OK, MsgBoxImage.Error);
                    return;
                }

                string combinedFilePath = result.Value;
                _selectedFiles = [combinedFilePath];
                InputFileList = [.. _selectedFiles];
                InputFilesInfo = $"{files.Count} files combined.";
                OutputFileName = "multiple_file_output.xlsx";
                StatusMessage = "Files combined. Ready to process.";
                _logger.LogInfo($"Files combined for flux: {combinedFilePath}");

                await _dialogService.ShowMessageAsync($"Files combined into:\n{combinedFilePath}", "Success",
                    MsgBoxButton.OK, MsgBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                _logger.LogException(ex, "CombineFilesAsync (Flux)");
                await _dialogService.ShowMessageAsync($"Error combining files: {ex.Message}", "Error",
                    MsgBoxButton.OK, MsgBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                ProgressValue = 0;
            }
        }
    }
}
