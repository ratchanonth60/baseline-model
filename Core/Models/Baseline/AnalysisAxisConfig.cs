namespace BaselineMode.WPF.Core.Models.Baseline
{
    public class AnalysisAxisConfig
    {
        public double XMin { get; set; }
        public double XMax { get; set; }
        public int BinCount { get; set; } = 4096;
        public int AxisIndex { get; set; } = 0; // 0=ADC, 1=Voltage, 2=Energy
        public double Slope { get; set; } = 0.000427;
        public double Offset { get; set; } = 0.0;
        public double VoltageMax { get; set; } = 5000.0;
        public double AdcResolution { get; set; } = 16383.0;
        public double BarWidthMultiplier { get; set; } = 1.0;
    }
}
