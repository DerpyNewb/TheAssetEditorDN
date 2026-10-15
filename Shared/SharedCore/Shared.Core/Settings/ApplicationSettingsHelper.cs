using Microsoft.Xna.Framework;

namespace Shared.Core.Settings
{
    // Saved in the settings file by number: a new colour goes on the end, or everyone's saved choice shifts to its neighbour.
    public enum BackgroundColour
    {
        DarkGrey,
        LegacyBlue,
        Green,
        Black,
        Charcoal,
        MidGrey,
        LightGrey,
        White,
        MidnightBlue,
        ChromaBlue,
        Magenta,
    }
    public class ApplicationSettingsHelper
    {
        public static string GetEnumAsString(BackgroundColour colour)
        {
            return colour switch
            {
                BackgroundColour.DarkGrey => "Dark Grey",
                BackgroundColour.LegacyBlue => "Legacy Blue",
                BackgroundColour.Green => "Green",
                BackgroundColour.Black => "Black",
                BackgroundColour.Charcoal => "Charcoal",
                BackgroundColour.MidGrey => "Mid Grey",
                BackgroundColour.LightGrey => "Light Grey",
                BackgroundColour.White => "White",
                BackgroundColour.MidnightBlue => "Midnight Blue",
                BackgroundColour.ChromaBlue => "Chroma Blue",
                BackgroundColour.Magenta => "Magenta",
                _ => throw new NotImplementedException(),
            };
        }
        public static Color GetEnumAsColour(BackgroundColour colour)
        {
            return colour switch
            {
                BackgroundColour.DarkGrey => new Color(50, 50, 50, 1),
                BackgroundColour.LegacyBlue => new Color(94, 150, 239, 1),
                BackgroundColour.Green => new Color(0, 177, 64, 1),
                BackgroundColour.Black => new Color(0, 0, 0, 1),
                BackgroundColour.Charcoal => new Color(28, 28, 30, 1),
                BackgroundColour.MidGrey => new Color(128, 128, 128, 1),
                BackgroundColour.LightGrey => new Color(200, 200, 200, 1),
                BackgroundColour.White => new Color(255, 255, 255, 1),
                BackgroundColour.MidnightBlue => new Color(18, 24, 44, 1),
                BackgroundColour.ChromaBlue => new Color(0, 71, 187, 1),
                BackgroundColour.Magenta => new Color(255, 0, 255, 1),
                _ => throw new NotImplementedException(),
            };
        }

        static readonly Color LightGuide = new(220, 220, 220);

        /// <summary>
        /// Skeletons, bounding boxes, the grid and overlay text are drawn black. On a dark background black is invisible, so they
        /// are drawn in this: black or light grey, whichever stands out more against the background.
        /// </summary>
        public static Color GuideColour(BackgroundColour background) => GuideColour(GetEnumAsColour(background));

        public static Color GuideColour(Color background) =>
            Contrast(Color.Black, background) >= Contrast(LightGuide, background) ? Color.Black : LightGuide;

        /// <summary>WCAG contrast ratio, 1 to 21.</summary>
        public static float Contrast(Color a, Color b)
        {
            var (la, lb) = (Luminance(a), Luminance(b));
            return (MathF.Max(la, lb) + 0.05f) / (MathF.Min(la, lb) + 0.05f);
        }

        static float Luminance(Color c)
        {
            static float Linear(byte channel)
            {
                var v = channel / 255f;
                return v <= 0.03928f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
            }
            return 0.2126f * Linear(c.R) + 0.7152f * Linear(c.G) + 0.0722f * Linear(c.B);
        }
    }
}
