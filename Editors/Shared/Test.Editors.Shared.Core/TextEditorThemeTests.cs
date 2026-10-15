using ICSharpCode.AvalonEdit.Highlighting;
using Shared.Core.Settings;
using Shared.Ui.Editors.TextEditor;
using Xna = Microsoft.Xna.Framework;

namespace Test.Editors.Shared.Core
{
    // Skeletons and the XML/HTML editor were drawn black on whatever background was chosen, so on a dark one they disappeared.
    [Apartment(ApartmentState.STA)]
    public class TextEditorThemeTests
    {
        [TestCase(BackgroundColour.Black, false)]
        [TestCase(BackgroundColour.DarkGrey, false)]
        [TestCase(BackgroundColour.Charcoal, false)]
        [TestCase(BackgroundColour.MidnightBlue, false)]
        [TestCase(BackgroundColour.ChromaBlue, false)]
        [TestCase(BackgroundColour.MidGrey, true)]
        [TestCase(BackgroundColour.LightGrey, true)]
        [TestCase(BackgroundColour.White, true)]
        [TestCase(BackgroundColour.Green, true)]
        [TestCase(BackgroundColour.Magenta, true)]
        public void GuidesAreBlack_OnlyWhereBlackReads(BackgroundColour background, bool black)
        {
            Assert.That(ApplicationSettingsHelper.GuideColour(background) == Xna.Color.Black, Is.EqualTo(black));
        }

        static IEnumerable<(string Definition, string Name, float Contrast)> SyntaxColours(BackgroundColour background)
        {
            var back = ApplicationSettingsHelper.GetEnumAsColour(background);
            foreach (var definition in new[] { "XML", "HTML" })
            {
                foreach (var colour in HighlightingManager.Instance.GetDefinition(definition).NamedHighlightingColors)
                {
                    if (colour.Foreground?.GetColor(null) is { } c)
                        yield return (definition, colour.Name, ApplicationSettingsHelper.Contrast(new Xna.Color(c.R, c.G, c.B), back));
                }
            }
        }

        [Test]
        public void OnEveryBackground_EveryXmlAndHtmlColourReads()
        {
            TextEditorTheme.Apply(BackgroundColour.White);
            var unreadableOnBlack = SyntaxColours(BackgroundColour.Black).Count(x => x.Contrast < TextEditorTheme.MinContrast);
            Assert.That(unreadableOnBlack, Is.GreaterThan(0), "the colours for white include some unreadable on black, or this test proves nothing");

            foreach (var background in Enum.GetValues<BackgroundColour>())
            {
                TextEditorTheme.Apply(background);
                var unreadable = SyntaxColours(background).Where(x => x.Contrast < TextEditorTheme.MinContrast).ToList();
                Assert.That(unreadable, Is.Empty, background.ToString());
            }
        }

        [Test]
        public void ApplyStartsFromTheShippedColours_NotTheLastApply()
        {
            TextEditorTheme.Apply(BackgroundColour.White);
            var onWhite = SyntaxColours(BackgroundColour.White).ToList();

            TextEditorTheme.Apply(BackgroundColour.Black);
            TextEditorTheme.Apply(BackgroundColour.White);

            Assert.That(SyntaxColours(BackgroundColour.White).ToList(), Is.EqualTo(onWhite));
        }

        [Test]
        public void ReadableKeepsAColourThatAlreadyReads()
        {
            var blue = new Xna.Color(0, 0, 255);
            Assert.That(TextEditorTheme.Readable(blue, Xna.Color.White, Xna.Color.Black), Is.EqualTo(blue));
            Assert.That(TextEditorTheme.Readable(blue, Xna.Color.Black, new Xna.Color(220, 220, 220)), Is.Not.EqualTo(blue));
        }
    }
}
