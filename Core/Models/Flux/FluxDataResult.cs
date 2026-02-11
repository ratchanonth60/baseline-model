namespace BaselineMode.WPF.Core.Models.Flux
{
    /// <summary>
    /// Holds processed flux observation data for a single packet.
    /// </summary>
    public class FluxDataResult
    {
        /// <summary>Time offset in seconds from the timecode field.</summary>
        public double TimeSeconds { get; set; }

        /// <summary>Particle counting for 7 layers (L1–L7).</summary>
        public double[] ParticleCounting { get; set; } = new double[7];

        /// <summary>Layer index for each of the 1000 particle slots.</summary>
        public double[] ParticleLayer { get; set; } = new double[1000];

        /// <summary>Offset time detected for each of the 1000 particle slots.</summary>
        public double[] ParticleOffsetTime { get; set; } = new double[1000];
    }
}
