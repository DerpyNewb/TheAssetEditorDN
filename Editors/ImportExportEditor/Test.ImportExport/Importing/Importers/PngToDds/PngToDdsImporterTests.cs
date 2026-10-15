using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DirectXTexNet;
using Editors.ImportExport.Importing.Importers.GltfToRmv;
using Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;
using Editors.ImportExport.Importing.Importers.PngToDds;
using Editors.ImportExport.Importing.Presentation.PngToDds;
using Editors.ImportExport.Misc;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.FileSources;
using Shared.Core.PackFiles.Models.Containers;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel.Types;
using Shared.TestUtility;
using SharpGLTF.Schema2;
using Test.TestingUtility.TestUtility;
using TextureType = Shared.GameFormats.RigidModel.Types.TextureType;

namespace Test.ImportExport.Importing.Importers.PngToDds
{
    // Users reported PNG import barely worked: anything but a 32-bit PNG threw, a .glb's textures (no file path) threw, and a
    // texture whose sides were not a multiple of 4 was not a valid block-compressed texture. Each case here failed before.
    class PngToDdsImporterTests
    {
        const GameTypeEnum Wh3 = GameTypeEnum.Warhammer3;

        [Test]
        public void A24BitPng_IsReadAndKeepsItsColour()
        {
            var png = Png(PixelFormats.Rgb24, 8, 8, [200, 100, 50]);

            var dds = Read(PngToDdsImporter.Import(png, TextureType.BaseColour, Wh3, "x.dds"));

            Assert.That(dds.Format, Is.EqualTo(DXGI_FORMAT.BC1_UNORM_SRGB));
            // No gamma change: sRGB in, sRGB out. A linear-to-sRGB step would make 200 into about 228.
            AssertPixel(dds, 200, 100, 50, 255);
        }

        [Test]
        public void AGreyscalePng_FillsAllThreeChannels()
        {
            var dds = Read(PngToDdsImporter.Import(Png(PixelFormats.Gray8, 8, 8, [90]), TextureType.Mask, Wh3, "x.dds"));

            Assert.That(dds.Format, Is.EqualTo(DXGI_FORMAT.BC3_UNORM));
            AssertPixel(dds, 90, 90, 90, 255);
        }

        [Test]
        public void A16BitPng_IsReadAt8Bits()
        {
            var dds = Read(PngToDdsImporter.Import(Png(PixelFormats.Rgba64, 8, 8, Rgba64(40000, 20000, 60000, 65535)), TextureType.Mask, Wh3, "x.dds"));

            AssertPixel(dds, 40000 / 257, 20000 / 257, 60000 / 257, 255);
        }

        [Test]
        public void SidesThatAreNotAMultipleOf4_AreRoundedUpWithMips()
        {
            var dds = Read(PngToDdsImporter.Import(Png(PixelFormats.Rgb24, 30, 18, [10, 20, 30]), TextureType.BaseColour, Wh3, "x.dds"));

            Assert.That((dds.Width, dds.Height), Is.EqualTo((32, 20)));
            Assert.That(dds.MipLevels, Is.EqualTo(6));
        }

        [Test]
        public void ABaseColourWithAlpha_IsBc3SoTheAlphaSurvives()
        {
            var dds = Read(PngToDdsImporter.Import(Png(PixelFormats.Bgra32, 8, 8, [50, 100, 200, 128]), TextureType.BaseColour, Wh3, "x.dds"));

            Assert.That(dds.Format, Is.EqualTo(DXGI_FORMAT.BC3_UNORM_SRGB));
            AssertPixel(dds, 200, 100, 50, 128);
        }

        // CA's normals, measured on Karl: R=255, G=y, B=0, A=x, linear. The old importer also put green through a 1/2.2 gamma.
        [Test]
        public void ABlueNormal_BecomesCaOrangeLayoutWithNoGamma()
        {
            var png = Png(PixelFormats.Rgb24, 8, 8, [200, 60, 255]);

            Assert.That(Read(PngToDdsImporter.Import(png, TextureType.Normal, Wh3, "x.dds")).Format, Is.EqualTo(DXGI_FORMAT.BC3_UNORM));
            AssertPixel(Read(PngToDdsImporter.Import(png, TextureType.Normal, Wh3, "x.dds")), 255, 60, 0, 200);
            AssertPixel(Read(PngToDdsImporter.Import(png, TextureType.Normal, Wh3, "x.dds", convert: false)), 200, 60, 255, 255);
        }

        [Test]
        public void ABlenderMaterialMap_MovesMetalFromBlueToRed()
        {
            var png = Png(PixelFormats.Rgb24, 8, 8, [10, 130, 230]);

            AssertPixel(Read(PngToDdsImporter.Import(png, TextureType.MaterialMap, Wh3, "x.dds")), 230, 130, 10, 255);
        }

        // A .glb carries its textures inside it, so they have no path on disk; the importer read them by path and threw.
        [Test]
        public void AGlb_ImportsItsEmbeddedTextures()
        {
            var gltf = PathHelper.GetDataFile(@"Karl_Gltf_Resaved_From_Blender\karl_franz_imported_and_saved_to_blendfile_and_exported_to_gltf_from_blender.gltf");
            var glb = Path.Combine(Path.GetTempPath(), $"karl_{Guid.NewGuid():N}.glb");
            ModelRoot.Load(gltf).SaveGLB(glb);
            try
            {
                var pfs = PackFileSerivceTestHelper.Create(PathHelper.GetDataFolder("Data\\Karl_and_celestialgeneral_Pack"));
                var dialogs = new Mock<IStandardDialogs>();
                var importer = new GltfImporter(pfs, dialogs.Object, new SkeletonAnimationLookUpHelper(pfs, new Mock<IGlobalEventHub>().Object), new RmvMaterialBuilder(pfs, dialogs.Object));
                var container = PackFileContainer.CreatePackFile("new", "new.pack");

                importer.Import(new GltfImporterSettings(glb, "skeletons", container, Wh3, true, true, true, true, false, 20.0f, true));

                var textures = container.GetAllFiles().Where(x => x.Key.EndsWith(".dds")).ToList();
                Assert.That(textures, Has.Count.EqualTo(3 * 4), "base colour, material map and normal for each of the four meshes");
                var normal = Read(textures.First(x => x.Key.Contains("normal")).Value);
                Assert.That(dialogs.Invocations.Where(x => x.Method.Name.Contains("Exception") || x.Method.Name.Contains("Error")), Is.Empty);
                Assert.That(normal.Pixels.Chunk(4).All(p => p[0] >= 250 && p[2] <= 5), "every normal texel is orange: R=255, B=0");
            }
            finally
            {
                File.Delete(glb);
            }
        }

        // Advanced Import on a folder lists every importer whose extensions match; this one is the only way in for a texture alone.
        [Test]
        public void TheImportWindowsTextureEntry_AddsALowerCaseDdsOfTheTypeItsNameSays()
        {
            var path = Path.Combine(Path.GetTempPath(), $"Shield_Normal_{Guid.NewGuid():N}.png");
            File.WriteAllBytes(path, Png(PixelFormats.Rgb24, 8, 8, [200, 60, 255]));
            try
            {
                var pfs = new Mock<IPackFileService>();
                List<NewPackFileEntry> added = [];
                pfs.Setup(x => x.AddFilesToPack(It.IsAny<IPackFileContainer>(), It.IsAny<List<NewPackFileEntry>>())).Callback<IPackFileContainer, List<NewPackFileEntry>>((_, x) => added.AddRange(x));
                var importer = new PngToDdsImporterViewModel(pfs.Object);
                var source = new PackFile(path, new FileSystemSource(path));

                Assert.That((importer.CanImportFile(source), importer.TextureType), Is.EqualTo((ImportSupportEnum.HighPriority, TextureType.Normal)));
                importer.Execute(source, @"variantmeshes\tex", PackFileContainer.CreatePackFile("new", "new.pack"), Wh3);

                var file = added.Single();
                Assert.That((file.DirectoyPath, file.PackFile.Name), Is.EqualTo((@"variantmeshes\tex", Path.GetFileNameWithoutExtension(path).ToLowerInvariant() + ".dds")));
                AssertPixel(Read(file.PackFile), 255, 60, 0, 200);
            }
            finally
            {
                File.Delete(path);
            }
        }

        static byte[] Png(PixelFormat format, int width, int height, byte[] pixel)
        {
            var stride = width * pixel.Length;
            var pixels = new byte[stride * height];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = pixel[i % pixel.Length];

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, format, null, pixels, stride)));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }

        // WPF's Rgba64 is little-endian 16 bits per channel.
        static byte[] Rgba64(ushort r, ushort g, ushort b, ushort a) => [.. new[] { r, g, b, a }.SelectMany(BitConverter.GetBytes)];

        record Dds(DXGI_FORMAT Format, int Width, int Height, int MipLevels, byte[] Pixels);

        // The top level decompressed to RGBA, in the texture's own encoding.
        static Dds Read(PackFile file)
        {
            var bytes = file.DataSource.ReadData();
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                using var image = TexHelper.Instance.LoadFromDDSMemory(handle.AddrOfPinnedObject(), bytes.Length, DDS_FLAGS.NONE);
                var meta = image.GetMetadata();
                var srgb = TexHelper.Instance.IsSRGB(meta.Format);
                using var rgba = image.Decompress(0, srgb ? DXGI_FORMAT.R8G8B8A8_UNORM_SRGB : DXGI_FORMAT.R8G8B8A8_UNORM);
                var top = rgba.GetImage(0, 0, 0);
                var pixels = new byte[top.Width * top.Height * 4];
                Marshal.Copy(top.Pixels, pixels, 0, pixels.Length);
                return new Dds(meta.Format, meta.Width, meta.Height, meta.MipLevels, pixels);
            }
            finally
            {
                handle.Free();
            }
        }

        // Block compression keeps 5-6 bits of a flat colour, so a channel can land a few steps off.
        static void AssertPixel(Dds dds, int r, int g, int b, int a) =>
            Assert.That(dds.Pixels.Take(4).Select(x => (int)x), Is.EqualTo(new[] { r, g, b, a }).Within(5));
    }
}
