using System.IO;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView;
using Editors.Shared.Core.Common.BaseControl;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.Commands;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimationMeta
{
    // SuperView's third tab, the slot's .snd.meta, on Throt's ATTACK_5: loaded from the slot's Sound, its tags on the timeline and
    // drawn in the scene, and saved as the clip tab is. The persistent row's .snd.meta is loaded for sound but gets no tab.
    public class SuperViewSoundTabTests
    {
        const string SndMeta = "animations/derpy/throt_attack_5.snd.meta";

        AssetEditorTestRunner _runner = null!;
        SuperViewViewModel _superView = null!;

        [SetUp]
        public void Setup()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            _superView = (SuperViewViewModel)_runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.SuperView_Editor);
            _superView.Load(new AnimationToolInput()
            {
                Mesh = _runner.PackFileService.FindFile(@"variantmeshes\variantmeshdefinitions\skv_throt.variantmeshdefinition")!,
                FragmentName = @"animations/database/battle/bin/hu17_dlc16_throt.bin",
                AnimationSlot = DefaultAnimationSlotTypeHelper.GetfromValue("ATTACK_5")
            });
            Assert.That(_superView.SlotGrid.SelectedRow?.Slot.SlotName, Is.EqualTo("ATTACK_5"), "Throt ATTACK_5 did not load");
        }

        MetaDataFileParser Parser => _runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>();

        // SOUND_TRIGGER v11 tags at the times given.
        byte[] SndMetaBytes(params float[] times)
        {
            var file = new ParsedMetadataFile { Version = 2 };
            foreach (var at in times)
            {
                var tag = (SoundTrigger_v11)Parser.CreateDefault("SOUND_TRIGGER_11");
                (tag.StartTime, tag.EndTime, tag.SoundEvent) = (at, at, "Derpy_Test_Swing");
                file.Attributes.Add(tag);
            }
            return Parser.GenerateBytes(2, file);
        }

        // Two SOUND_TRIGGER v11 tags, at 0.2 s and 0.6 s, in the editable pack.
        string WriteSndMeta(string path) => WriteFile(path, SndMetaBytes(0.2f, 0.6f));

        string WriteFile(string path, byte[] bytes)
        {
            var pfs = _runner.PackFileService;
            pfs.AddFilesToPack(pfs.GetEditablePack()!, [new NewPackFileEntry(Path.GetDirectoryName(path)!, PackFile.CreateFromBytes(Path.GetFileName(path), bytes))]);
            return path;
        }

        static string Key(string? path) => path?.Replace('\\', '/').ToLowerInvariant() ?? "";
        string PathOf(PackFile? file) => file == null ? "" : Key(_runner.PackFileService.GetFullPath(file));

        void AnswerBrowse(string path) => _runner.Dialogs.Setup(x => x.DisplayBrowseDialog(It.IsAny<List<string>>(), It.IsAny<Func<string, bool>?>()))
            .Returns(new BrowseDialogResultFile(true, _runner.PackFileService.FindFile(path)!));

        [Test]
        public void SlotSound_LoadsTheSndMetaIntoTheThirdTab()
        {
            _superView.SlotSound = WriteSndMeta(SndMeta);

            Assert.That(_superView.SoundMetaEditor.ParsedFile!.Attributes, Has.Count.EqualTo(2));
            Assert.That(_superView.SoundMetaSource, Does.Contain(SndMeta));
            Assert.That(_superView.SoundFileProblems.Where(x => x.StartsWith("Slot ")), Is.Empty);
        }

        [Test]
        public void SndMetaTags_AreTimelineTicks_OnTheSoundTab()
        {
            _superView.SlotSound = WriteSndMeta(SndMeta);
            var ticks = _superView.TimelineTicks.Where(x => x.Tab == MetaTab.Sound).ToList();
            Assert.That(ticks.Select(x => x.Tag), Is.EqualTo(_superView.SoundMetaEditor.ParsedFile!.Attributes));
            Assert.That(ticks.Select(x => x.Tooltip), Is.All.EndWith(" It's in the slot's .snd.meta."));
            _superView.SelectedTabControllerIndex = 1;

            _superView.SelectTickCommand.Execute(ticks[1]);

            Assert.That(_superView.SelectedTabControllerIndex, Is.EqualTo(2));
            Assert.That(_superView.SoundMetaEditor.SelectedTag?._input, Is.SameAs(ticks[1].Tag));
            Assert.That(_superView.TimelineTicks.Single(x => x.IsSelected).Tag, Is.SameAs(ticks[1].Tag), "its tick is highlighted");
        }

        [Test]
        public void SndMetaTags_DrawMarkers()
        {
            var data = _superView.SceneObjects[0].Data;
            var before = data.MetaDataItems.Count;

            _superView.SlotSound = WriteSndMeta(SndMeta);

            Assert.That(data.MetaDataItems, Has.Count.EqualTo(before + 2));
        }

        // The persistent row's Sound, set in the slot grid and saved with the set, as only the saved set's persistent row is read.
        [Test]
        public void PersistentRowSndMeta_IsLoaded_ButHasNoTab()
        {
            var path = WriteSndMeta("animations/derpy/throt_persistent.snd.meta");
            _superView.SlotGrid.Rows.First(x => x.Slot.SlotName == "PERSISTENT_METADATA_ALIVE").Sound = path;
            Assert.That(_superView.Save(), Is.True);

            Assert.That(PathOf(_superView.SceneObjects[0].Data.PersistSoundMetaData), Is.EqualTo(path));
            Assert.That(_superView.SlotGrid.SelectedRow?.Slot.SlotName, Is.EqualTo("ATTACK_5"), "the clip played is still ATTACK_5");
            Assert.That(_superView.SoundMetaEditor.ParsedFile, Is.Null);
            Assert.That(_superView.TimelineTicks.Where(x => x.Tab == MetaTab.Sound), Is.Empty);
        }

        [Test]
        public void SlotSound_InNoLoadedPack_LeavesTheTabEmpty()
        {
            _superView.SlotSound = WriteSndMeta(SndMeta);
            const string Missing = "animations/derpy/nowhere.snd.meta";

            Assert.DoesNotThrow(() => _superView.SlotSound = Missing);

            Assert.That(_superView.SoundMetaEditor.ParsedFile, Is.Null);
            Assert.That(_superView.TimelineTicks.Where(x => x.Tab == MetaTab.Sound), Is.Empty);
            Assert.That(_superView.SoundFileProblems, Does.Contain($"Slot .snd.meta {Missing} is in no loaded pack."));
            Assert.That(_superView.MetaEditor.ParsedFile, Is.Not.Null, "the clip still plays with its meta");
        }

        [Test]
        public void BrowseSoundTab_WithASlotPicked_SetsTheSlotsSound()
        {
            AnswerBrowse(WriteSndMeta(SndMeta));

            _superView.BrowseSoundTabCommand.Execute(null);

            Assert.That((_superView.SlotSound, _superView.SlotGrid.SelectedRow!.Sound), Is.EqualTo((SndMeta, SndMeta)));
            Assert.That(_superView.SoundMetaEditor.ParsedFile!.Attributes, Has.Count.EqualTo(2));
        }

        // "Any animation" plays a clip with no slot, so the tab opens a .snd.meta picked by hand. It holds until another clip plays.
        [Test]
        public void AnyAnimation_SndMetaPickedByHand_LoadsIntoTheTab()
        {
            var selection = _superView.SceneObjects[0].FragAndSlotSelection;
            var clip = Key(selection.FragmentSlotList.SelectedItem!.AnimationFile);
            selection.FragmentSlotList.SelectedItem = null;
            Assert.That(_superView.SlotGrid.SelectedRow, Is.Null);
            selection.PlayAnimation(selection.AnimationList.PossibleValues.First(x => Key(x.AnimationFile) == clip));
            AnswerBrowse(WriteSndMeta(SndMeta));

            _superView.BrowseSoundTabCommand.Execute(null);

            Assert.That(_superView.SoundMetaEditor.ParsedFile!.Attributes, Has.Count.EqualTo(2));
            Assert.That(_superView.TimelineTicks.Count(x => x.Tab == MetaTab.Sound), Is.EqualTo(2));
            Assert.That(_superView.SlotGrid.HasUnsavedChanges, Is.False, "no slot was edited");

            selection.PlayAnimation(selection.AnimationList.PossibleValues.First(x => Key(x.AnimationFile) != clip));
            Assert.That(_superView.SoundMetaEditor.ParsedFile, Is.Null, "another clip");
        }

        // Final review: "Any animation" leaves the slot picked in the grid. What plays decides: the pick opens in the tab, the slot keeps
        // its Sound, and the clip the user picked goes on playing.
        [Test]
        public void AnyAnimation_WithTheSlotStillPicked_OpensThePickInTheTab_AndEditsNoSlot()
        {
            var selection = _superView.SceneObjects[0].FragAndSlotSelection;
            var row = _superView.SlotGrid.SelectedRow!;
            var (slotClip, sound) = (Key(row.Animation), row.Sound);
            var clip = selection.AnimationList.PossibleValues.First(x => Key(x.AnimationFile) != slotClip);
            selection.AnimationList.SelectedItem = clip;
            Assert.That(_superView.SlotGrid.SelectedRow, Is.SameAs(row), "the slot stays picked");
            AnswerBrowse(WriteSndMeta(SndMeta));

            _superView.BrowseSoundTabCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(_superView.SoundMetaEditor.ParsedFile?.Attributes, Has.Count.EqualTo(2), "the pick is in the tab");
                Assert.That((row.Sound, _superView.SlotGrid.HasUnsavedChanges), Is.EqualTo((sound, false)), "no slot was edited");
                Assert.That(Key(_superView.SceneObjects[0].Data.AnimationName.Value), Is.EqualTo(Key(clip.AnimationFile)), "the picked clip still plays");
            });
        }

        const string ChangedOnDisk = "The .snd.meta changed on disk. Your unsaved edits are kept.";

        static AttributeViewModel Field(MetaDataEntry row, string name) => row.Variables.Single(x => x.FieldName == name);
        float StartOf(int tag) => ((SoundTrigger_v11)_superView.SoundMetaEditor.ParsedFile!.Attributes[tag]).StartTime;
        void EditFirstStart(float to) => Field(_superView.SoundMetaEditor.Tags[0], "Start Time").ValueAsString = to.ToString();

        // The save dialog answers with the path given, and the extensions it was offered are kept.
        List<List<string>> AnswerSave(string path)
        {
            var offered = new List<List<string>>();
            _runner.Dialogs.Setup(x => x.DisplaySaveDialog(It.IsAny<IPackFileService>(), It.IsAny<List<string>>(), It.IsAny<string>()))
                .Callback<IPackFileService, List<string>, string>((_, extensions, _) => offered.Add(extensions))
                .Returns(new SaveDialogResult(true, null, path));
            return offered;
        }

        List<string> Said()
        {
            var said = new List<string>();
            _runner.Dialogs.Setup(x => x.ShowDialogBox(It.IsAny<string>(), It.IsAny<string>())).Callback<string, string>((m, _) => said.Add(m));
            return said;
        }

        void SaveFromOutside(string path, byte[] bytes) => _runner.ServiceProvider.GetRequiredService<IFileSaveService>().Save(path, bytes, false);

        [Test]
        public void Save_WritesAnEditedSndMeta()
        {
            _superView.SlotSound = WriteSndMeta(SndMeta);
            EditFirstStart(0.35f);

            Assert.That(_superView.Save(), Is.True);

            var saved = Parser.ParseFile(_runner.PackFileService.FindFile(SndMeta)!.DataSource.ReadData());
            Assert.That(((SoundTrigger_v11)saved.Attributes[0]).StartTime, Is.EqualTo(0.35f));
            Assert.That(_superView.HasUnsavedChanges, Is.False);
        }

        // Another mod's .snd.meta: Save asks first, and "Save as custom meta" is Save meta as on the Sound tab.
        [Test]
        public void Save_OfAnotherModsSndMeta_AsACustomCopy_PointsTheSoundColumn()
        {
            var pfs = _runner.PackFileService;
            const string Theirs = "animations/derpy/theirs.snd.meta";
            pfs.AddFilesToPack(_runner.CreateEmptyPackFile("OtherMod", false), [new NewPackFileEntry(@"animations\derpy", PackFile.CreateFromBytes("theirs.snd.meta", SndMetaBytes(0.2f, 0.6f)))]);
            _superView.SlotSound = Theirs;
            EditFirstStart(0.35f);
            _runner.Dialogs.Setup(x => x.ShowChoiceBox(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>())).Returns(0);
            AnswerSave("animations/derpy/custom_theirs");

            Assert.That(_superView.Save(), Is.True);

            Assert.That(_superView.SlotGrid.SelectedRow!.Sound, Is.EqualTo("animations/derpy/custom_theirs.snd.meta"));
            Assert.That(pfs.FindFile(Theirs, pfs.GetEditablePack()!), Is.Null, "their file is not overridden");
        }

        [Test]
        public void SaveMetaAs_OnTheSoundTab_PointsTheSoundColumn()
        {
            _superView.SlotSound = WriteSndMeta(SndMeta);
            var meta = _superView.SlotGrid.SelectedRow!.Meta;
            EditFirstStart(0.35f);
            _superView.SelectedTabControllerIndex = (int)MetaTab.Sound;
            const string Custom = "animations/derpy/custom_x.snd.meta";
            AnswerSave(Custom);
            var said = Said();

            _superView.SaveMetaAsCommand.Execute(null);

            Assert.That(said, Is.Empty);
            var file = _runner.PackFileService.FindFile(Custom);
            Assert.That(file, Is.Not.Null);
            Assert.That(((SoundTrigger_v11)Parser.ParseFile(file!.DataSource.ReadData()).Attributes[0]).StartTime, Is.EqualTo(0.35f), "the copy has the edit");
            Assert.That((_superView.SlotGrid.SelectedRow!.Sound, _superView.SlotGrid.SelectedRow.Meta), Is.EqualTo((Custom, meta)));
            Assert.That((PathOf(_superView.SoundMetaEditor.CurrentFile), _superView.SoundMetaEditor.HasUnsavedChanges), Is.EqualTo((Custom, false)), "the tab shows the copy");
        }

        [Test]
        public void SaveMetaAs_AppendsSndMeta_NotAnmMeta()
        {
            _superView.SlotSound = WriteSndMeta(SndMeta);
            _superView.SelectedTabControllerIndex = (int)MetaTab.Sound;
            var offered = AnswerSave(@"Animations\Derpy\Custom_X");

            _superView.SaveMetaAsCommand.Execute(null);

            Assert.That(offered, Is.EqualTo(new[] { new List<string> { ".snd.meta" } }));
            Assert.That(_runner.PackFileService.FindFile("animations/derpy/custom_x.snd.meta"), Is.Not.Null);
            Assert.That(_superView.SlotGrid.SelectedRow!.Sound, Is.EqualTo("animations/derpy/custom_x.snd.meta"));
        }

        // Save meta as on a tab already showing a custom copy offers that copy's own path. Writing it is the tab's own save, not a
        // change on disk.
        [Test]
        public void SaveMetaAs_OverTheTabsOwnFile_IsNotAChangeOnDisk()
        {
            _superView.SlotSound = WriteSndMeta(SndMeta);
            Assert.That(_superView.Save(), Is.True);
            EditFirstStart(0.35f);
            _superView.SelectedTabControllerIndex = (int)MetaTab.Sound;
            AnswerSave(SndMeta);

            _superView.SaveMetaAsCommand.Execute(null);

            Assert.That(((SoundTrigger_v11)Parser.ParseFile(_runner.PackFileService.FindFile(SndMeta)!.DataSource.ReadData()).Attributes[0]).StartTime, Is.EqualTo(0.35f));
            Assert.That((_superView.SoundMetaChangedOnDisk, StartOf(0), _superView.SoundMetaEditor.HasUnsavedChanges), Is.EqualTo(("", 0.35f, false)));
        }

        // A refused save takes the row's edit back. The tab keeps its edits: the row played its new, unwritten file for a moment.
        [Test]
        public void SaveMetaAs_OnTheSoundTab_Refused_KeepsTheEdits()
        {
            _superView.SlotSound = WriteSndMeta(SndMeta);
            Assert.That(_superView.Save(), Is.True);
            EditFirstStart(0.35f);
            _superView.SelectedTabControllerIndex = (int)MetaTab.Sound;
            AnswerSave("animations/derpy/custom_x.snd.meta");
            var said = Said();
            _runner.PackFileService.SetEditablePack(null);

            _superView.SaveMetaAsCommand.Execute(null);

            Assert.That(said, Is.EqualTo(new[] { "No editable pack is selected." }));
            Assert.That((StartOf(0), _superView.SoundMetaEditor.HasUnsavedChanges, _superView.SlotGrid.SelectedRow!.Sound), Is.EqualTo((0.35f, true, SndMeta)));
        }

        // "Any animation" with a .snd.meta picked by hand has no slot to point at a copy, so Save meta as refuses, as the other tabs do.
        [Test]
        public void SaveMetaAs_OnTheSoundTab_WithNoSlot_Refuses()
        {
            var selection = _superView.SceneObjects[0].FragAndSlotSelection;
            var clip = Key(selection.FragmentSlotList.SelectedItem!.AnimationFile);
            selection.FragmentSlotList.SelectedItem = null;
            selection.PlayAnimation(selection.AnimationList.PossibleValues.First(x => Key(x.AnimationFile) == clip));
            AnswerBrowse(WriteSndMeta(SndMeta));
            _superView.BrowseSoundTabCommand.Execute(null);
            _superView.SelectedTabControllerIndex = (int)MetaTab.Sound;
            var offered = AnswerSave("animations/derpy/custom_x.snd.meta");
            var said = Said();

            _superView.SaveMetaAsCommand.Execute(null);

            Assert.That(said, Is.EqualTo(new[] { "No slot is picked to point at a copy of the Sound tab's .snd.meta. Pick an animation set and a slot first." }));
            Assert.That((offered, _runner.PackFileService.FindFile("animations/derpy/custom_x.snd.meta")), Is.EqualTo((new List<List<string>>(), (PackFile?)null)));
        }

        [Test]
        public void ExternalSave_ReloadsACleanTab()
        {
            _superView.SlotSound = WriteSndMeta(SndMeta);

            SaveFromOutside(SndMeta, SndMetaBytes(0.1f, 0.4f, 0.8f));

            Assert.That(_superView.SoundMetaEditor.ParsedFile!.Attributes, Has.Count.EqualTo(3));
            Assert.That(_superView.TimelineTicks.Count(x => x.Tab == MetaTab.Sound), Is.EqualTo(3), "the scene was rebuilt");
            Assert.That(_superView.SoundMetaChangedOnDisk, Is.Empty);
        }

        [Test]
        public void ExternalSave_KeepsUnsavedEdits()
        {
            _superView.SlotSound = WriteSndMeta(SndMeta);
            EditFirstStart(0.35f);

            SaveFromOutside(SndMeta, SndMetaBytes(0.1f, 0.4f, 0.8f));

            Assert.That((_superView.SoundMetaEditor.ParsedFile!.Attributes.Count, StartOf(0), _superView.SoundMetaEditor.HasUnsavedChanges), Is.EqualTo((2, 0.35f, true)));
            Assert.That(_superView.SoundMetaChangedOnDisk, Is.EqualTo(ChangedOnDisk));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ChangedOnDisk_ClearsOnTheNextSaveOrLoad(bool save)
        {
            // The set saved first, so the save below is the tab's alone and does not pick the set again, which would load the tab.
            _superView.SlotSound = WriteSndMeta(SndMeta);
            Assert.That(_superView.Save(), Is.True);
            EditFirstStart(0.35f);
            SaveFromOutside(SndMeta, SndMetaBytes(0.1f, 0.4f, 0.8f));
            Assert.That(_superView.SoundMetaChangedOnDisk, Is.EqualTo(ChangedOnDisk));

            if (save)
                Assert.That(_superView.Save(), Is.True);
            else
                _superView.SlotSound = WriteSndMeta("animations/derpy/another.snd.meta");

            Assert.That(_superView.SoundMetaChangedOnDisk, Is.Empty);
        }

        // A .snd.meta that does not parse is left out: the tab stays empty, the problem is named, and the clip and its meta play on.
        [TestCase(true)]
        [TestCase(false)]
        public void UnreadableSndMeta_IsNamed_AndSuperViewPlaysOn(bool slot)
        {
            var data = _superView.SceneObjects[0].Data;
            var markers = data.MetaDataItems.Count;
            const string Bad = "animations/derpy/bad.snd.meta";
            WriteFile(Bad, [5, 0, 0, 0, 0, 0, 0, 0]);
            if (slot)
            {
                _superView.SlotSound = WriteSndMeta(SndMeta);
                Assert.DoesNotThrow(() => _superView.SlotSound = Bad);
            }
            else
            {
                _superView.SlotGrid.Rows.First(x => x.Slot.SlotName == "PERSISTENT_METADATA_ALIVE").Sound = Bad;
                Assert.That(_superView.Save(), Is.True);
            }

            Assert.That(_superView.SoundFileProblems, Does.Contain($"{(slot ? "Slot" : "Persistent")} .snd.meta {Bad} could not be read: Unknown version - 5"));
            Assert.That(_superView.SoundMetaEditor.ParsedFile, Is.Null);
            Assert.That(_superView.MetaEditor.ParsedFile, Is.Not.Null, "the clip meta still loaded");
            Assert.That(data.MetaDataItems, Has.Count.EqualTo(markers), "the scene was rebuilt without the sound tab's markers");
        }

        // A new mesh clears the metas it played with, the .snd.metas among them.
        [Test]
        public void MeshSwap_ClearsTheSndMetas()
        {
            _superView.SlotGrid.Rows.First(x => x.Slot.SlotName == "PERSISTENT_METADATA_ALIVE").Sound = WriteSndMeta("animations/derpy/throt_persistent.snd.meta");
            _superView.SlotSound = WriteSndMeta(SndMeta);
            Assert.That(_superView.Save(), Is.True);
            var data = _superView.SceneObjects[0].Data;
            Assert.That(data.SoundMetaData, Is.Not.Null);
            Assert.That(data.PersistSoundMetaData, Is.Not.Null);

            _superView.LoadFile(_runner.PackFileService.FindFile(@"variantmeshes\variantmeshdefinitions\skv_throt.variantmeshdefinition")!);

            Assert.That((data.SoundMetaData, data.PersistSoundMetaData, _superView.SoundMetaEditor.ParsedFile), Is.EqualTo(((PackFile?)null, (PackFile?)null, (ParsedMetadataFile?)null)));
        }
    }
}
