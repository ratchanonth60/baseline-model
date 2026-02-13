using System.Collections.Generic;
using BaselineMode.WPF.Core.Models.Observation;

namespace BaselineMode.WPF.Core.Interfaces.Observation
{
    public interface IObservationDataProcessor
    {
        void ClearData();
        void ProcessParticles(string[] hexData);
        string[] SplitHexData(string hexString);
        System.DateTime GetDateTimeFromHexData(string[] hexData);

        // File processing methods
        System.Threading.Tasks.Task<Dictionary<string, int[]>> ProcessFilesAsync(string[] filePaths);
        string ReadHeader(string filePath);

        // Generic accessors
        Dictionary<DetectorLayer, LayerData> DSSDData { get; }
        Dictionary<BGOLayer, BGOData> BGOData { get; }
        List<Dictionary<string, object>> AllResults { get; }
        /// <summary>
        /// Validates the checksum of a hex data packet.
        /// </summary>
        bool ValidateHeader(string[] hexData);
    }
}
