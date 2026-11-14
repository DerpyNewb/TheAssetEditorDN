using System.Diagnostics;
using System.IO;
using Editors.AnimationMeta.MetaEditor.Examples;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.Dat;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // Needs the game installed: set WH3_DATA to the game's data folder. The vanilla examples scan on the real packs, and the
    // vanilla counts Check meta's messages quote.
    public class VanillaMetaExamplesSurvey
    {
        static AssetEditorTestRunner LoadVanilla(params string[] packs)
        {
            var dataFolder = Environment.GetEnvironmentVariable("WH3_DATA");
            Assume.That(dataFolder != null && Directory.Exists(dataFolder), "set WH3_DATA");
            var runner = new AssetEditorTestRunner();
            var loader = (IPackFileContainerLoader)runner.ServiceProvider.GetService(typeof(IPackFileContainerLoader))!;
            foreach (var pack in packs)
            {
                var container = loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(dataFolder!, pack), true);
                container.IsCaPackFile = true;
                runner.PackFileService.AddContainer(container);
            }
            return runner;
        }

        // 5.5 s and 9.7 s on 11 threads on 2026-11-14, against 32.5 s on one: 67,530 metas, 111 tag versions.
        [Test, Explicit]
        public void Warhammer3_ExamplesScan()
        {
            var runner = LoadVanilla("anim.pack", "anim2.pack", "anim_3.pack");
            var examples = new VanillaMetaExamples(runner.PackFileService, new MetaDataFileParser(new MetaDataDatabase()));
            var watch = Stopwatch.StartNew();
            // Waited on, not awaited: the runner's WPF objects give this thread a dispatcher that an await would post back to.
            var found = examples.Scan()!.GetAwaiter().GetResult();
            TestContext.Out.WriteLine($"scan: {watch.Elapsed.TotalSeconds:0.0} s, {found.Count} tag versions");
            foreach (var (key, list) in found.OrderBy(x => x.Key))
                TestContext.Out.WriteLine($"{key,-30} {list.Count} {string.Join(" | ", list.Select(x => x.FileName))}");

            Assert.Multiple(() =>
            {
                Assert.That(found["EFFECT_12"], Has.Count.EqualTo(VanillaMetaExamples.PerKey));
                Assert.That(found["SOUND_TRIGGER_11"].Select(x => x.Fields.Single(f => f.Name == "Sound Event").Value).Distinct().Count(), Is.EqualTo(VanillaMetaExamples.PerKey),
                    "five different events, not one footstep at five moments");
            });
        }

        // The counts Check meta quotes, and why it has no sound event check: none of vanilla's SOUND_TRIGGER event names is in
        // the event registry (audio_base_bnk.pack's event_data__*.dat). About a third end a registry name, as
        // Battle_Individual_<name> and Campaign_Individual_<name>; the rest are found nowhere in it, so the registry cannot tell
        // a real name from a typo.
        [Test, Explicit]
        public void Warhammer3_TheCountsCheckMetaQuotes()
        {
            var runner = LoadVanilla("anim.pack", "anim2.pack", "anim_3.pack", "audio_base_bnk.pack");
            var parser = new MetaDataFileParser(new MetaDataDatabase());
            var registry = runner.PackFileService.GetAllPackfileContainers().SelectMany(x => x.SearchFiles("event_data__", [".dat"]))
                .SelectMany(x => DatFileParser.Parse(x.File, false).EventWithStateGroup.Select(e => e.Event))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int timed = 0, backwards = 0, vfxTags = 0, noVfx = 0;
            var soundNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var container in runner.PackFileService.GetAllPackfileContainers())
            {
                foreach (var (_, file) in container.SearchFiles(null, [".meta"]).Where(x => x.Path.EndsWith(".meta")))
                {
                    ParsedMetadataFile parsed;
                    try { parsed = parser.ParseFile(file.DataSource.ReadData()); }
                    catch (Exception) { continue; }
                    foreach (var tag in parsed.Attributes)
                    {
                        (float Start, float End)? times = tag switch { DecodedMetaEntryBase x => (x.StartTime, x.EndTime), DecodedMetaEntryBase_v2 x => (x.StartTime, x.EndTime), _ => null };
                        if (times is { } t)
                        {
                            timed++;
                            if (t.End < t.Start - 0.001f)
                                backwards++;
                        }
                        if ((tag switch { IEffectMeta x => x.VfxName, Blood_v11 x => x.VfxName, Blood_v5 x => x.VfxName, _ => null }) is { } vfx)
                        {
                            vfxTags++;
                            if (string.IsNullOrWhiteSpace(vfx))
                                noVfx++;
                        }
                        if ((tag switch { SoundTrigger_v10 x => x.SoundEvent, SoundTrigger_v4 x => x.SoundEvent, _ => null }) is { Length: > 0 } sound)
                            soundNames.Add(sound);
                    }
                }
            }

            var tails = soundNames.Count(n => registry.Any(r => r.EndsWith(n, StringComparison.OrdinalIgnoreCase)));
            TestContext.Out.WriteLine($"registry {registry.Count}, sound names {soundNames.Count}, as a registry name's tail {tails}");
            Assert.Multiple(() =>
            {
                Assert.That((timed, backwards), Is.EqualTo((421_684, 1)), "timed tags, and those ending a millisecond or more before they start");
                Assert.That((vfxTags, noVfx), Is.EqualTo((22_817, 9)), "EFFECT and BLOOD tags, and those naming no vfx file");
                Assert.That(soundNames.Count(registry.Contains), Is.Zero, "SOUND_TRIGGER names found in the event registry as they are");
            });
        }
    }
}
