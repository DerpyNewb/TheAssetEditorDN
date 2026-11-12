using System.IO;
using Editors.VfxEditor.VariantMesh;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Test.TestingUtility.Shared;

namespace Test.VfxEditor
{
    // Every variant mesh file in the game's variants.pack, from the data folder in WH3_DATA; skipped without it.
    class VanillaVmdTests
    {
        [Test]
        public void Every_CA_file_opens_and_saves_byte_identical()
        {
            var dataFolder = Environment.GetEnvironmentVariable("WH3_DATA");
            Assume.That(dataFolder != null && Directory.Exists(dataFolder), "set WH3_DATA");
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var loader = (IPackFileContainerLoader)runner.ServiceProvider.GetService(typeof(IPackFileContainerLoader))!;
            var container = runner.PackFileService.AddContainer(
                loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(dataFolder!, "variants.pack"), true))!;
            var files = runner.PackFileService.FindAllWithExtention(".variantmeshdefinition", container);

            var opened = 0;
            var refused = new List<string>();
            foreach (var file in files)
            {
                var bytes = file.Pack.DataSource.ReadData();
                var (document, error) = VmdLayout.Load(bytes);
                if (document == null)
                {
                    Assert.That(error, Is.Not.Null.And.Not.Empty, file.FileName);
                    refused.Add(file.FileName);
                    continue;
                }
                Assert.That(VmdLayout.Rows(document), Is.Not.Empty, file.FileName);
                Assert.That(document.Save(), Is.EqualTo(bytes), file.FileName);
                opened++;
            }

            // Survey of 2026-11-13: 3,483 files, 9 with mismatched tags (cth_clawspeaker and others).
            Assert.That((opened, refused.Count), Is.EqualTo((3474, 9)), string.Join("\n", refused));
        }
    }
}
