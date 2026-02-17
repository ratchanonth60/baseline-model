using System.Windows.Media;
using DrawingColor = System.Drawing.Color;

namespace BaselineMode.WPF.Core.Helpers
{
    public static class ColorHelper
    {
        public static DrawingColor ToDrawingColor(Color mediaColor)
        {
            return DrawingColor.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B);
        }

        public static DrawingColor ToDrawingColor(Color mediaColor, DrawingColor fallback)
        {
            if (mediaColor.A == 0) return fallback;
            return DrawingColor.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B);
        }

        public static DrawingColor ToDrawingColor(Color mediaColor, DrawingColor? fallback)
        {
            if (mediaColor.A == 0) return fallback ?? DrawingColor.Black;
            return DrawingColor.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B);
        }
    }
}
