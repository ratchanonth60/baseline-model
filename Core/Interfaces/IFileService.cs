using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BaselineMode.WPF.Core.Models;
using BaselineMode.WPF.Core.Models.Baseline;

namespace BaselineMode.WPF.Core.Interfaces
{
    /// <summary>
    /// Interface for file processing services with memory-safe operations
    /// </summary>
    public interface IFileService : IDisposable
    {
        /// <summary>
        /// Process binary file stream and convert to BaselineData
        /// </summary>
        Task<List<BaselineData>> ProcessFileStreamAsync(string filePath, IProgress<double>? progress = null);

        /// <summary>
        /// Save data list to Excel file
        /// </summary>
        Task SaveToExcelAsync(List<BaselineData> dataList, string filePath, IProgress<double>? progress = null);

        /// <summary>
        /// Read data from Excel file
        /// </summary>
        Task<List<BaselineData>> ReadExcelFileAsync(string filePath, IProgress<double>? progress = null);

        /// <summary>
        /// Open file dialog to select files
        /// </summary>
        string[]? OpenFileDialog(string filter, bool multiselect);
    }
}
