using System;
using System.Collections.Generic;
using BaselineMode.WPF.Core.Models;

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
        List<BaselineData> ProcessFileStream(string filePath, IProgress<double>? progress = null);

        /// <summary>
        /// Save data list to Excel file
        /// </summary>
        void SaveToExcel(List<BaselineData> dataList, string filePath, IProgress<double>? progress = null);

        /// <summary>
        /// Read data from Excel file
        /// </summary>
        List<BaselineData> ReadExcelFile(string filePath, IProgress<double>? progress = null);

        /// <summary>
        /// Open file dialog to select files
        /// </summary>
        string[]? OpenFileDialog(string filter, bool multiselect);
    }
}
