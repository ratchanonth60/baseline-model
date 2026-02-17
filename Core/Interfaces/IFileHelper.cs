using System.Collections.Generic;
using System.Threading.Tasks;
using BaselineMode.WPF.Core.Models.Shared;

namespace BaselineMode.WPF.Core.Interfaces
{
    /// <summary>
    /// Shared file helper interface for both Baseline and Observation modes
    /// </summary>
    public interface IFileHelper
    {
        /// <summary>
        /// Get the user's Documents folder path
        /// </summary>
        string GetDocumentsFolder();

        /// <summary>
        /// Get output folder path (in Documents folder)
        /// </summary>
        string GetOutputFolder(string subFolder = "");

        /// <summary>
        /// Combine multiple files into one file
        /// </summary>
        Task<Result<string>> CombineFilesAsync(string[] filePaths, string outputFileName);

        /// <summary>
        /// Save hex data segments to Excel file
        /// </summary>
        Task<Result> SaveToExcelAsync(List<string> data, string fileName, string subFolder = "");

        /// <summary>
        /// Save processed results to Excel file
        /// </summary>
        Task<Result<string>> SaveResultsToExcelAsync(string folderName, List<Dictionary<string, object>> results);

        /// <summary>
        /// Save hex data segments to Excel file with SaveFileDialog
        /// </summary>
        Task<Result<string>> SaveToExcelWithDialogAsync(List<string> data, string defaultFileName);

        /// <summary>
        /// Save processed results to Excel file with SaveFileDialog
        /// </summary>
        Task<Result<string>> SaveResultsToExcelWithDialogAsync(string defaultFileName, List<Dictionary<string, object>> results);

        /// <summary>
        /// Check if file exists
        /// </summary>
        bool FileExists(string path);

        /// <summary>
        /// Search for Excel file in multiple possible locations
        /// </summary>
        string[] GetPossibleFilePaths(string outputName);

        /// <summary>
        /// Find existing Excel file from possible paths
        /// </summary>
        string? FindExcelFile(string outputName);
    }
}
