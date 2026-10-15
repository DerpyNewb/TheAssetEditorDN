using Microsoft.Extensions.DependencyInjection;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Bmd;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.E2EVerification
{
    // 360 of the game's 6,008 BMDs did not load: building properties v12 and v13, prop v29 and v30 and vfx v11 were newer than
    // the parser, and AI hint separators were never read. Each misread byte threw several sections later, in a reader with nothing
    // wrong with it. Every one now loads to its last byte, and every one the writer takes saves back unchanged. Needs WH3_DATA.
    internal class BmdVanilla_Tests
    {
        [Test]
        public void EveryVanillaBmd_LoadsToTheEnd_AndSavesBackUnchanged()
        {
            var data = Environment.GetEnvironmentVariable("WH3_DATA");
            Assume.That(data != null && Directory.Exists(data), "set WH3_DATA");
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var loader = runner.ServiceProvider.GetRequiredService<IPackFileContainerLoader>();

            var problems = new List<string>();
            int files = 0, refused = 0;
            foreach (var pack in new[] { "data.pack", "data_3.pack", "terrainb.pack" })
            {
                var container = loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(data!, pack), true);
                foreach (var (path, file) in container.GetAllFiles().Where(x => x.Key.EndsWith(".bmd")))
                {
                    files++;
                    var bytes = file.DataSource.ReadData();
                    BmdFile parsed;
                    using (var stream = new MemoryStream(bytes))
                    {
                        try { parsed = new BmdParser(stream).Parse(); }
                        catch (Exception e) { problems.Add($"{path}: {e.Message}"); continue; }
                        if (stream.Position != stream.Length)
                            problems.Add($"{path}: {stream.Length - stream.Position} bytes left unread");
                    }

                    byte[] written;
                    // Files older than FastBin 11 and a few legacy record versions are refused by the writer on purpose.
                    try { written = BmdWriter.Write(parsed); }
                    catch (NotSupportedException) { refused++; continue; }
                    if (!written.AsSpan().SequenceEqual(bytes))
                        problems.Add($"{path}: saves back different");
                }
            }

            Assert.That(problems, Is.Empty);
            Assert.That((files, refused), Is.EqualTo((6008, 160)), "the game's own count; a patch that adds BMDs changes it");
        }
    }
}
