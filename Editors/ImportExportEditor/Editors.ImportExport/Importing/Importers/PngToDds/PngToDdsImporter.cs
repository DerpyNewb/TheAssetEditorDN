using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DirectXTexNet;
using Editors.ImportExport.Importing.Importers.PngToDds.Helpers;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.FileSources;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel.Types;

namespace Editors.ImportExport.Importing.Importers.PngToDds
{
    /// <summary>
    /// An image file (PNG, JPEG, BMP, TIFF) as a game texture. Any pixel layout is read: 24-bit, greyscale, palette and 16-bit
    /// files all come out as 8-bit BGRA. The values are written as they are, with no gamma change on the way, which is what the
    /// game expects whether the target format is sRGB or not.
    /// </summary>
    public static class PngToDdsImporter
    {
        public static PackFile Import(string inputPath, TextureType textureType, GameTypeEnum gameType, string outFileName, bool convert = true) =>
            Import(File.ReadAllBytes(inputPath), textureType, gameType, outFileName, convert);

        /// <param name="convert">Normal maps from blue (most 3D software) to CA's orange layout, material maps from Blender's
        /// (metal in blue) to CA's (metal in red). Nothing for the other types.</param>
        public static PackFile Import(byte[] image, TextureType textureType, GameTypeEnum gameType, string outFileName, bool convert = true)
        {
            var (pixels, width, height) = Decode(image);
            if (convert)
                Convert(pixels, textureType);

            var format = DDSFormatHelper.GetDDSFormat(gameType, textureType);
            // CA's base colours with cut-outs are BC3: BC1 keeps one bit of alpha.
            if (format == DXGI_FORMAT.BC1_UNORM_SRGB && textureType is TextureType.BaseColour or TextureType.Diffuse && HasAlpha(pixels))
                format = DXGI_FORMAT.BC3_UNORM_SRGB;

            // A block-compressed texture's top level is a whole number of 4x4 blocks.
            using var source = ToScratchImage(pixels, width, height);
            using var sized = width % 4 == 0 && height % 4 == 0 ? null : source.Resize((width + 3) / 4 * 4, (height + 3) / 4 * 4, TEX_FILTER_FLAGS.DEFAULT);

            // The pixels are already in the target's encoding, so the SRGB flags only tell DirectXTex not to convert them.
            var srgb = TexHelper.Instance.IsSRGB(format);
            using var mips = (sized ?? source).GenerateMipMaps(srgb ? TEX_FILTER_FLAGS.SRGB : TEX_FILTER_FLAGS.DEFAULT, 0);
            using var dds = mips.Compress(format, (srgb ? TEX_COMPRESS_FLAGS.SRGB : TEX_COMPRESS_FLAGS.DEFAULT) | TEX_COMPRESS_FLAGS.PARALLEL, 0.5f);
            using var stream = dds.SaveToDDSMemory(DDS_FLAGS.NONE);

            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return new PackFile(outFileName, new MemorySource(bytes));
        }

        static (byte[] Pixels, int Width, int Height) Decode(byte[] image)
        {
            var frame = BitmapDecoder.Create(new MemoryStream(image), BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
            bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
            return (pixels, bgra.PixelWidth, bgra.PixelHeight);
        }

        static void Convert(byte[] bgra, TextureType textureType)
        {
            for (var i = 0; i < bgra.Length; i += 4)
            {
                if (textureType == TextureType.Normal)
                {
                    // Blue (R=x, G=y, B=z) to orange (R=255, G=y, B=0, A=x); the game rebuilds z. Linear, as CA's are.
                    var x = bgra[i + 2];
                    bgra[i + 0] = 0;
                    bgra[i + 2] = 255;
                    bgra[i + 3] = x;
                }
                else if (textureType == TextureType.MaterialMap)
                {
                    // Blender's metal is in blue, CA's in red; roughness is green in both.
                    (bgra[i + 0], bgra[i + 2]) = (bgra[i + 2], bgra[i + 0]);
                    bgra[i + 3] = 255;
                }
            }
        }

        static bool HasAlpha(byte[] bgra)
        {
            for (var i = 3; i < bgra.Length; i += 4)
            {
                if (bgra[i] != 255)
                    return true;
            }
            return false;
        }

        static ScratchImage ToScratchImage(byte[] bgra, int width, int height)
        {
            var image = TexHelper.Instance.Initialize2D(DXGI_FORMAT.B8G8R8A8_UNORM, width, height, 1, 1, CP_FLAGS.NONE);
            Marshal.Copy(bgra, 0, image.GetImage(0, 0, 0).Pixels, bgra.Length);
            return image;
        }
    }
}
