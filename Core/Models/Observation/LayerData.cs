using System.Collections.Generic;

namespace BaselineMode.WPF.Core.Models.Observation
{
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
}
