using System.Collections.Generic;

namespace BaselineMode.WPF.Models
{
    public class BaselineData
    {
        public int SamplingPacketNo { get; set; }
        public int SamplingNo { get; set; }
        public double[] L1 { get; set; } = new double[16];
        public double[] L2 { get; set; } = new double[16];
        public double[] L6 { get; set; } = new double[16];
        public double[] L7 { get; set; } = new double[16];

        // Voltage
        public double[] L1_Voltage { get; set; } = new double[16];
        public double[] L2_Voltage { get; set; } = new double[16];
        public double[] L6_Voltage { get; set; } = new double[16];
        public double[] L7_Voltage { get; set; } = new double[16];
    }
}
