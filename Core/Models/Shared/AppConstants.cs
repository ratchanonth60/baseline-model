namespace BaselineMode.WPF.Core.Models.Shared
{
    public static class AppConstants
    {
        // File & Folder Names
        public const string SourceFolderName = "Source";
        public const string DefaultOutputFileName = "Output";

        // Protocol Definitions
        public const string HeaderStart = "E225";
        public const int PacketLength = 256;      // Total hex bytes per line (Header+Data+Checksum)
        public const int PacketHexLength = PacketLength * 2; // Total hex characters
        public const int HeaderOffset = 16;       // bytes to skip before particle data
        public const int ParticlesPerLine = 5;    // number of particles per data packet

        public const int ParticleDataLength = 34; // length of one particle's data in bytes
        public const int ChannelsPerLayer = 16;   // Number of channels per detector layer

        // Chart Settings
        public const int ChartXMin = 0;
        public const int ChartXMaxDSSD = 16384;
        public const int ChartXMaxBGO = 4095;
        public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
    }
}
