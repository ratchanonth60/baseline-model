using System.Collections.Generic;
using System.Threading.Tasks;
using BaselineMode.WPF.Core.Models.Shared;

namespace BaselineMode.WPF.Core.Interfaces.Observation
{
    public interface IObservationExcelHelper
    {
        Task<Result> SaveToExcelAsync(List<string> data, string filePath);
        Task<Result<string>> SaveAllResultsToExcelAsync(string folderName, List<Dictionary<string, object>> allResults);
    }
}
