using System.Collections.Generic;

namespace BaselineMode.WPF.Core.Interfaces.Observation
{
    public interface IObservationExcelHelper
    {
        void SaveToExcel(List<string> data, string filePath);
        string? SaveAllResultsToExcel(string folderName, List<Dictionary<string, object>> allResults);
    }
}
