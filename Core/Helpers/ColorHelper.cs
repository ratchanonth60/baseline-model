using Avalonia.Media;
using ScottPlot;

namespace BaselineMode.WPF.Core.Helpers
{
    public static class ColorHelper
    {
        /// <summary>
        /// Converts an Avalonia Media Color to a ScottPlot Color.
        /// </summary>
        public static ScottPlot.Color ToScottPlotColor(Avalonia.Media.Color mediaColor)
        {
            return new ScottPlot.Color(mediaColor.R, mediaColor.G, mediaColor.B, mediaColor.A);
        }

        /// <summary>
        /// Converts an Avalonia Media Color to a ScottPlot Color with a fallback.
        /// </summary>
        public static ScottPlot.Color ToScottPlotColor(Avalonia.Media.Color mediaColor, ScottPlot.Color fallback)
        {
            if (mediaColor.A == 0) return fallback;
            return new ScottPlot.Color(mediaColor.R, mediaColor.G, mediaColor.B, mediaColor.A);
        }

        /// <summary>
        /// Converts an Avalonia Media Color to a ScottPlot Color with a nullable fallback.
        /// </summary>
        public static ScottPlot.Color ToScottPlotColor(Avalonia.Media.Color mediaColor, ScottPlot.Color? fallback)
        {
            if (mediaColor.A == 0) return fallback ?? ScottPlot.Colors.Black;
            return new ScottPlot.Color(mediaColor.R, mediaColor.G, mediaColor.B, mediaColor.A);
        }

        /// <summary>
        /// Converts a System.Drawing.Color to a ScottPlot Color.
        /// </summary>
        public static ScottPlot.Color ToScottPlotColor(System.Drawing.Color drawingColor)
        {
            return new ScottPlot.Color(drawingColor.R, drawingColor.G, drawingColor.B, drawingColor.A);
        }


        // Legacy compatibility methods using System.Drawing.Color
        public static System.Drawing.Color ToDrawingColor(Avalonia.Media.Color mediaColor)
        {
            return System.Drawing.Color.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B);
        }

        public static System.Drawing.Color ToDrawingColor(Avalonia.Media.Color mediaColor, System.Drawing.Color fallback)
        {
            if (mediaColor.A == 0) return fallback;
            return System.Drawing.Color.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B);
        }

        public static System.Drawing.Color ToDrawingColor(Avalonia.Media.Color mediaColor, System.Drawing.Color? fallback)
        {
            if (mediaColor.A == 0) return fallback ?? System.Drawing.Color.Black;
            return System.Drawing.Color.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B);
        }
    }
}
