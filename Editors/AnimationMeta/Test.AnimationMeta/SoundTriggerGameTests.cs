using System.Diagnostics;
using System.IO;
using Editors.AnimationMeta.SuperView;
using Editors.Audio.SoundTriggers;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.Commands;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // Sound triggers on the game's own files, through SuperView's own LoadSounds and PrepareSound: db.pack's audio_metadata_tags_tables,
    // and the base and English UK banks and WEMs. Skipped without WH3_DATA. The audio is loaded once for the fixture. The first run after
    // a game update builds the audio cache for these packs, which takes minutes; later runs read it.
    public class SoundTriggerGameTests
    {
        static readonly string[] Packs = ["db.pack", "audio_base_bnk.pack", "audio_base.pack", "audio_en_bnk.pack", "audio_en.pack", "anim2.pack"];

        // A sword-and-shield attack's .snd.meta (CA's are named hu1_sws_attack_01.{hash}.snd.meta): a real clip's triggers, for the decode time.
        const string AttackSndMeta = "hu1_sws_attack_01.";
        const string AttackFolder = "humanoid01/sword_and_shield/attacks/";

        AssetEditorTestRunner _runner = null!;
        SoundTriggerResolver _resolver = null!;
        AudioMetadataTags _tags = null!;
        TimeSpan _loadTime;
        static readonly IReadOnlyDictionary<uint, uint> NoChoices = new Dictionary<uint, uint>();

        [OneTimeSetUp]
        public void LoadTheGamesAudio()
        {
            var data = Environment.GetEnvironmentVariable("WH3_DATA");
            Assume.That(data != null && Directory.Exists(data), "set WH3_DATA to the game's data folder");

            _runner = new AssetEditorTestRunner();
            var loader = _runner.ServiceProvider.GetRequiredService<IPackFileContainerLoader>();
            // db.pack first: the first CA pack names the audio cache file.
            foreach (var pack in Packs)
            {
                var container = loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(data!, pack), true);
                container.IsCaPackFile = true;
                _runner.PackFileService.AddContainer(container);
            }

            var clock = Stopwatch.StartNew();
            var superView = (SuperViewViewModel)_runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.SuperView_Editor);
            _resolver = superView.LoadSounds(CancellationToken.None).GetAwaiter().GetResult();
            _loadTime = clock.Elapsed;
            _tags = AudioMetadataTags.Read(_runner.PackFileService);
        }

        // At least one WEM the sound can play is in a loaded pack.
        static IReadOnlyList<uint> Leaves(SoundTriggerResult result) => [.. SoundNodes.Leaves(result.Sound!)];

        [Test]
        public void VanillaPainCry_Resolves()
        {
            Assert.That(_tags.Find("Vocalisation_Humanoid_Male_Pain")?.BattleStart, Is.EqualTo("Battle_Individual_Vocalisation_Humanoid_Male_Pain"));

            var result = _resolver.Resolve("Vocalisation_Humanoid_Male_Pain", false, NoChoices);

            Assert.That(result.Silent, Is.Null);
            Assert.That(Leaves(result).Any(x => FindWem(x) != null), Is.True);
        }

        [Test]
        public void DropWeaponRest_UsesTheTable()
        {
            Assert.That(_tags.Find("Drop_Weapon_Rest")?.BattleStart, Is.EqualTo("Battle_Individual_Melee_Weapon_Drop_Weapon_Rest"));

            var result = _resolver.Resolve("Drop_Weapon_Rest", false, NoChoices);

            Assert.That(result.Silent, Is.Null);
            Assert.That(Leaves(result).Any(x => FindWem(x) != null), Is.True);
        }

        [Test]
        public void MadeUpTag_IsSilent()
        {
            Assert.That(_resolver.Resolve("Derpy_Made_Up_Tag", false, NoChoices).Silent, Is.EqualTo("no audio_metadata_tags_tables row"));
        }

        // Final review: Battle_Individual_Melee_Weapon_Hit_Primary's one action has type 0x2303, and it reaches a dialogue event, through
        // which the hit sounds in game. SuperView does not play it, and says so instead of calling it silent in game.
        [Test]
        public void MeleeWeaponHitPrimary_IsNotSupported_AndSaysWhatItReaches()
        {
            var result = _resolver.Resolve("Melee_Weapon_Hit_Primary", false, NoChoices);
            TestContext.Out.WriteLine(result.Silent);

            Assert.That(result.Silent, Does.StartWith("not supported: 0x2303 action -> ").And.EndWith("Dialogue_Event"));
        }

        PackFile? FindWem(uint sourceId) => _runner.PackFileService.FindFile($"audio\\wwise\\{sourceId}.wem")
            ?? _runner.PackFileService.FindFile($"audio\\wwise\\english(uk)\\{sourceId}.wem");

        // A real clip's .snd.meta opened in the Sound tab with Sound on, on a clip whose tags nothing has resolved yet: its first resolves
        // read the banks, and its sources need decoding. Both must happen off the UI thread, which this test's thread stands for. Its share
        // is the browse and what the background work posts back. The times are printed for the 200 ms rule.
        [Test]
        public void RealClip_ResolvesAndDecodes_OffTheUiThread()
        {
            var file = _runner.PackFileService.GetAllPackfileContainers().SelectMany(x => x.SearchFiles(AttackSndMeta, [".snd.meta"]))
                .Where(x => x.Path.Replace('\\', '/').Contains(AttackFolder)).OrderBy(x => x.Path).FirstOrDefault();
            // The first of no files is the default tuple, whose File is null. MinBy would throw there, as the tuple is a value type.
            Assert.That(file.File, Is.Not.Null, $"the game has a .snd.meta named {AttackSndMeta}* under {AttackFolder}");
            var (ui, before) = (new UiThread(), SynchronizationContext.Current);
            SynchronizationContext.SetSynchronizationContext(ui);
            try
            {
                var superView = (SuperViewViewModel)_runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.SuperView_Editor);
                var mixer = new SoundTriggerMixer(openDevice: false);
                superView.OpenMixer = () => mixer;
                superView.LoadSounds = _ => Task.FromResult(_resolver);
                var (prepare, queued) = (superView.PrepareSound, new List<uint>());
                superView.PrepareSound = id =>
                {
                    queued.Add(id);
                    return prepare(id);
                };
                superView.SoundOn = true;
                ui.Settle(superView);
                _runner.Dialogs.Setup(x => x.DisplayBrowseDialog(It.IsAny<List<string>>(), It.IsAny<Func<string, bool>?>())).Returns(new BrowseDialogResultFile(true, file.File));
                var memory = GC.GetTotalMemory(true);

                var onThisThread = Stopwatch.StartNew();
                superView.BrowseSoundTabCommand.Execute(null);
                onThisThread.Stop();
                var firstResolves = Stopwatch.StartNew();
                Assert.That(superView.SoundDecoding.Wait(TimeSpan.FromMinutes(2)), Is.True, "resolved within two minutes");
                firstResolves.Stop();
                // The session's rebuild, against the warm cache, which queues the decodes.
                Pump();
                var whileDecoding = superView.SoundStatus.LastOrDefault();
                var decoding = Stopwatch.StartNew();
                do
                    Assert.That(superView.SoundDecoding.Wait(TimeSpan.FromMinutes(2)), Is.True, "decoded within two minutes");
                while (Pump());
                decoding.Stop();

                var sources = queued.Distinct().ToList();
                var decoded = sources.Count(id => mixer.Prepare(id, () => null));    // true only for a source already in the cache
                var triggers = superView.SoundMetaEditor.ParsedFile!.Attributes.Count(x => x is SoundTrigger_v10 or SoundTrigger_v4);
                TestContext.Out.WriteLine($"The fixture's LoadSounds took {_loadTime.TotalSeconds:0.0} s. {file.Path}: {triggers} triggers, {sources.Count} sources. " +
                    $"On the UI thread: {onThisThread.Elapsed.TotalMilliseconds:0} ms. Off it: the first resolves {firstResolves.Elapsed.TotalMilliseconds:0} ms, " +
                    $"{decoded} decoded in {decoding.Elapsed.TotalMilliseconds:0} ms, {(GC.GetTotalMemory(true) - memory) / 1_048_576.0:0} MB. " +
                    string.Join(" | ", superView.SoundStatus));
                Assert.That(triggers, Is.GreaterThan(0));
                Assert.That(decoded, Is.GreaterThan(0), "real WEMs decode");
                Assert.That(onThisThread.Elapsed.TotalMilliseconds, Is.LessThan(200), "the first resolves and the decoding are not on the UI thread");
                Assert.That((firstResolves.Elapsed + onThisThread.Elapsed).TotalMilliseconds, Is.GreaterThan(200), "the clip was cold, or this measures nothing");
                Assert.That(whileDecoding, Is.EqualTo("Decoding sounds..."), "the status says so while the decoding runs");
                Assert.That(superView.SoundStatus, Has.None.EqualTo("Decoding sounds..."), "and not after");
                GC.KeepAlive(mixer);

                bool Pump()
                {
                    onThisThread.Start();
                    var ran = ui.RunPosted();
                    onThisThread.Stop();
                    return ran;
                }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(before);
            }
        }
    }
}
