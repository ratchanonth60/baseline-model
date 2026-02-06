using System.Collections.Generic;

namespace BaselineMode.WPF.Core.Models.Observation
{
    public class BGOData
    {
        public List<double> HighGain { get; set; } = new List<double>();
        public List<double> LowGain { get; set; } = new List<double>();
    }
}
