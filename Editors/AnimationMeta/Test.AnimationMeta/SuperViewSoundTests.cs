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
    // SuperView's Sound toggle on Throt's ATTACK_5. The game's audio is replaced by a small in-memory bank, and the four sound
    // delegates by recorders: no audio device opens and no repository loads.
    public class SuperViewSoundTests
    {
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

        [SetUp]
        public void Setup()
        {
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

        // The last load, or the one given, finishes with the bank.
        void Finish(int? load = null) => _loads[load ?? _loads.Count - 1].SetResult(_bank.Resolver());

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
            PlayTo(0.25f);

            Assert.That(_played, Is.EqualTo(new[] { ClipLeaf }));
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

            public SoundTriggerResolver Resolver() => new(tag => Rows.GetValueOrDefault(tag), id => Items.GetValueOrDefault(id), _ => true);

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
