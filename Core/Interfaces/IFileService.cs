using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BaselineMode.WPF.Core.Models;
using BaselineMode.WPF.Core.Models.Baseline;
using BaselineMode.WPF.Core.Models.Shared;

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
        Task<Result<List<BaselineData>>> ProcessFileStreamAsync(string filePath, IProgress<double>? progress = null);

        /// <summary>
        /// Save data list to Excel file
        /// </summary>
        Task<Result> SaveToExcelAsync(List<BaselineData> dataList, string filePath, IProgress<double>? progress = null);

        /// <summary>
        /// Read data from Excel file
        /// </summary>
        Task<Result<List<BaselineData>>> ReadExcelFileAsync(string filePath, IProgress<double>? progress = null);


    }
}
