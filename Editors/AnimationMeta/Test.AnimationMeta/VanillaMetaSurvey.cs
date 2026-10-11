using System.IO;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.AnimationMeta.Parsing;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // Needs the game installed: set WH3_DATA to the game's data folder. Reads every .meta in the
    // three vanilla anim packs and checks that each tag decodes (no red rows in the editor),
    // re-encodes to the same bytes, and that MetaDataDatabase's WH3 use counts are current.
    public class VanillaMetaSurvey
    {
        [Test, Explicit]
        public void Warhammer3_AllVanillaMetaTags_DecodeAndRoundTrip()
        {
            var dataFolder = Environment.GetEnvironmentVariable("WH3_DATA");
            Assume.That(dataFolder != null && Directory.Exists(dataFolder), "set WH3_DATA");

            var runner = new AssetEditorTestRunner();
            var loader = (IPackFileContainerLoader)runner.ServiceProvider.GetService(typeof(IPackFileContainerLoader))!;
            var database = new MetaDataDatabase();
            var parser = new MetaDataFileParser(database);

            var seen = new Dictionary<string, (int Total, int Failed, string Example)>();
            var unparsedFiles = new List<string>();
            var changedOnResave = new List<string>();
            foreach (var packName in new[] { "anim.pack", "anim2.pack", "anim_3.pack" })
            {
                var container = loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(dataFolder!, packName), true);
                foreach (var (path, file) in container.GetAllFiles().Where(x => x.Key.EndsWith(".meta")))
                {
                    var bytes = file.DataSource.ReadData();
                    ParsedMetadataFile parsed;
                    try { parsed = parser.ParseFile(bytes); }
                    catch (Exception e) { unparsedFiles.Add($"{path}: {e.Message}"); continue; }

                    foreach (var attribute in parsed.Attributes)
                    {
                        var key = $"{attribute.Name}_{attribute.Version}";
                        var (total, failed, example) = seen.GetValueOrDefault(key, (0, 0, ""));
                        var isFailure = attribute is ParsedUnknownMetadataAttribute;
                        seen[key] = (total + 1, failed + (isFailure ? 1 : 0), isFailure && failed == 0 ? path : example);
                    }

                    if (!parsed.Attributes.Any(x => x is ParsedUnknownMetadataAttribute) && !parser.GenerateBytes(parsed.Version, parsed).SequenceEqual(bytes))
                        changedOnResave.Add(path);
                }
            }

            foreach (var (key, v) in seen.OrderBy(x => x.Key))
                TestContext.Out.WriteLine($"{key,-34} {v.Total,7} {v.Failed,7}  {v.Example}");
            TestContext.Out.WriteLine($"files that did not parse at all: {unparsedFiles.Count}");
            foreach (var f in unparsedFiles.Take(20))
                TestContext.Out.WriteLine(f);

            Assert.Multiple(() =>
            {
                Assert.That(seen.Where(x => x.Value.Failed > 0).Select(x => x.Key), Is.Empty, "tags that fail to decode");
                Assert.That(changedOnResave, Is.Empty, "files whose bytes change when saved unedited");
                Assert.That(seen.Where(x => database.GetWarhammer3UseCount(x.Key) != x.Value.Total).Select(x => $"{x.Key} = {x.Value.Total}"),
                    Is.Empty, "stale WH3 use counts in MetaDataDatabase");
            });
        }
    }
}
