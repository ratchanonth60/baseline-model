using System.Collections.Generic;

namespace BaselineMode.WPF.Core.Models.Observation
{
    public class LayerData
    {
        public List<double> PulseHeightX { get; set; } = [];
        public List<double> PulseHeightY { get; set; } = [];

        // Strip data: Key is strip index (0-8)
        public Dictionary<int, List<int>> StripX { get; set; } = [];
        public Dictionary<int, List<int>> StripY { get; set; } = [];

        public LayerData()
        {
            for (int i = 0; i <= 8; i++) // 0-8 strips
            {
                StripX[i] = [];
                StripY[i] = [];
            }
        }
    }
}
