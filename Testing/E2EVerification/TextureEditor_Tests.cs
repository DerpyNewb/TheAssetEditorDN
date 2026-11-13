using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using DirectXTexNet;
using Editors.TextureEditor.ViewModels;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;
using Shared.Core.Commands;

namespace Test.E2EVerification
{
    internal class TextureEditor_Tests
    {
        static readonly Regex BindingPath = new(@"\{Binding\s+(?:Path=)?([A-Za-z_][\w.\[\]]*)");

        // A uniform texture with a different value in each channel, so each channel preview has one right answer.
        internal const byte R = 200, G = 100, B = 40, A = 230;

        internal static byte[] Dds(DXGI_FORMAT format, bool legacyHeader, byte r = R, byte g = G, byte b = B, byte a = A, int size = 8)
        {
            using var image = TexHelper.Instance.Initialize2D(DXGI_FORMAT.B8G8R8A8_UNORM, size, size, 1, 1, CP_FLAGS.NONE);
            var pixels = Enumerable.Range(0, size * size).SelectMany(_ => new[] { b, g, r, a }).ToArray();
            Marshal.Copy(pixels, 0, image.GetImage(0, 0, 0).Pixels, pixels.Length);

            // The SRGB flags keep the bytes as they are: without them DirectXTex converts the colours into an sRGB format.
            var srgb = TexHelper.Instance.IsSRGB(format);
            using var encoded = format == DXGI_FORMAT.B8G8R8A8_UNORM ? null
                : TexHelper.Instance.IsCompressed(format) ? image.Compress(format, srgb ? TEX_COMPRESS_FLAGS.SRGB : TEX_COMPRESS_FLAGS.DEFAULT, 0.5f)
                : image.Convert(format, srgb ? TEX_FILTER_FLAGS.SRGB : TEX_FILTER_FLAGS.DEFAULT, 0.5f);
            using var stream = (encoded ?? image).SaveToDDSMemory(legacyHeader ? DDS_FLAGS.FORCE_DX9_LEGACY : DDS_FLAGS.NONE);
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }

        internal static (AssetEditorTestRunner Runner, PackFile File) PackWith(string name, byte[] bytes)
        {
            var runner = new AssetEditorTestRunner();
            var pack = runner.CreateEmptyPackFile("derpy_texture_test", true);
            runner.PackFileService.AddFilesToPack(pack, [new NewPackFileEntry(@"variantmeshes\derpy", PackFile.CreateFromBytes(name, bytes))]);
            return (runner, runner.PackFileService.FindFile(@"variantmeshes\derpy\" + name)!);
        }

        static Color Pixel(ImageSource image)
        {
            var bitmap = new FormatConvertedBitmap((BitmapSource)image, PixelFormats.Bgra32, null, 0);
            var pixel = new byte[4];
            bitmap.CopyPixels(new System.Windows.Int32Rect(2, 2, 1, 1), pixel, 4, 0);
            return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
        }

        // The A radio button was bound to FormatACheckbox on the editor, where R, G and B bind ViewModel.Format*Checkbox. WPF drops a
        // binding to a missing property without a word, so A never switched the big image to the alpha channel.
        [Test]
        public void EveryBindingInThePreview_NamesAPropertyTheEditorHas()
        {
            var view = XDocument.Load(Path.Combine(SettingsView_Tests.Root(), "Editors", "TextureEditor", "Views", "TexturePreviewView.xaml"));
            var paths = view.Descendants().SelectMany(e => e.Attributes()).Select(a => BindingPath.Match(a.Value))
                .Where(m => m.Success).Select(m => m.Groups[1].Value).Distinct().ToList();

            var missing = paths.Where(p => Resolve(typeof(TextureEditorViewModel), p) == false).ToList();

            Assert.That(paths, Has.Member("ViewModel.FormatRCheckbox"), "the reader found no channel bindings to check");
            Assert.That(missing, Is.Empty);
        }

        static bool Resolve(Type type, string path)
        {
            foreach (var part in path.Split('.'))
            {
                var name = part.Split('[')[0];
                var property = type.GetProperty(name);
                if (property == null)
                    return false;
                type = part.Contains('[') ? property.PropertyType.GetElementType() ?? typeof(object) : property.PropertyType;
            }
            return true;
        }

        // Each channel radio button shows the image beside it, and the G and B previews were each other's: G drew the blue channel.
        // The alpha preview drew 1 - alpha, so an opaque texture showed black.
        [Test]
        public void ChannelPreviews_ShowTheirOwnChannel_AndAlphaIsWhiteWhereOpaque()
        {
            var (runner, file) = PackWith("derpy_test_mask.dds", Dds(DXGI_FORMAT.B8G8R8A8_UNORM, legacyHeader: true));
            var editor = runner.CommandFactory.Create<OpenEditorCommand>().Execute<TextureEditorViewModel>(file, EditorEnums.Texture_Editor);
            var previews = editor.ViewModel.PreviewImage;

            Assert.That(Pixel(previews[1]), Is.EqualTo(Color.FromRgb(R, 0, 0)), "R");
            Assert.That(Pixel(previews[2]), Is.EqualTo(Color.FromRgb(0, G, 0)), "G");
            Assert.That(Pixel(previews[3]), Is.EqualTo(Color.FromRgb(0, 0, B)), "B");
            Assert.That(Pixel(previews[4]), Is.EqualTo(Color.FromRgb(A, A, A)), "A");

            editor.ViewModel.FormatACheckbox = true;
            Assert.That(editor.ViewModel.ActiveImage.Value, Is.SameAs(previews[4]));
        }

        // .jpg was not registered, only .jpeg. And every image that is not a .dds came back with no image information, which the
        // preview read at once, so a .png or .jpeg failed to open too.
        [TestCase("derpy_test.png")]
        [TestCase("derpy_test.jpg")]
        [TestCase("derpy_test.jpeg")]
        public void AnImageFile_OpensInTheTextureEditor(string name)
        {
            var (runner, file) = PackWith(name, Encode(name));
            var editor = runner.CommandFactory.Create<OpenEditorCommand>().Execute<TextureEditorViewModel>(file);

            Assert.That(editor.ViewModel.Width.Value, Is.EqualTo(6));
            Assert.That(editor.ViewModel.Height.Value, Is.EqualTo(5));
            var red = Pixel(editor.ViewModel.PreviewImage[1]);
            Assert.That(red.R, Is.InRange(R - 8, R + 8), "the red channel preview");
        }

        // The game reads a base colour with no sRGB flag as linear and draws it lighter. Nothing in the viewer showed it: it draws the
        // bytes as they are, and so the file looked right right up to the game.
        [TestCase("derpy_test_base_colour.dds", DXGI_FORMAT.BC3_UNORM, true, "legacy")]
        [TestCase("derpy_test_base_colour.dds", DXGI_FORMAT.BC1_UNORM, false, "BC1_UNORM")]
        [TestCase("derpy_test_base_colour.dds", DXGI_FORMAT.BC3_UNORM_SRGB, false, null)]
        [TestCase("derpy_test_mask.dds", DXGI_FORMAT.BC3_UNORM, true, null)]
        public void ABaseColourWithNoSrgbFlag_IsWarnedAbout(string name, DXGI_FORMAT format, bool legacyHeader, string? named)
        {
            var (runner, file) = PackWith(name, Dds(format, legacyHeader));
            var editor = runner.CommandFactory.Create<OpenEditorCommand>().Execute<TextureEditorViewModel>(file, EditorEnums.Texture_Editor);

            var warning = editor.ViewModel.ColourSpaceWarning.Value;
            if (named == null)
                Assert.That(warning, Is.Null);
            else
                Assert.That(warning, Does.Contain(named).And.Contain("BC3_UNORM_SRGB"));
        }

        internal static byte[] Encode(string name, int width = 6, int height = 5)
        {
            var pixels = Enumerable.Range(0, width * height).SelectMany(_ => new[] { B, G, R, (byte)255 }).ToArray();
            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            BitmapEncoder encoder = Path.GetExtension(name) is ".png" ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 100 };
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
    }
}
