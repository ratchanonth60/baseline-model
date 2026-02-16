using System.Collections.Generic;
using System.Threading.Tasks;

namespace BaselineMode.WPF.Core.Interfaces.Observation
{
    public interface IObservationExcelHelper
    {
        Task SaveToExcelAsync(List<string> data, string filePath);
        Task<string?> SaveAllResultsToExcelAsync(string folderName, List<Dictionary<string, object>> allResults);
    }
}
