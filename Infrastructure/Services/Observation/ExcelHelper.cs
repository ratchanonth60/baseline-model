using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OfficeOpenXml;
using BaselineMode.WPF.Core.Models;
using BaselineMode.WPF.Core.Models.Observation;
using BaselineMode.WPF.Core.Interfaces;
using BaselineMode.WPF.Core.Interfaces.Observation;
using BaselineMode.WPF.Core.Models.Shared;

namespace BaselineMode.WPF.Infrastructure.Services.Observation
{
    public class ObservationExcelHelper : IObservationExcelHelper
    {
        private readonly ILoggerService _logger;

        public ObservationExcelHelper(ILoggerService logger)
        {
            _logger = logger;
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        public async Task<Result> SaveToExcelAsync(List<string> data, string filePath)
        {
            try
            {
                // Ensure the file name is valid
                string fileName = Path.GetFileName(filePath);
                if (string.IsNullOrWhiteSpace(fileName) || Path.GetInvalidFileNameChars().Any(fileName.Contains))
                {
                    return Result.Failure("Invalid file name. Please use a valid name.");
                }

                // Ensure the directory exists
                string? saveDirectory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(saveDirectory) && !Directory.Exists(saveDirectory))
                {
                    Directory.CreateDirectory(saveDirectory);
                }

                string fullPath = filePath;
                using var package = new ExcelPackage();
                var worksheet = package.Workbook.Worksheets.Add("Processed Data");

                for (int i = 0; i < data.Count; i++)
                {
                    worksheet.Cells[i + 1, 1].Value = data[i];
                }

                await package.SaveAsAsync(new FileInfo(fullPath));
                _logger.LogInfo($"Observation data saved to Excel: {fullPath}");
                return Result.Success();
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, $"Error saving observation data to Excel: {filePath}");
                return Result.Failure($"Failed to save Excel file: {ex.Message}");
            }
        }

        public async Task<Result<string>> SaveAllResultsToExcelAsync(string folderName, List<Dictionary<string, object>> allResults)
        {
            if (allResults == null || allResults.Count == 0)
            {
                return Result.Failure<string>("No results to save.");
            }

            try
            {
                // Create the full output path
                string outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folderName);

                // Ensure the output directory exists
                Directory.CreateDirectory(outputPath);

                // Create the full file name with path
                string fileName = $"{folderName}_ParticleData.xlsx";
                string filePath = Path.Combine(outputPath, fileName);

                // Delete the file if it already exists to replace it with new data
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    // Create a new worksheet
                    var worksheet = package.Workbook.Worksheets.Add("ParticleData");

                    // Write headers
                    int col = 1;
                    foreach (var key in allResults[0].Keys)
                    {
                        worksheet.Cells[1, col++].Value = key;
                    }

                    // Write data rows
                    int row = 2;
                    foreach (var result in allResults)
                    {
                        int column = 1;
                        foreach (var value in result.Values)
                        {
                            worksheet.Cells[row, column++].Value = value;
                        }
                        row++;
                    }

                    // Save the file
                    await package.SaveAsync();
                }

                _logger.LogInfo($"All observation results saved to: {filePath}");
                return Result.Success(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogException(ex, $"Error saving all results to Excel: {folderName}");
                return Result.Failure<string>($"Failed to save results: {ex.Message}");
            }
        }
    }
}
