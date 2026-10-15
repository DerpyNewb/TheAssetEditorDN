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
    }
}
