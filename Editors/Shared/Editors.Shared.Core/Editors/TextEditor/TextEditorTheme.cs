using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;
using Shared.Core.Settings;
using Xna = Microsoft.Xna.Framework;

namespace Shared.Ui.Editors.TextEditor
{
    /// <summary>
    /// The XML/HTML text editor's colours, set apart from the app theme: a background from the 3D preview's colour list, text in
    /// the guide colour for it, and every syntax colour moved toward the text colour until it reads on that background.
    /// AvalonEdit's highlighting definitions are shared, so this recolours every open editor at once.
    /// </summary>
    public static class TextEditorTheme
    {
        public const string BackgroundKey = "TextEditor.Background";
        public const string ForegroundKey = "TextEditor.Foreground";
        public const float MinContrast = 4.5f;

        // Each syntax colour as AvalonEdit shipped it, so a second Apply starts from the original and not from the last one.
        // By reference: HighlightingColor's hash follows its Foreground, which this changes.
        static readonly Dictionary<HighlightingColor, HighlightingBrush?> s_original = new(ReferenceEqualityComparer.Instance);

        /// <summary>Raised after Apply, for open editors to redraw the lines they have already coloured.</summary>
        public static event Action? Changed;

        public static void Apply(BackgroundColour background)
        {
            var back = ApplicationSettingsHelper.GetEnumAsColour(background);
            var text = ApplicationSettingsHelper.GuideColour(back);

            var resources = Application.Current?.Resources;
            if (resources != null)
            {
                resources[BackgroundKey] = Brush(back);
                resources[ForegroundKey] = Brush(text);
            }

            foreach (var definition in HighlightingManager.Instance.HighlightingDefinitions)
            {
                foreach (var colour in definition.NamedHighlightingColors)
                    Recolour(colour, back, text);
            }
            Changed?.Invoke();
        }

        static void Recolour(HighlightingColor colour, Xna.Color background, Xna.Color text)
        {
            if (!s_original.TryGetValue(colour, out var original))
                s_original[colour] = original = colour.Foreground;
            if (original?.GetColor(null) is not { } shipped)
                return;
            var readable = Readable(new Xna.Color(shipped.R, shipped.G, shipped.B), background, text);
            colour.Foreground = new SimpleHighlightingBrush(Color.FromRgb(readable.R, readable.G, readable.B));
        }

        /// <summary>The colour, moved toward the text colour a tenth at a time until it reads on the background.</summary>
        public static Xna.Color Readable(Xna.Color colour, Xna.Color background, Xna.Color text)
        {
            for (var step = 0; step <= 10; step++)
            {
                var candidate = Xna.Color.Lerp(colour, text, step / 10f);
                if (ApplicationSettingsHelper.Contrast(candidate, background) >= MinContrast)
                    return candidate;
            }
            return text;
        }

        static SolidColorBrush Brush(Xna.Color c)
        {
            var brush = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
            brush.Freeze();
            return brush;
        }
    }
}
