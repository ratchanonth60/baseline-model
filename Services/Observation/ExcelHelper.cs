using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using BaselineMode.WPF.Models.Observation;

namespace BaselineMode.WPF.Services.Observation
{
    public class ObservationExcelHelper
    {
        public ObservationExcelHelper()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        public void SaveToExcel(List<string> data, string filePath)
        {
            // Ensure the file name is valid
            filePath = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(filePath) || Path.GetInvalidFileNameChars().Any(filePath.Contains))
            {
                throw new ArgumentException("Invalid file name. Please use a valid name.");
            }

            // Get the Debug/Source folder dynamically
            string projectDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string saveDirectory = Path.Combine(projectDirectory, ObservationConstants.SourceFolderName);

            // Ensure the directory exists
            if (!Directory.Exists(saveDirectory))
            {
                Directory.CreateDirectory(saveDirectory);
            }

            string fullPath = Path.Combine(saveDirectory, filePath);
            using (var package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("Processed Data");

                for (int i = 0; i < data.Count; i++)
                {
                    worksheet.Cells[i + 1, 1].Value = data[i];
                }

                package.SaveAs(new FileInfo(fullPath));
            }
        }

        public string SaveAllResultsToExcel(string folderName, List<Dictionary<string, object>> allResults)
        {
            if (allResults == null || allResults.Count == 0)
            {
                return null;
            }

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
                package.Save();
            }

            return filePath;
        }
    }
}
