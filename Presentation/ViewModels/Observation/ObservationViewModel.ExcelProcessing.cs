using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaselineMode.WPF.Core.Models.Observation;
using BaselineMode.WPF.Core.Models.Shared;
using ExcelDataReader;

namespace BaselineMode.WPF.Presentation.ViewModels.Observation
{
    /// <summary>
    /// Excel data processing and header validation for Observation mode.
    /// </summary>
    public partial class ObservationViewModel
    {
        public async Task ProcessExcelDataAsync(string fileName, IProgress<ObservationProcessReport> progress, CancellationToken token)
        {
            if (!File.Exists(fileName))
                throw new FileNotFoundException("The specified file does not exist.", fileName);

            await Task.Run(() =>
            {
                using var stream = File.Open(fileName, FileMode.Open, FileAccess.Read);
                using var reader = ExcelReaderFactory.CreateReader(stream);

                var result = reader.AsDataSet();
                var rawData = result.Tables[0];
                int totalSteps = rawData.Rows.Count;
                int dataIndex = 1;
                bool isFirstData = true;

                while (dataIndex <= totalSteps && !token.IsCancellationRequested)
                {
                    string? hexString = rawData.Rows[dataIndex - 1][0]?.ToString();
                    if (hexString == null)
                    {
                        dataIndex++;
                        continue;
                    }

                    var hexData = _dataProcessor.SplitHexData(hexString);
                    _dataProcessor.ProcessParticles(hexData);

                    if (progress != null)
                    {
                        var report = new ObservationProcessReport
                        {
                            CurrentStep = dataIndex,
                            TotalSteps = totalSteps,
                            Message = $"Processing... {Math.Round((double)dataIndex / totalSteps * 100)}%",
                            IsComplete = false,
                            LastHexData = hexData
                        };
                        if (isFirstData)
                        {
                            report.CurrentTime = _dataProcessor.GetDateTimeFromHexData(hexData);
                            isFirstData = false;
                        }
                        progress.Report(report);
                    }

                    dataIndex++;
                }

                if (progress != null && totalSteps > 0)
                {
                    string? lastHex = rawData.Rows[totalSteps - 1][0]?.ToString();
                    string[]? lastHexData = null;
                    DateTime? lastTime = null;
                    if (lastHex != null)
                    {
                        lastHexData = _dataProcessor.SplitHexData(lastHex);
                        lastTime = _dataProcessor.GetDateTimeFromHexData(lastHexData);
                    }
                    progress.Report(new ObservationProcessReport
                    {
                        CurrentStep = totalSteps,
                        TotalSteps = totalSteps,
                        Message = "Process Complete",
                        IsComplete = true,
                        CurrentTime = lastTime,
                        LastHexData = lastHexData
                    });
                }
            }, token);
        }

        public static async Task<(bool IsValid, string Message, int ErrorRow)> CheckHeaderAsync(string fileName)
        {
            if (!File.Exists(fileName))
                return (false, "File not found.", 0);

            return await Task.Run(() =>
            {
                try
                {
                    using var stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var reader = ExcelReaderFactory.CreateReader(stream);

                    var result = reader.AsDataSet();
                    var rawData = result.Tables[0];
                    int totalSteps = rawData.Rows.Count;

                    for (int i = 1; i <= totalSteps; i++)
                    {
                        string? hexString = rawData.Rows[i - 1][0]?.ToString();
                        if (hexString == null || !hexString.StartsWith(AppConstants.HeaderStart, StringComparison.OrdinalIgnoreCase))
                            return (false, $"Header INCORRECT at row {i}", i);
                    }

                    return (true, "Header is correct!", 0);
                }
                catch (Exception ex)
                {
                    return (false, $"Error: {ex.Message}", 0);
                }
            });
        }
    }
}
