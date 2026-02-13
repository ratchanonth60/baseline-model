namespace BaselineMode.WPF.Core.Models.Baseline
{
    public class BaselineData
    {
        public int SamplingPacketNo { get; set; }
        public int SamplingNo { get; set; }
        public float[] L1 { get; set; } = new float[16];
        public float[] L2 { get; set; } = new float[16];
        public float[] L6 { get; set; } = new float[16];
        public float[] L7 { get; set; } = new float[16];

        // Voltage
        public float[] L1_Voltage { get; set; } = new float[16];
        public float[] L2_Voltage { get; set; } = new float[16];
        public float[] L6_Voltage { get; set; } = new float[16];
        public float[] L7_Voltage { get; set; } = new float[16];
    }
}
