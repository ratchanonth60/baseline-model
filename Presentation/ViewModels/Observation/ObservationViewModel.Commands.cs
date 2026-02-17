using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace BaselineMode.WPF.Presentation.ViewModels.Observation
{
    /// <summary>
    /// Analysis commands for Observation mode.
    /// </summary>
    public partial class ObservationViewModel
    {
        [RelayCommand]
        private async Task AnalyzeFiles()
        {
            if (InputFileList == null || InputFileList.Length == 0)
            {
                StatusMessage = "Please select files first.";
                return;
            }

            try
            {
                StatusMessage = "Processing...";
                ProgressValue = 0;
                IsBusy = true;

                var result = await _dataProcessor.ProcessFilesAsync(InputFileList);
                HistogramData = result;

                StatusMessage = "Processing complete!";
                IsBusy = false;
                ProgressValue = 100;
                RequestPlotUpdate?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                StatusMessage = "Error!";
                IsBusy = false;
                _logger.LogException(ex, "AnalyzeFiles (Observation)");
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }
    }
}
