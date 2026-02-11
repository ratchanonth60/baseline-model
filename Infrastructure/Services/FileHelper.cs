using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using BaselineMode.WPF.Core.Interfaces;
using BaselineMode.WPF.Core.Models;
using BaselineMode.WPF.Core.Models.Observation; // For ObservationConstants if needed
using OfficeOpenXml;
using Microsoft.Win32; // For SaveFileDialog

namespace BaselineMode.WPF.Infrastructure.Services
{
    public class FileHelper : IFileHelper
    {
        private const string AppFolderName = "DSSD_Analysis";
        private const string SourceFolderName = "Source"; // For raw excel files

        public string GetDocumentsFolder()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        public string GetOutputFolder(string subFolder = "")
        {
            string documentsPath = GetDocumentsFolder();
            string appFolderPath = Path.Combine(documentsPath, AppFolderName);

            if (!string.IsNullOrEmpty(subFolder))
            {
                appFolderPath = Path.Combine(appFolderPath, subFolder);
            }

            if (!Directory.Exists(appFolderPath))
            {
                Directory.CreateDirectory(appFolderPath);
            }

            return appFolderPath;
        }

        public string CombineFiles(string[] filePaths, string outputFileName)
        {
            if (filePaths == null || filePaths.Length == 0)
                throw new ArgumentException("No files selected for combination.");

            // Create output directory if it doesn't exist
            string outputFolder = GetOutputFolder("CombinedData");
            string outputPath = Path.Combine(outputFolder, outputFileName);

            // Ensure unique filename
            // outputPath = GetUniqueFilePath(outputPath);

            using var outputStream = File.Create(outputPath);
            foreach (var filePath in filePaths)
            {
                using var inputStream = File.OpenRead(filePath);
                inputStream.CopyTo(outputStream);
                // Optional: Add a newline between files if needed
                // outputStream.WriteByte((byte)'\n'); 
            }

            return outputPath;
        }

        public void SaveToExcel(List<string> data, string fileName, string subFolder = "")
        {
            string fullPath;

            // Check if fileName is already a full path
            if (Path.IsPathRooted(fileName))
            {
                fullPath = fileName;
                // Ensure directory exists
                string? directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            else
            {
                // Legacy behavior: relative path logic
                // Ensure the file name is valid
                fileName = Path.GetFileName(fileName);
                if (string.IsNullOrWhiteSpace(fileName) || Path.GetInvalidFileNameChars().Any(fileName.Contains))
                {
                    throw new ArgumentException("Invalid file name. Please use a valid name.");
                }

                // Ensure .xlsx extension
                if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                {
                    fileName += ".xlsx";
                }

                string folderName = string.IsNullOrWhiteSpace(subFolder) ? SourceFolderName : subFolder;
                string saveDirectory = GetOutputFolder(folderName);
                fullPath = Path.Combine(saveDirectory, fileName);
            }

            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Processed Data");

            for (int i = 0; i < data.Count; i++)
            {
                worksheet.Cells[i + 1, 1].Value = data[i];
            }

            package.SaveAs(new FileInfo(fullPath));
        }

        public string SaveResultsToExcel(string folderName, List<Dictionary<string, object>> results)
        {
            if (results == null || results.Count == 0)
                return string.Empty;

            string saveDirectory;
            if (Path.IsPathRooted(folderName))
            {
                // If folderName is a full path, use it directly
                saveDirectory = folderName;
            }
            else
            {
                // Legacy: use default output folder structure
                saveDirectory = GetOutputFolder(folderName);
            }

            if (!Directory.Exists(saveDirectory))
            {
                Directory.CreateDirectory(saveDirectory);
            }

            string fileName = $"AnalysisResults_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            string filePath = Path.Combine(saveDirectory, fileName);

            using var package = new ExcelPackage(new FileInfo(filePath));
            var worksheet = package.Workbook.Worksheets.Add("ParticleData");

            // Write headers
            int col = 1;
            foreach (var key in results[0].Keys)
            {
                worksheet.Cells[1, col++].Value = key;
            }

            // Write data rows
            int row = 2;
            foreach (var result in results)
            {
                int column = 1;
                foreach (var value in result.Values)
                {
                    worksheet.Cells[row, column++].Value = value;
                }
                row++;
            }

            package.Save();
            return filePath;
        }

        public string? SaveToExcelWithDialog(List<string> data, string defaultFileName)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                FileName = defaultFileName
            };

            if (dialog.ShowDialog() == true)
            {
                string fullPath = dialog.FileName;
                using var package = new ExcelPackage();
                var worksheet = package.Workbook.Worksheets.Add("Processed Data");

                for (int i = 0; i < data.Count; i++)
                {
                    worksheet.Cells[i + 1, 1].Value = data[i];
                }

                package.SaveAs(new FileInfo(fullPath));
                return fullPath;
            }

            return null;
        }

        public string? SaveResultsToExcelWithDialog(string defaultFileName, List<Dictionary<string, object>> results)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                FileName = defaultFileName
            };

            if (dialog.ShowDialog() == true)
            {
                string fullPath = dialog.FileName;
                using var package = new ExcelPackage();
                var worksheet = package.Workbook.Worksheets.Add("ParticleData");

                // Write headers
                if (results.Count > 0)
                {
                    int col = 1;
                    foreach (var key in results[0].Keys)
                    {
                        worksheet.Cells[1, col++].Value = key;
                    }

                    // Write data rows
                    int row = 2;
                    foreach (var result in results)
                    {
                        int column = 1;
                        foreach (var value in result.Values)
                        {
                            worksheet.Cells[row, column++].Value = value;
                        }
                        row++;
                    }
                }

                package.SaveAs(new FileInfo(fullPath));
                return fullPath;
            }

            return null;
        }

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public string[] GetPossibleFilePaths(string outputName)
        {
            string documentsBase = Path.Combine(GetDocumentsFolder(), AppFolderName);
            string debugBase = AppDomain.CurrentDomain.BaseDirectory;

            return
            [
                // Documents folder paths (primary)
                Path.Combine(documentsBase, outputName, $"{outputName}_ParticleData.xlsx"),
                Path.Combine(documentsBase, SourceFolderName, $"{outputName}.xlsx"),
                Path.Combine(documentsBase, $"{outputName}.xlsx"),
                // Legacy Documents folder paths
                Path.Combine(documentsBase, outputName, $"{outputName}_ParticleData.xlsx"),
                Path.Combine(documentsBase, AppConstants.SourceFolderName, $"{outputName}.xlsx"), // Added missing path
                // Legacy debug folder paths (for backwards compatibility)
                Path.Combine(debugBase, outputName, $"{outputName}_ParticleData.xlsx"),
                Path.Combine(debugBase, AppConstants.SourceFolderName, $"{outputName}.xlsx"),
                Path.Combine(debugBase, $"{outputName}.xlsx")
            ];
        }

        public string? FindExcelFile(string outputName)
        {
            return GetPossibleFilePaths(outputName).FirstOrDefault(File.Exists);
        }
    }
}
