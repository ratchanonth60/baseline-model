using System.Collections.Generic;
using BaselineMode.WPF.Models.Observation;

namespace BaselineMode.WPF.Interfaces.Observation
{
    public interface IObservationDataProcessor
    {
        void ClearData();
        void ProcessParticles(string[] hexData);
        string[] SplitHexData(string hexString);
        System.DateTime GetDateTimeFromHexData(string[] hexData);

        // Generic accessors
        Dictionary<DetectorLayer, LayerData> DSSDData { get; }
        Dictionary<BGOLayer, BGOData> BGOData { get; }
        List<Dictionary<string, object>> AllResults { get; }
    }

    public class LayerData
    {
        public List<double> PulseHeightX { get; set; } = new List<double>();
        public List<double> PulseHeightY { get; set; } = new List<double>();

        // Strip data: Key is strip index (0-8)
        public Dictionary<int, List<int>> StripX { get; set; } = new Dictionary<int, List<int>>();
        public Dictionary<int, List<int>> StripY { get; set; } = new Dictionary<int, List<int>>();

        public LayerData()
        {
            for (int i = 0; i <= 8; i++) // 0-8 strips
            {
                StripX[i] = new List<int>();
                StripY[i] = new List<int>();
            }
        }
    }

    public class BGOData
    {
        public List<double> HighGain { get; set; } = new List<double>();
        public List<double> LowGain { get; set; } = new List<double>();
    }
}
