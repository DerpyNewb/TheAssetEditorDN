using System.IO;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.AnimationMeta.Parsing;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // Scratch, not committed: every vanilla tag's start and end time pattern, per tag name and version.
    public class TagTimingSurveyScratch
    {
        [Test, Explicit]
        public void Scratch_TagTimingSurvey()
        {
            var dataFolder = Environment.GetEnvironmentVariable("WH3_DATA")!;
            var runner = new AssetEditorTestRunner();
            var loader = (IPackFileContainerLoader)runner.ServiceProvider.GetService(typeof(IPackFileContainerLoader))!;
            var parser = new MetaDataFileParser(new MetaDataDatabase());
            var rows = new Dictionary<string, int[]>();   // total, end==start, end>start, end==0&&start>0, end<start(other), start==0
            var widths = new Dictionary<string, List<float>>();
            var examples = new Dictionary<string, string>();
            foreach (var packName in new[] { "anim.pack", "anim2.pack", "anim_3.pack" })
            {
                var container = loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(dataFolder, packName), true);
                foreach (var (path, file) in container.GetAllFiles().Where(x => x.Key.EndsWith(".meta")))
                {
                    ParsedMetadataFile parsed;
                    try { parsed = parser.ParseFile(file.DataSource.ReadData()); } catch { continue; }
                    foreach (var a in parsed.Attributes)
                    {
                        float s, e;
                        if (a is DecodedMetaEntryBase b) { s = b.StartTime; e = b.EndTime; }
                        else if (a is DecodedMetaEntryBase_v2 c) { s = c.StartTime; e = c.EndTime; }
                        else continue;
                        var key = $"{a.Name}_{a.Version}";
                        var r = rows.TryGetValue(key, out var x) ? x : rows[key] = new int[6];
                        r[0]++;
                        if (e == s) r[1]++;
                        else if (e > s) { r[2]++; (widths.TryGetValue(key, out var w) ? w : widths[key] = []).Add(e - s); }
                        else if (e == 0) r[3]++;
                        else r[4]++;
                        if (s == 0) r[5]++;
                        examples.TryAdd(key, $"{path} s={s} e={e}");
                    }
                }
            }
            TestContext.Out.WriteLine($"SURVEY {"tag",-36} {"n",6} {"e=s",6} {"e>s",6} {"e=0",6} {"e<s",6} {"s=0",6}  median(e-s)  example");
            foreach (var (key, r) in rows.OrderByDescending(x => x.Value[0]))
            {
                var med = widths.TryGetValue(key, out var w) ? w.OrderBy(v => v).ElementAt(w.Count / 2).ToString("0.000") : "-";
                TestContext.Out.WriteLine($"SURVEY {key,-36} {r[0],6} {r[1],6} {r[2],6} {r[3],6} {r[4],6} {r[5],6}  {med,10}  {examples[key]}");
            }
        }

        [Test, Explicit]
        public void Scratch_FirePosFiles()
        {
            var dataFolder = Environment.GetEnvironmentVariable("WH3_DATA")!;
            var runner = new AssetEditorTestRunner();
            var loader = (IPackFileContainerLoader)runner.ServiceProvider.GetService(typeof(IPackFileContainerLoader))!;
            var parser = new MetaDataFileParser(new MetaDataDatabase());
            foreach (var packName in new[] { "anim.pack", "anim2.pack", "anim_3.pack" })
            {
                var container = loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(dataFolder, packName), true);
                foreach (var (path, file) in container.GetAllFiles().Where(x => x.Key.EndsWith(".meta") && (x.Key.Contains("chd") || x.Key.Contains("humanoid03"))))
                {
                    ParsedMetadataFile parsed;
                    try { parsed = parser.ParseFile(file.DataSource.ReadData()); } catch { continue; }
                    var names = parsed.Attributes.Select(a => a.Name).Where(n => n is "FIRE_POS" or "IMPACT_POS" or "TARGET_POS" or "SPLASH_ATTACK" or "CAMERA_SHAKE_POS").Distinct().OrderBy(n => n).ToList();
                    if (names.Contains("FIRE_POS"))
                        TestContext.Out.WriteLine($"FIREFILE {path} {string.Join(",", names)} " + string.Join(" ", parsed.Attributes.OfType<DecodedMetaEntryBase>().Where(a => a.Name is "FIRE_POS" or "IMPACT_POS").Select(a => $"{a.Name}@{a.StartTime:0.00}")));
                }
            }
        }
    }
}
