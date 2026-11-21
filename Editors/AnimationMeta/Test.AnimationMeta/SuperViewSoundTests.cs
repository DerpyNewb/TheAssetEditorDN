using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView;
using Editors.Audio.SoundTriggers;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.BaseControl;
using Microsoft.Xna.Framework;
using Shared.Core.Commands;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimationMeta
{
    // The UI thread's queue, run by hand: what SuperView's sound work posts back runs when a test says so. Installed before SuperView
    // opens, as SuperView takes the context it opens on.
    sealed class UiThread : SynchronizationContext
    {
        readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _posted = new();

        public override void Post(SendOrPostCallback d, object? state) => _posted.Enqueue((d, state));

        /// <summary>Runs what was posted, and what that posts, and says whether anything ran.</summary>
        public bool RunPosted()
        {
            var ran = false;
            while (_posted.TryDequeue(out var item))
            {
                item.Callback(item.State);
                ran = true;
            }
            return ran;
        }

        /// <summary>Until the background work has ended and all it posted has run.</summary>
        public void Settle(SuperViewViewModel superView)
        {
            do
                Assert.That(superView.SoundDecoding.Wait(TimeSpan.FromMinutes(2)), Is.True, "the background work ended");
            while (RunPosted());
        }
    }

    // SuperView's Sound toggle on Throt's ATTACK_5. The game's audio is replaced by a small in-memory bank, and the four sound
    // delegates by recorders: no audio device opens and no repository loads.
    public class SuperViewSoundTests
    {
        const string Decoding = "Decoding sounds...";
        const string ThrotVmd = @"variantmeshes\variantmeshdefinitions\skv_throt.variantmeshdefinition";
        const string ThrotTable = @"animations/database/battle/bin/hu17_dlc16_throt.bin";
        const string SndMeta = "animations/derpy/throt_attack_5.snd.meta";

        // Each tag's battle event plays one sound, its leaf.
        const uint PersistLeaf = 11, ClipLeaf = 12, SlotLeaf = 13, PersistSndLeaf = 14, Dirt = 21, Stone = 22, BattleLeaf = 31, CampaignLeaf = 32;

        AssetEditorTestRunner _runner = null!;
        SuperViewViewModel _superView = null!;
        Bank _bank = null!;
        readonly List<TaskCompletionSource<SoundTriggerResolver>> _loads = [];
        readonly List<uint> _played = [], _prepared = [];
        int _stops;
        UiThread _ui = null!;
        SynchronizationContext? _before;

        [SetUp]
        public void Setup()
        {
            _before = SynchronizationContext.Current;
            _ui = new UiThread();
            SynchronizationContext.SetSynchronizationContext(_ui);
            _loads.Clear();
            _played.Clear();
            _prepared.Clear();
            _stops = 0;
            _bank = new Bank();
            _bank.Tag("Derpy_Persist", PersistLeaf);
            _bank.Tag("Derpy_Clip", ClipLeaf);
            _bank.Tag("Derpy_Slot", SlotLeaf);
            _bank.Tag("Derpy_PersistSnd", PersistSndLeaf);

            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            _superView = Open();
            _superView.Load(new AnimationToolInput()
            {
                Mesh = _runner.PackFileService.FindFile(ThrotVmd)!,
                FragmentName = ThrotTable,
                AnimationSlot = DefaultAnimationSlotTypeHelper.GetfromValue("ATTACK_5")
            });
            Assert.That(_superView.SlotGrid.SelectedRow?.Slot.SlotName, Is.EqualTo("ATTACK_5"), "Throt ATTACK_5 did not load");
            Assert.That(_superView.PersistentMetaEditor.ParsedFile, Is.Not.Null, "Throt's persistent meta");
            Assert.That(_superView.MetaEditor.ParsedFile, Is.Not.Null, "ATTACK_5's meta");
            _stops = 0;
        }

        [TearDown]
        public void RestoreTheContext() => SynchronizationContext.SetSynchronizationContext(_before);

        void Settle() => _ui.Settle(_superView);

        SuperViewViewModel Open()
        {
            var superView = (SuperViewViewModel)_runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.SuperView_Editor);
            superView.LoadSounds = _ =>
            {
                var load = new TaskCompletionSource<SoundTriggerResolver>();
                _loads.Add(load);
                return load.Task;
            };
            superView.PrepareSound = id =>
            {
                _prepared.Add(id);
                return true;
            };
            superView.PlaySound = _played.Add;
            superView.StopSounds = () => _stops++;
            superView.SoundName = id => _bank.Names.GetValueOrDefault(id, id.ToString());
            superView.OpenMixer = () => new SoundTriggerMixer(openDevice: false);
            return superView;
        }

        // The last load, or the one given, finishes with the bank, and what that sets going runs to its end.
        void Finish(int? load = null)
        {
            _loads[load ?? _loads.Count - 1].SetResult(_bank.Resolver());
            Settle();
        }

        SceneObject Main => _superView.SceneObjects[0].Data;
        MetaDataFileParser Parser => _runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>();

        SoundTrigger_v11 Trigger(string soundEvent, float at)
        {
            var tag = (SoundTrigger_v11)Parser.CreateDefault("SOUND_TRIGGER_11");
            (tag.StartTime, tag.EndTime, tag.SoundEvent) = (at, at, soundEvent);
            return tag;
        }

        // An unsaved edit: the tag is added to the tab's file and the rows rebuilt, as the tab's own add does.
        void AddTag(MetaDataEditorViewModel editor, string soundEvent, float at)
        {
            editor.ParsedFile!.Attributes.Add(Trigger(soundEvent, at));
            editor.UpdateView();
        }

        string WriteSndMeta(string path, string soundEvent, float at)
        {
            var file = new ParsedMetadataFile { Version = 2, Attributes = [Trigger(soundEvent, at)] };
            var pfs = _runner.PackFileService;
            pfs.AddFilesToPack(pfs.GetEditablePack()!, [new NewPackFileEntry(Path.GetDirectoryName(path)!, PackFile.CreateFromBytes(Path.GetFileName(path), Parser.GenerateBytes(2, file)))]);
            return path;
        }

        // One SOUND_TRIGGER in each of the three files: the persistent meta at 0.1 s, the clip meta at 0.2 s, the slot's .snd.meta at 0.3 s.
        void AddATriggerToEachFile()
        {
            AddTag(_superView.PersistentMetaEditor, "Derpy_Persist", 0.1f);
            AddTag(_superView.MetaEditor, "Derpy_Clip", 0.2f);
            _superView.SlotSound = WriteSndMeta(SndMeta, "Derpy_Slot", 0.3f);
        }

        int TriggersInTheThreeFiles() => new[] { _superView.PersistentMetaEditor, _superView.MetaEditor, _superView.SoundMetaEditor }
            .Sum(x => x.ParsedFile?.Attributes.Count(a => a is SoundTrigger_v10 or SoundTrigger_v4) ?? 0);

        // 20 ms frames from the clip's start, as SuperViewSpeedTests.Run plays them, until the playhead reaches a time.
        void PlayTo(float seconds)
        {
            var player = Main.Player;
            Assert.That(player.GetAnimationLengthUs(), Is.GreaterThan(seconds * 1_000_000), "the clip is long enough not to loop");
            player.SetTime(0);
            player.Play();
            while (player.GetTimeUs() < seconds * 1_000_000)
                player.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(20)));
        }

        // The status lines below start with the .snd.meta problems: Throt's slots name CA's .snd.meta files, which no pack here has.
        string[] Status(params string[] then) => [.. _superView.SoundFileProblems, .. then];

        [Test]
        public void SoundOff_ByDefault_AndNothingLoads()
        {
            Assert.That(_superView.SoundOn, Is.False);
            Assert.That(_loads, Is.Empty);
            Assert.That(_superView.SoundStatus, Is.EqualTo(Status()));
        }

        [Test]
        public void SoundOn_ShowsLoading_ThenTheStatus()
        {
            AddATriggerToEachFile();
            var triggers = TriggersInTheThreeFiles();

            _superView.SoundOn = true;

            Assert.That(_superView.SoundStatus, Is.EqualTo(Status("Loading sounds...")));
            Finish();
            var summary = _superView.SoundStatus[_superView.SoundFileProblems.Count];
            Assert.That(summary, Does.StartWith($"{triggers} triggers:"));
            Assert.That(summary, Does.Contain(" 3 play,"), "the three added tags play");
        }

        [Test]
        public void Playing_FiresTagsFromAllThreeFiles()
        {
            AddATriggerToEachFile();
            _superView.SoundOn = true;
            Finish();

            PlayTo(0.35f);

            Assert.That(_played, Is.EqualTo(new[] { PersistLeaf, ClipLeaf, SlotLeaf }));
            Assert.That(_prepared, Is.SupersetOf(new[] { PersistLeaf, ClipLeaf, SlotLeaf }), "decoded ahead of the firing");
        }

        // SuperView starts each sound SoundLeadSeconds early, for the audio device's lag. With 20 ms frames, a tag at 1.0 s is due at
        // 0.974 s, so it fires on the frame landing at 0.98 s and not on the one landing at 1.0 s.
        [Test]
        public void Playing_StartsEachSoundTheLeadEarly()
        {
            Assert.That(1.0 - SuperViewViewModel.SoundLeadSeconds, Is.InRange(0.96, 0.98), "the lead puts the tag's due time in the frame landing at 0.98 s");
            Assert.That(Main.Player.GetAnimationLengthUs(), Is.GreaterThan(1_000_000), "the clip is long enough not to loop");
            AddTag(_superView.MetaEditor, "Derpy_Clip", 1.0f);
            _superView.SoundOn = true;
            Finish();

            PlayTo(0.96f);
            var before = _played.ToList();
            Main.Player.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(20)));
            var atTheLead = _played.ToList();
            Main.Player.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(20)));

            Assert.Multiple(() =>
            {
                Assert.That(before, Is.Empty);
                Assert.That(atTheLead, Is.EqualTo(new[] { ClipLeaf }), "fired by the frame crossing 0.974 s");
                Assert.That(_played, Is.EqualTo(new[] { ClipLeaf }), "not again at 1.0 s");
            });
        }

        void Frames(int count)
        {
            for (var i = 0; i < count; i++)
                Main.Player.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(20)));
        }

        // Final review, Critical 1: with Loop on, the view model starts the clip again by itself as playback enters the last frame, by
        // a paused jump to frame 0, so the session never saw the clip's end. Over three loops of the real view model's loop, from First
        // Frame and Play: a tag at 0.0, one in the last animation frame and one at the clip's length each fire once a pass, and the one
        // at 0.0 once more, at the start.
        [Test]
        public void Looping_FiresTheTagsAtBothEnds_OncePerPass()
        {
            var player = Main.Player;
            var length = player.GetAnimationLengthUs() / 1_000_000d;
            var frame = length / player.FrameCount();
            var end = (float)length;
            if (end > length)
                end = MathF.BitDecrement(end);
            AddTag(_superView.MetaEditor, "Derpy_Persist", 0f);
            AddTag(_superView.MetaEditor, "Derpy_Clip", (float)(length - frame / 2));
            AddTag(_superView.MetaEditor, "Derpy_Slot", end);
            _superView.SoundOn = true;
            Finish();
            var loops = 0;
            _superView.Player.Looping += () => loops++;
            Assert.That(_superView.Player.LoopAnimation.Value, Is.True, "Loop is on");

            _superView.Player.SetAnimationNextFrame();
            _superView.Player.SetAnimationFirstFrame();
            _superView.Player.ToggleAnimationPausePlay();
            for (var i = 0; loops < 3 && i < 10_000; i++)
                Frames(1);

            Assert.That(loops, Is.EqualTo(3));
            Assert.That(_played.GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count()),
                Is.EqualTo(new Dictionary<uint, int> { [PersistLeaf] = 4, [ClipLeaf] = 3, [SlotLeaf] = 3 }));
        }

        // Critical 1's other two cases start from a paused frame 0: First Frame then Play, and Play once the clip has run out with Loop
        // off. Each fires a tag at 0.0 once.
        [Test]
        public void FirstFrameThenPlay_AndPlayAtTheEnd_FireATagAtZeroOnce()
        {
            AddTag(_superView.MetaEditor, "Derpy_Persist", 0f);
            _superView.SoundOn = true;
            Finish();
            PlayTo(1.0f);
            _played.Clear();

            _superView.Player.SetAnimationFirstFrame();
            _superView.Player.ToggleAnimationPausePlay();
            Frames(10);
            var firstFrame = _played.ToList();

            _superView.Player.LoopAnimation.Value = false;
            for (var i = 0; Main.Player.IsPlaying && i < 10_000; i++)
                Frames(1);
            // What the end of the pass fired is not this test's question.
            _played.Clear();
            _superView.Player.ToggleAnimationPausePlay();
            Frames(10);

            Assert.Multiple(() =>
            {
                Assert.That(firstFrame, Is.EqualTo(new[] { PersistLeaf }), "First Frame, then Play");
                Assert.That(_played, Is.EqualTo(new[] { PersistLeaf }), "Play at the end");
            });
        }

        // Follow-up P1: with Loop off, a tag at 0.0 sounds once at each Play from the start. The lead used to put it 26 ms before the
        // clip's end, as if a loop followed, so it sounded there as well.
        [Test]
        public void LoopOff_ATagAtZeroSoundsOnceAtEachPlay_AndNotAtTheEnd()
        {
            AddTag(_superView.MetaEditor, "Derpy_Persist", 0f);
            _superView.SoundOn = true;
            Finish();
            _superView.Player.LoopAnimation.Value = false;

            _superView.Player.SetAnimationNextFrame();
            _superView.Player.SetAnimationFirstFrame();
            _superView.Player.ToggleAnimationPausePlay();
            for (var i = 0; Main.Player.IsPlaying && i < 10_000; i++)
                Frames(1);
            var thePass = _played.ToList();
            _superView.Player.ToggleAnimationPausePlay();
            Frames(10);

            Assert.Multiple(() =>
            {
                Assert.That(Main.Player.GetTimeUs(), Is.LessThan(Main.Player.GetAnimationLengthUs()), "Play at the end started the clip again");
                Assert.That(thePass, Is.EqualTo(new[] { PersistLeaf }), "once, at the start of the pass");
                Assert.That(_played, Is.EqualTo(new[] { PersistLeaf, PersistLeaf }), "and once more at the next Play");
            });
        }

        // Follow-up P2: with Loop off, the player clamps the step that passes the clip's end and reports it paused. A tag due inside
        // that step sounds on it. The frames are picked so the last playing one lands more than the lead before the end, which leaves a
        // tag at the end due inside the step that reaches it. They stay under the 50 ms of slack, as the real time between them is nil.
        [Test]
        public void LoopOff_ATagDueInTheStepThatReachesTheEnd_Sounds()
        {
            var lengthUs = Main.Player.GetAnimationLengthUs();
            var leadUs = (long)(SuperViewViewModel.SoundLeadSeconds * 1_000_000);
            var frameMs = Enumerable.Range(30, 20).First(ms => lengthUs - (lengthUs - 1) / (ms * 1000L) * ms * 1000L > leadUs + 2000);
            var end = (float)(lengthUs / 1_000_000d);
            if (end > lengthUs / 1_000_000d)
                end = MathF.BitDecrement(end);
            AddTag(_superView.MetaEditor, "Derpy_Slot", end);
            _superView.SoundOn = true;
            Finish();
            _superView.Player.LoopAnimation.Value = false;

            _superView.Player.SetAnimationNextFrame();
            _superView.Player.SetAnimationFirstFrame();
            _superView.Player.ToggleAnimationPausePlay();
            for (var i = 0; Main.Player.IsPlaying && i < 10_000; i++)
                Main.Player.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(frameMs)));

            Assert.That(Main.Player.GetTimeUs(), Is.EqualTo(lengthUs), "the clip ran out");
            Assert.That(_played, Is.EqualTo(new[] { SlotLeaf }));
        }

        // Final review, minor 4: replaying a slot puts the new clip back on the last one's frame index, which raises a frame while the
        // last clip's triggers are still in the session. They must not fire on it. The tag is due just before that frame's start, inside
        // the span a jump back to it covers. An edit to the playing row's Sound is what replays it.
        [Test]
        public void ASlotReplay_FiresNothingOfTheLastClip_OnTheFrameItPutsBack()
        {
            var player = Main.Player;
            var frameUs = player.GetAnimationLengthUs() / player.FrameCount();
            var at = 0.30f;
            if ((long)(at * 1_000_000) % frameUs == 0)
                at = 0.32f;
            var restored = (long)(at * 1_000_000) / frameUs * frameUs / 1_000_000d;
            AddTag(_superView.MetaEditor, "Derpy_Clip", (float)(restored + SuperViewViewModel.SoundLeadSeconds - 5e-7));
            _superView.SoundOn = true;
            Finish();
            PlayTo(at);
            Assert.That(_played, Is.EqualTo(new[] { ClipLeaf }), "the tag fires on its own pass");
            _played.Clear();

            _superView.SlotSound = WriteSndMeta(SndMeta, "Derpy_Slot", 3.0f);

            Assert.That(player.GetTimeUs() / 1_000_000d, Is.EqualTo(restored).Within(1e-6), "the replay put the clip back on its frame");
            Assert.That(_played, Is.Empty);
        }

        // The fourth file: the .snd.meta of the row the persistent meta comes from, which has no tab. Set in the slot grid and saved
        // while sound is on, as only the saved set's persistent row is read. No tab's file changes, so only the scene update rebuilds.
        [Test]
        public void Playing_FiresThePersistentRowsSndMeta()
        {
            _superView.SoundOn = true;
            Finish();
            var path = WriteSndMeta("animations/derpy/throt_persistent.snd.meta", "Derpy_PersistSnd", 0.1f);
            _superView.SlotGrid.Rows.First(x => x.Slot.SlotName == "PERSISTENT_METADATA_ALIVE").Sound = path;
            Assert.That(_superView.Save(), Is.True);
            Settle();

            PlayTo(0.15f);

            Assert.That(_played, Is.EqualTo(new[] { PersistSndLeaf }));
            Assert.That(_superView.SoundStatus, Has.None.Contains("persistent .snd.meta"), "it resolves, so no line names it");
        }

        // An edit made while sound is on plays on the next pass, with no reload.
        [Test]
        public void AnEditWhileOn_PlaysOnTheNextPass()
        {
            _superView.SoundOn = true;
            Finish();

            AddTag(_superView.MetaEditor, "Derpy_Clip", 0.2f);
            Settle();
            PlayTo(0.25f);

            Assert.That(_played, Is.EqualTo(new[] { ClipLeaf }));
        }

        // Ruling 13: while the background work for the triggers is still running, the status's last line says so, and the line goes
        // when the work ends. The bank holds every lookup made off this thread, so the work is held where the first resolves run.
        [Test]
        public void Decoding_ShowsUntilTheBackgroundWorkEnds()
        {
            _superView.SoundOn = true;
            Finish();
            Assert.That(_superView.SoundStatus, Has.None.EqualTo(Decoding));

            using var hold = new ManualResetEventSlim();
            _bank.Hold = hold;
            AddTag(_superView.MetaEditor, "Derpy_Clip", 0.2f);
            _ui.RunPosted();
            Assert.That((_superView.SoundDecoding.IsCompleted, _superView.SoundStatus[^1]), Is.EqualTo((false, Decoding)));

            hold.Set();
            Settle();
            Assert.That(_superView.SoundStatus, Has.None.EqualTo(Decoding));
            Assert.That(_superView.SoundStatus[_superView.SoundFileProblems.Count], Does.Contain(" 1 plays,"), "the added tag is in the status");
        }

        // Fix round 1: another slot's clip plays nothing of the last clip's triggers while its own rebuild is on its way, and the status
        // drops the last clip's summary meanwhile. The rebuild's apply waits in the UI thread's queue, as it does behind a cold warm-up.
        [Test]
        public void ANewSlot_PlaysNoneOfTheLastClipsTriggers_UntilItsRebuildLands()
        {
            AddTag(_superView.MetaEditor, "Derpy_Clip", 0.2f);
            _superView.SoundOn = true;
            Finish();
            PlayTo(0.25f);
            Assert.That(_played, Is.EqualTo(new[] { ClipLeaf }), "the tag fires on its own clip");
            _played.Clear();

            var slots = _superView.SceneObjects[0].FragAndSlotSelection.FragmentSlotList;
            // Another attack, long enough to play to 0.25 s.
            slots.SelectedItem = slots.PossibleValues.First(x => x.SlotName.StartsWith("ATTACK_") && x.SlotName != "ATTACK_5" && _runner.PackFileService.FindFile(x.AnimationFile) != null);
            PlayTo(0.25f);

            Assert.That(_played, Is.Empty, "the last clip's trigger did not fire on this one");
            Assert.That(_superView.SoundStatus, Is.EqualTo(Status(Decoding)), "the last clip's summary is gone");
            Settle();
            PlayTo(0.25f);
            Assert.That(_played, Is.Empty, "and the new clip's own triggers do not have it");
        }

        // Ruling 14: a warm-up that a later rebuild made stale stops at its next tag. Sound off and on twice: the first warm-up is held
        // at its first lookup, and by the time it goes on, it is stale.
        [Test]
        public void AStaleWarmUp_StopsAtItsNextTag()
        {
            AddATriggerToEachFile();
            _superView.SoundOn = true;
            Finish();
            using var hold = new ManualResetEventSlim();
            _bank.Hold = hold;
            _bank.OffThread.Clear();

            _superView.SoundOn = false;
            _superView.SoundOn = true;
            _superView.SoundOn = false;
            _superView.SoundOn = true;
            hold.Set();
            Settle();

            var lastTag = WwiseHash.Compute("Derpy_Slot_Event");
            Assert.That(_bank.OffThread.Count(x => x == lastTag), Is.EqualTo(1), "only the latest warm-up reached the last tag");
        }

        // Ruling 15: a background job that throws ends cleanly, so SoundDecoding does not fault, and the status says why until the next
        // rebuild.
        [Test]
        public void AJobThatThrows_EndsCleanly_AndSaysWhy()
        {
            _superView.SoundOn = true;
            Finish();
            _bank.Throw = new InvalidOperationException("the bank is gone");

            AddTag(_superView.MetaEditor, "Derpy_Clip", 0.2f);
            Settle();

            Assert.That(_superView.SoundDecoding.IsFaulted, Is.False);
            Assert.That(_superView.SoundStatus, Is.EqualTo(Status("Sounds could not be prepared: the bank is gone")));
            _bank.Throw = null;
            _superView.SoundOn = false;
            _superView.SoundOn = true;
            Settle();
            Assert.That(_superView.SoundStatus, Has.None.StartsWith("Sounds could not be prepared"));
        }

        // Deferred item D25: when the first warm-up fails, its failure takes the place of "Loading sounds...", which would otherwise stay
        // up for good, as the session never takes a rebuild.
        [Test]
        public void TheFirstWarmUpsFailure_ReplacesLoadingSounds()
        {
            AddTag(_superView.MetaEditor, "Derpy_Clip", 0.2f);
            _bank.Throw = new InvalidOperationException("the bank is gone");

            _superView.SoundOn = true;
            Finish();

            Assert.That(_superView.SoundStatus, Is.EqualTo(Status("Sounds could not be prepared: the bank is gone")));
        }

        // Deferred item D25: a job that a later rebuild made stale says nothing when it fails, as its rebuild is no longer the one the
        // status describes. The stale job is held inside its first lookup, so the later rebuild is asked for while it is still running.
        [Test]
        public void AStaleJobsFailure_IsDropped()
        {
            AddTag(_superView.MetaEditor, "Derpy_Clip", 0.2f);
            _superView.SoundOn = true;
            Finish();
            using var hold = new ManualResetEventSlim();
            _bank.Hold = hold;
            _bank.Throw = new InvalidOperationException("the bank is gone");

            AddTag(_superView.MetaEditor, "Derpy_Slot", 0.3f);
            Assert.That(SpinWait.SpinUntil(() => _bank.Held > 0, TimeSpan.FromSeconds(30)), Is.True, "the first job is inside a lookup");
            _bank.Hold = null;
            AddTag(_superView.MetaEditor, "Derpy_Persist", 0.1f);
            hold.Set();
            Settle();

            Assert.That(_superView.SoundStatus, Has.None.StartsWith("Sounds could not be prepared"));
            Assert.That(_superView.SoundStatus[_superView.SoundFileProblems.Count], Does.Contain(" 3 play,"), "the later rebuild landed");
        }

        // Deferred item D22: another slot's clip shows none of the last clip's switch pickers while its own rebuild is on its way, as the
        // status already drops the last clip's lines then.
        [Test]
        public void ANewSlot_HidesTheLastClipsPickers_UntilItsRebuildLands()
        {
            _bank.Rows["Derpy_Step"] = new AudioTagEvents(_bank.Event("Derpy_Step_Event", _bank.Switch("Surface", "Dirt", ("Dirt", _bank.Sound(Dirt)), ("Stone", _bank.Sound(Stone)))), "");
            AddTag(_superView.MetaEditor, "Derpy_Step", 0.1f);
            _superView.SoundOn = true;
            Finish();
            Assert.That(_superView.SoundSwitchPickers, Has.Count.EqualTo(1));

            var slots = _superView.SceneObjects[0].FragAndSlotSelection.FragmentSlotList;
            slots.SelectedItem = slots.PossibleValues.First(x => x.SlotName.StartsWith("ATTACK_") && x.SlotName != "ATTACK_5" && _runner.PackFileService.FindFile(x.AnimationFile) != null);

            Assert.That(_superView.SoundSwitchPickers, Is.Empty, "while the rebuild is on its way");
            Settle();
            Assert.That(_superView.SoundSwitchPickers, Is.Empty, "the new clip's triggers reach no switch");
        }

        // Review Focus 3: a load that finishes after sound was turned off builds nothing, and turning sound on again loads afresh.
        // Closing SuperView before a load finishes throws nothing when it does.
        [Test]
        public void SoundOffDuringLoad_PlaysNothing()
        {
            AddATriggerToEachFile();
            _superView.SoundOn = true;
            _superView.SoundOn = false;
            Finish(0);
            _superView.SoundOn = true;

            PlayTo(0.35f);

            Assert.That(_played, Is.Empty);
            Assert.That((_loads.Count, _superView.SoundStatus), Is.EqualTo((2, Status("Loading sounds..."))), "the stale load built no session");

            _superView.Close();
            Assert.DoesNotThrow(() => Finish(1));
            PlayTo(0.35f);
            Assert.That(_played, Is.Empty);
        }

        // Review Focus 5: another slot stops the sounds still ringing, and so does turning sound off.
        [Test]
        public void SlotChange_StopsSounds()
        {
            _superView.SoundOn = true;
            Finish();
            var slots = _superView.SceneObjects[0].FragAndSlotSelection.FragmentSlotList;

            slots.SelectedItem = slots.PossibleValues.First(x => x.SlotName != "ATTACK_5" && _runner.PackFileService.FindFile(x.AnimationFile) != null);
            Assert.That(_stops, Is.EqualTo(1));

            _superView.SoundOn = false;
            Assert.That(_stops, Is.EqualTo(2));
        }

        [Test]
        public void Picker_ChangesWhatPlays()
        {
            _bank.Rows["Derpy_Step"] = new AudioTagEvents(_bank.Event("Derpy_Step_Event", _bank.Switch("Surface", "Dirt", ("Dirt", _bank.Sound(Dirt)), ("Stone", _bank.Sound(Stone)))), "");
            AddTag(_superView.MetaEditor, "Derpy_Step", 0.1f);
            _superView.SoundOn = true;
            Finish();

            var picker = _superView.SoundSwitchPickers.Single();
            Assert.That((picker.GroupName, picker.Options.Select(x => x.ToString()), picker.Selected.Name),
                Is.EqualTo(("Surface", new[] { "Dirt (default)", "Stone" }, "Dirt")));
            PlayTo(0.15f);
            Assert.That(_played, Is.EqualTo(new[] { Dirt }));

            picker.Selected = picker.Options.Single(x => x.Name == "Stone");
            Settle();
            PlayTo(0.15f);

            Assert.That(_played, Is.EqualTo(new[] { Dirt, Stone }));
            Assert.That(_superView.SoundSwitchPickers.Single().Selected.Name, Is.EqualTo("Stone"), "the picker keeps the choice");
        }

        // A campaign set's slot plays the row's campaign event. The set is written before a second SuperView opens, as sets are listed
        // when the skeleton loads.
        [Test]
        public void CampaignSet_UsesTheCampaignEvent()
        {
            const string CamThrot = "animations/campaign/database/bin/cam_throt.bin";
            _bank.Rows["Derpy_Both"] = new AudioTagEvents(_bank.Event("Derpy_Battle_Event", _bank.Sound(BattleLeaf)), _bank.Event("Derpy_Campaign_Event", _bank.Sound(CampaignLeaf)));
            var attack = _superView.SceneObjects[0].FragAndSlotSelection.FragmentSlotList.SelectedItem!;
            var sound = WriteSndMeta("animations/derpy/cam_throt.snd.meta", "Derpy_Both", 0.1f);
            var bin = new CampaignAnimationBin
            {
                Version = 3,
                SkeletonName = _superView.SlotGrid.Set!.SkeletonName,
                // A global status first, as SuperViewOpenFileTests' campaign set has.
                Status =
                [
                    new() { Name = "global", PersitantMetaData = [], Poses = [], Docks = [] },
                    new() { Name = "status_battle", Action = [new() { Animation = attack.AnimationFile, Type = "global", Meta = attack.MetaFile, SoundMeta = sound, ActionType = "battle_fatality", ActionId = 1 }] },
                ],
            };
            _runner.PackFileService.AddFilesToPack(_runner.PackFileService.GetEditablePack()!,
                [new NewPackFileEntry(@"animations\campaign\database\bin", PackFile.CreateFromBytes("cam_throt.bin", CampaignAnimationBinLoader.Write(bin, "cam_throt")))]);
            _superView = Open();
            _superView.Load(new AnimationToolInput { Mesh = _runner.PackFileService.FindFile(ThrotVmd)! });
            var selection = _superView.SceneObjects[0].FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == CamThrot);
            selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "status_battle/battle_fatality");
            Assert.That(_superView.SlotGrid.Set, Is.InstanceOf<CampaignAnimationSet>());

            _superView.SoundOn = true;
            Finish();

            Assert.That(_prepared, Does.Contain(CampaignLeaf).And.No.Contains(BattleLeaf));
        }

        // Ruling 2: with no audio device to open, sound stays off and the status says why. Nothing is loaded.
        [Test]
        public void NoAudioDevice_SaysSo_AndSoundStaysOff()
        {
            _superView.OpenMixer = () => throw new InvalidOperationException("no device here");

            _superView.SoundOn = true;

            Assert.That((_superView.SoundOn, _loads.Count), Is.EqualTo((false, 0)));
            Assert.That(_superView.SoundStatus, Is.EqualTo(Status("No audio device could be opened: no device here")));
        }

        // Ruling 6: a .snd.meta problem shows whether sound is on or off, and the trigger lines only while it is on.
        [Test]
        public void Status_ShowsFileProblems_AlwaysAndTheTriggers_OnlyWithSoundOn()
        {
            const string Missing = "animations/derpy/nowhere.snd.meta";
            _superView.SlotSound = Missing;
            Assert.That(_superView.SoundFileProblems, Does.Contain($"Slot .snd.meta {Missing} is in no loaded pack."));
            Assert.That(_superView.SoundStatus, Is.EqualTo(Status()));

            _superView.SoundOn = true;
            Assert.That(_superView.SoundStatus, Is.EqualTo(Status("Loading sounds...")));
            Finish();
            var problems = _superView.SoundFileProblems.Count;
            Assert.That(_superView.SoundStatus.Take(problems), Is.EqualTo(Status()));
            Assert.That(_superView.SoundStatus[problems], Does.Match("^[0-9]+ triggers?"));

            _superView.SoundOn = false;
            Assert.That(_superView.SoundStatus, Is.EqualTo(Status()));
        }

        // The strip above the tabs: the Sound box turns sound on, the status lines and the pickers show, and the Sound tab says when
        // its file changed on disk under unsaved edits, and is gone when it did not.
        [Test, Apartment(ApartmentState.STA)]
        public void TheSoundStrip_IsBound()
        {
            _bank.Rows["Derpy_Step"] = new AudioTagEvents(_bank.Event("Derpy_Step_Event", _bank.Switch("Surface", "Dirt", ("Dirt", _bank.Sound(Dirt)), ("Stone", _bank.Sound(Stone)))), "");
            AddTag(_superView.MetaEditor, "Derpy_Step", 0.1f);
            _superView.SelectedTabControllerIndex = (int)MetaTab.Sound;
            AppResources.Load();
            var panel = new EditorView { DataContext = _superView };
            Lay(panel);

            Descendants(panel).OfType<CheckBox>().Single(x => AutomationProperties.GetAutomationId(x) == "SoundOn").IsChecked = true;
            Assert.That(_superView.SoundOn, Is.True);
            Finish();
            Lay(panel);
            Assert.That(Descendants(panel).OfType<TextBlock>().Select(x => x.Text), Is.SupersetOf(_superView.SoundStatus));
            var combo = Descendants(panel).OfType<ComboBox>().Single(x => AutomationProperties.GetAutomationId(x) == "SoundSwitch");
            combo.SelectedIndex = 1;
            Assert.That(_superView.SoundSwitchPickers.Single().Selected.Name, Is.EqualTo("Stone"));
            // Sound off takes the picker away under its combo box, which must not pick nothing on its way out.
            var picker = _superView.SoundSwitchPickers.Single();
            _superView.SoundOn = false;
            Lay(panel);
            Assert.That((_superView.SoundSwitchPickers.Count, picker.Selected?.Name), Is.EqualTo((0, "Stone")));

            var changed = Descendants(panel).OfType<TextBlock>().Single(x => AutomationProperties.GetAutomationId(x) == "SoundMetaChangedOnDisk");
            Assert.That(changed.Visibility, Is.EqualTo(Visibility.Collapsed));
            _superView.SoundMetaChangedOnDisk = "The .snd.meta changed on disk. Your unsaved edits are kept.";
            Lay(panel);
            Assert.That((changed.Visibility, changed.Text), Is.EqualTo((Visibility.Visible, _superView.SoundMetaChangedOnDisk)));
        }

        static void Lay(FrameworkElement element)
        {
            element.Measure(new Size(1400, 900));
            element.Arrange(new Rect(0, 0, 1400, 900));
            element.UpdateLayout();
        }

        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var below in Descendants(child))
                    yield return below;
            }
        }

        // The in-memory bank, as SoundTriggerTests builds one: a tags table, events, Play actions, sounds and switch containers.
        // Every hashed name is kept so the pickers can be labelled.
        sealed class Bank
        {
            uint _next = 1_000_000;

            public Dictionary<uint, HircItem> Items { get; } = [];
            public Dictionary<string, AudioTagEvents> Rows { get; } = new(StringComparer.Ordinal);
            public Dictionary<uint, string> Names { get; } = [];

            readonly int _thread = Environment.CurrentManagedThreadId;

            /// <summary>When set, a lookup made off the thread that made the bank waits for it.</summary>
            public ManualResetEventSlim? Hold { get; set; }

            /// <summary>How many lookups off the thread that made the bank have reached Hold.</summary>
            public int Held => Volatile.Read(ref _held);
            int _held;

            /// <summary>When set, the next lookup made off the thread that made the bank throws it, once.</summary>
            public Exception? Throw { get => _throw; set => _throw = value; }
            Exception? _throw;

            /// <summary>The ids looked up off the thread that made the bank, in order.</summary>
            public ConcurrentQueue<uint> OffThread { get; } = new();

            public SoundTriggerResolver Resolver() => new(tag => Rows.GetValueOrDefault(tag), id =>
            {
                if (Environment.CurrentManagedThreadId != _thread)
                {
                    if (Hold is { } hold)
                    {
                        Interlocked.Increment(ref _held);
                        hold.Wait(TimeSpan.FromSeconds(30));
                    }
                    if (Interlocked.Exchange(ref _throw, null) is { } thrown)
                        throw thrown;
                    OffThread.Enqueue(id);
                }
                return Items.TryGetValue(id, out var item) ? [item] : [];
            }, _ => true);

            uint Hash(string name)
            {
                var id = WwiseHash.Compute(name);
                Names[id] = name;
                return id;
            }

            /// <summary>A tag whose battle event plays one sound.</summary>
            public void Tag(string tag, uint leaf) => Rows[tag] = new AudioTagEvents(Event(tag + "_Event", Sound(leaf)), "");

            /// <summary>An event with one Play action on the target. Returns the event's name.</summary>
            public string Event(string name, uint target)
            {
                var action = _next++;
                Items[action] = new CAkAction_V136 { Id = action, HircType = AkBkHircType.Action, ActionType = AkActionType.Play, IdExt = target };
                var id = Hash(name);
                Items[id] = new CAkEvent_V136 { Id = id, HircType = AkBkHircType.Event, Actions = [new CAkEvent_V136.Action_V136 { ActionId = action }] };
                return name;
            }

            public uint Sound(uint sourceId)
            {
                var id = _next++;
                Items[id] = new CAkSound_V136
                {
                    Id = id,
                    HircType = AkBkHircType.Sound,
                    AkBankSourceData = new AkBankSourceData_V136 { AkMediaInformation = new AkBankSourceData_V136.AkMediaInformation_V136 { SourceId = sourceId } },
                };
                return id;
            }

            public uint Switch(string group, string defaultCase, params (string Case, uint Child)[] cases)
            {
                var id = _next++;
                Items[id] = new CAkSwitchCntr_V136
                {
                    Id = id,
                    HircType = AkBkHircType.SwitchContainer,
                    GroupId = Hash(group),
                    DefaultSwitch = Hash(defaultCase),
                    SwitchList = cases
                        .Select(x => (ICAkSwitchCntr.ICAkSwitchPackage)new CAkSwitchCntr_V136.CAkSwitchPackage_V136 { SwitchId = Hash(x.Case), NodeIdList = [x.Child] })
                        .ToList(),
                };
                return id;
            }
        }
    }
}
