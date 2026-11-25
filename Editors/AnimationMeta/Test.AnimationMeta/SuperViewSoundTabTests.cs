using Editors.AnimationMeta.SuperView.Editing;
using Editors.AnimationMeta.SuperView.Timeline;
using System.IO;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView;
using Editors.Shared.Core.Common;
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

        // The scrub bar has a lane for each META file: the .snd.meta's tags sit under the persistent and the animation lanes.
        [Test]
        public void SndMetaTags_AreScrubBarMarkers_InALaneOfTheirOwn()
        {
            _superView.SlotSound = WriteSndMeta(SndMeta);
            var markers = Markers(MetaDataDocumentOwner.Sound);
            Assert.That(markers.Select(x => x.Item.Source), Is.EqualTo(_superView.SoundMetaEditor.ParsedFile!.Attributes));
            Assert.That(markers.Select(x => x.Lane), Is.All.EqualTo(2));
            _superView.SelectedTabControllerIndex = 1;

            _superView.Player.TimelineItemClickCommand.Execute(markers[1]);

            Assert.That(_superView.SelectedTabControllerIndex, Is.EqualTo(2));
            Assert.That(_superView.SoundMetaEditor.SelectedTag?._input, Is.SameAs(markers[1].Item.Source));
            Assert.That(Highlighted, Is.SameAs(markers[1].Item.Source), "its marker is highlighted");
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
            Assert.That(Markers(MetaDataDocumentOwner.Sound), Is.Empty);
            Assert.That(_superView.SoundFileProblems.Where(x => x.StartsWith("Persistent ")), Is.Empty, "a loaded one is no problem");
        }

        // Deferred item D13: the persistent row's .snd.meta, named by its row and in no loaded pack, has a line of its own.
        [Test]
        public void PersistentRowSndMeta_InNoLoadedPack_IsNamed()
        {
            const string Missing = "animations/derpy/nowhere_persistent.snd.meta";
            _superView.SlotGrid.Rows.First(x => x.Slot.SlotName == "PERSISTENT_METADATA_ALIVE").Sound = Missing;
            Assert.That(_superView.Save(), Is.True);

            Assert.That(_superView.SoundFileProblems, Does.Contain($"Persistent .snd.meta {Missing} is in no loaded pack."));
            Assert.That(_superView.SceneObjects[0].Data.PersistSoundMetaData, Is.Null);
        }

        [Test]
        public void SlotSound_InNoLoadedPack_LeavesTheTabEmpty()
        {
            _superView.SlotSound = WriteSndMeta(SndMeta);
            const string Missing = "animations/derpy/nowhere.snd.meta";

            Assert.DoesNotThrow(() => _superView.SlotSound = Missing);

            Assert.That(_superView.SoundMetaEditor.ParsedFile, Is.Null);
            Assert.That(Markers(MetaDataDocumentOwner.Sound), Is.Empty);
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
            Assert.That(Markers(MetaDataDocumentOwner.Sound), Has.Count.EqualTo(2));
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
            Assert.That(Markers(MetaDataDocumentOwner.Sound), Has.Count.EqualTo(3), "the scene was rebuilt");
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

        // Deferred item D20: a second way a .snd.meta can fail to read, an empty file, which is too short for its version number.
        [Test]
        public void EmptySndMeta_IsNamed_AndSuperViewPlaysOn()
        {
            const string Empty = "animations/derpy/empty.snd.meta";
            WriteFile(Empty, []);

            Assert.DoesNotThrow(() => _superView.SlotSound = Empty);

            Assert.That(_superView.SoundFileProblems.Where(x => x.StartsWith($"Slot .snd.meta {Empty} could not be read: ")), Has.Exactly(1).Items);
            Assert.That((_superView.SoundMetaEditor.ParsedFile, _superView.MetaEditor.ParsedFile != null), Is.EqualTo(((ParsedMetadataFile?)null, true)));
        }

        // Deferred item D18: a .snd.meta that could not be read, then fixed from outside SuperView, is read again at once, as any change
        // on disk to the Sound tab's file is.
        [Test]
        public void UnreadableSndMeta_FixedOnDisk_IsReadAgain()
        {
            const string Bad = "animations/derpy/bad.snd.meta";
            WriteFile(Bad, [5, 0, 0, 0, 0, 0, 0, 0]);
            _superView.SlotSound = Bad;
            Assert.That(_superView.SoundFileProblems, Has.Some.StartsWith($"Slot .snd.meta {Bad} could not be read"));

            SaveFromOutside(Bad, SndMetaBytes(0.2f, 0.6f, 0.9f));

            Assert.That(_superView.SoundMetaEditor.ParsedFile?.Attributes, Has.Count.EqualTo(3));
            Assert.That(_superView.SoundFileProblems, Has.None.StartsWith($"Slot .snd.meta {Bad}"));
        }

        // Deferred item D15, as the review settled it: the Sound tab saves a .snd.meta. A name typed with another meta's ending has it
        // replaced, and a name with no ending gets one, where the Animation tab keeps any name ending in .meta.
        [TestCase("animations/derpy/custom_x.anm.meta")]
        [TestCase("animations/derpy/custom_x.meta")]
        [TestCase("animations/derpy/custom_x")]
        [TestCase("animations/derpy/custom_x.snd.meta")]
        public void SaveMetaAs_OnTheSoundTab_EndsTheNameInSndMeta(string typed)
        {
            const string Saved = "animations/derpy/custom_x.snd.meta";
            _superView.SlotSound = WriteSndMeta(SndMeta);
            _superView.SelectedTabControllerIndex = (int)MetaTab.Sound;
            AnswerSave(typed);
            var said = Said();

            _superView.SaveMetaAsCommand.Execute(null);

            Assert.That((said.Count, _superView.SlotGrid.SelectedRow!.Sound, _runner.PackFileService.FindFile(Saved) != null), Is.EqualTo((0, Saved, true)));
            if (typed != Saved)
                Assert.That(_runner.PackFileService.FindFile(typed), Is.Null, "nothing is written under the name typed");
        }

        // Review fix, minor 4: a tag the tab shows that cannot be written, here one with no event name, does not stop a change on disk
        // from loading. Before, comparing the file with what the tab shows threw.
        [Test]
        public void ExternalSave_OverATagThatCannotBeWritten_ReloadsACleanTab()
        {
            _superView.SlotSound = WriteSndMeta(SndMeta);
            ((SoundTrigger_v11)_superView.SoundMetaEditor.ParsedFile!.Attributes[0]).SoundEvent = null!;

            Assert.DoesNotThrow(() => SaveFromOutside(SndMeta, SndMetaBytes(0.1f, 0.4f, 0.8f)));

            Assert.That(_superView.SoundMetaEditor.ParsedFile!.Attributes, Has.Count.EqualTo(3));
        }

        // Review fix, minor 5: a pack loaded later with the same bytes at the tab's path is now the file the tab reads, so the tab
        // loads it and names where it is from, though what it shows does not change.
        [Test]
        public void AnotherPacksCopy_WithTheSameBytes_BecomesTheTabsFile()
        {
            _superView.SlotSound = WriteSndMeta(SndMeta);
            Assert.That(_superView.SoundMetaSource, Is.EqualTo($"From your pack: {SndMeta}"));

            _runner.PackFileService.AddFilesToPack(_runner.CreateEmptyPackFile("OtherMod", false),
                [new NewPackFileEntry(@"animations\derpy", PackFile.CreateFromBytes(Path.GetFileName(SndMeta), SndMetaBytes(0.2f, 0.6f)))]);

            Assert.That(_superView.SoundMetaSource, Is.EqualTo($"From OtherMod: {SndMeta}"));
        }

        // Deferred item D16: Save meta as never writes over another pack's file, which would override it for every set naming it. It
        // refuses as New meta does, on every tab, and nothing is written or pointed at.
        [TestCase(MetaTab.Sound, "animations/derpy/theirs.snd.meta")]
        [TestCase(MetaTab.Clip, "animations/derpy/theirs.anm.meta")]
        public void SaveMetaAs_RefusesAPathAnotherPackHas(MetaTab tab, string theirs)
        {
            var pfs = _runner.PackFileService;
            var editable = pfs.GetEditablePack()!;
            _superView.SlotSound = WriteSndMeta(SndMeta);
            pfs.AddFilesToPack(_runner.CreateEmptyPackFile("OtherMod", false), [new NewPackFileEntry(@"animations\derpy", PackFile.CreateFromBytes(Path.GetFileName(theirs), SndMetaBytes(0.5f)))]);
            Assert.That(pfs.GetEditablePack(), Is.SameAs(editable));
            var row = _superView.SlotGrid.SelectedRow!;
            var before = (row.Meta, row.Sound);
            _superView.SelectedTabControllerIndex = (int)tab;
            AnswerSave(theirs);
            var said = Said();

            _superView.SaveMetaAsCommand.Execute(null);

            Assert.That(said, Is.EqualTo(new[] { $"{theirs} is already in another pack. To use it, type it into the slot's {(tab == MetaTab.Sound ? "Sound" : "Meta")} field." }));
            Assert.That(((row.Meta, row.Sound), pfs.FindFile(theirs, editable)), Is.EqualTo((before, (PackFile?)null)));
        }

        // Deferred item D17: Save on a Sound tab with no edits writes the file again, which is the tab's own save and nothing new on
        // disk, so the tab keeps what it shows and its undo history.
        [Test]
        public void ACleanSave_OnTheSoundTab_KeepsTheUndoHistory()
        {
            _superView.SlotSound = WriteSndMeta(SndMeta);
            Assert.That(_superView.Save(), Is.True);
            EditFirstStart(0.35f);
            Assert.That(_superView.Save(), Is.True);
            var editor = _superView.SoundMetaEditor;
            var shown = editor.ParsedFile;
            Assert.That((editor.HasUnsavedChanges, editor.UndoCommand.CanExecute(null)), Is.EqualTo((false, true)), "saved, and the edit can be undone");

            editor.SaveActionCommand.Execute(null);

            Assert.That((editor.ParsedFile, editor.UndoCommand.CanExecute(null), StartOf(0)), Is.EqualTo((shown, true, 0.35f)));
        }

        // Deferred item D20: Save meta as over the tab's own custom copy loads nothing new, so the copy's tab is marked saved by hand,
        // on the Persistent and Animation tabs as on the Sound tab.
        [TestCase(MetaTab.Persistent)]
        [TestCase(MetaTab.Clip)]
        public void SaveMetaAs_OverTheTabsOwnCopy_MarksTheTabSaved(MetaTab tab)
        {
            _superView.SelectedTabControllerIndex = (int)tab;
            var editor = tab == MetaTab.Persistent ? _superView.PersistentMetaEditor : _superView.MetaEditor;
            const string Copy = "animations/derpy/custom_copy.anm.meta";
            AnswerSave(Copy);
            var said = Said();
            _superView.SaveMetaAsCommand.Execute(null);
            Assert.That(PathOf(editor.CurrentFile), Is.EqualTo(Copy), "the tab shows the copy");
            var field = editor.Tags.SelectMany(x => x.Variables).First(x => !x.IsReadOnly && x.Scrub == Shared.Ui.Common.Behaviors.ScrubMode.Integer);
            field.ValueAsString = (int.Parse(field.ValueAsString) + 1).ToString();
            var copy = editor.CurrentFile;
            Assert.That(editor.HasUnsavedChanges, Is.True);

            _superView.SaveMetaAsCommand.Execute(null);

            Assert.That(said, Is.Empty);
            Assert.That((editor.CurrentFile, editor.HasUnsavedChanges), Is.EqualTo((copy, false)));
        }

        // Deferred item D12: the scene highlights the shown tab's picked tag, which is also the one the gizmo moves, and the scrub bar
        // highlights its marker. A switch between two tabs with the same pick (none, here) changes nothing in the scene, so the scene
        // is not built again.
        [Test]
        public void TheShownTab_DecidesWhichTagIsHighlighted_AndASwitchWithTheSamePickLeavesTheSceneAlone()
        {
            _superView.SlotSound = WriteSndMeta(SndMeta);
            var data = _superView.SceneObjects[0].Data;
            _superView.SelectedTabControllerIndex = (int)MetaTab.Clip;
            _superView.MetaEditor.SelectedTag = null;
            _superView.PersistentMetaEditor.SelectedTag = null;
            _superView.SoundMetaEditor.SelectedTag = _superView.SoundMetaEditor.Tags[1];

            var onClip = RedSoundMarkers(data);
            _superView.SelectedTabControllerIndex = (int)MetaTab.Sound;
            var onSound = RedSoundMarkers(data);
            var selectedOnSound = Highlighted;
            _superView.SelectedTabControllerIndex = (int)MetaTab.Clip;
            var backOnClip = RedSoundMarkers(data);
            var scene = data.MetaDataItems;
            _superView.SelectedTabControllerIndex = (int)MetaTab.Persistent;
            var sameScene = data.MetaDataItems == scene;
            var persistentTag = _superView.PersistentMetaEditor.Tags.First(x => Markers(MetaDataDocumentOwner.Persistent).Any(m => m.Item.Source == x._input));
            _superView.PersistentMetaEditor.SelectedTag = persistentTag;

            Assert.Multiple(() =>
            {
                Assert.That((onClip, onSound, backOnClip), Is.EqualTo((0, 1, 0)), "the Sound tab's pick is red on the Sound tab only");
                Assert.That(selectedOnSound, Is.SameAs(_superView.SoundMetaEditor.Tags[1]._input));
                Assert.That(sameScene, Is.True, "Animation to Persistent, neither with a pick, did not build the scene again");
                Assert.That(Highlighted, Is.SameAs(persistentTag._input), "the scrub bar highlights the persistent tab's pick");
            });
        }

        List<MetaDataTimelineItem> Markers(MetaDataDocumentOwner owner) =>
            _superView.Player.TimelineItems.OfType<MetaDataTimelineItem>().Where(x => x.Item.Owner == owner).ToList();

        // The tag whose marker the scrub bar highlights.
        ParsedMetadataAttribute? Highlighted => (_superView.Player.SelectedTimelineItem as MetaDataTimelineItem)?.Item.Source;

        // The Sound: markers drawn red, the picked tag's colour.
        static int RedSoundMarkers(SceneObject data) => GameWorld.Core.SceneNodes.SceneNodeHelper.GetChildrenOfType<GameWorld.Core.SceneNodes.SimpleDrawableNode>(data.MainNode)
            .Where(x => x.Name.StartsWith("Sound:"))
            .Count(x => ((List<GameWorld.Core.Rendering.RenderItems.WorldTextRenderItem>)typeof(GameWorld.Core.SceneNodes.SimpleDrawableNode)
                .GetField("_renderList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(x)!)
                .Any(label => label.Colour == Microsoft.Xna.Framework.Color.Red));

        // Deferred item D12: a click on a marker in another tab switches the tab and picks the tag, and builds the scene once for both.
        // The scene build is counted by the VFX filter list it sets every time.
        [Test]
        public void ClickingAMarker_InAnotherTab_BuildsTheSceneOnce()
        {
            _superView.SlotSound = WriteSndMeta(SndMeta);
            _superView.SelectedTabControllerIndex = (int)MetaTab.Clip;
            var marker = Markers(MetaDataDocumentOwner.Sound)[0];
            var builds = 0;
            _superView.PropertyChanged += (_, e) => builds += e.PropertyName == nameof(SuperViewViewModel.VfxFilterIds) ? 1 : 0;

            _superView.Player.TimelineItemClickCommand.Execute(marker);

            Assert.That((_superView.SelectedTabControllerIndex, builds), Is.EqualTo(((int)MetaTab.Sound, 1)));
            Assert.That(RedSoundMarkers(_superView.SceneObjects[0].Data), Is.EqualTo(1), "and the scene it built shows the pick");
        }

        // Deferred item D12: the gizmo is on the shown tab's picked tag, so a SOUND_TRIGGER in the .snd.meta can be moved on the Sound
        // tab, and nothing is under the gizmo on a tab with no pick.
        [Test]
        public void TheGizmo_FollowsTheShownTabsPick()
        {
            _superView.SlotSound = WriteSndMeta(SndMeta);
            _superView.MetaEditor.SelectedTag = null;
            _superView.SoundMetaEditor.SelectedTag = _superView.SoundMetaEditor.Tags[0];
            var session = _runner.GetRequiredServiceInCurrentEditorScope<CombatMetaDataEditSession>();

            _superView.SelectedTabControllerIndex = (int)MetaTab.Sound;
            var onSound = session.CurrentSource;
            _superView.SelectedTabControllerIndex = (int)MetaTab.Clip;

            Assert.That(onSound, Is.SameAs(_superView.SoundMetaEditor.Tags[0]._input));
            Assert.That(session.CurrentSource, Is.Null);
        }

        // Deferred item D14: the Sound tab's browse button says what it does.
        [Test, Apartment(ApartmentState.STA)]
        public void TheSoundTabsBrowseButton_HasATooltip()
        {
            _superView.SlotSound = WriteSndMeta(SndMeta);
            // The button is in the Sound tab, and a tab control builds only the tab it shows.
            _superView.SelectedTabControllerIndex = (int)MetaTab.Sound;
            AppResources.Load();
            var panel = new EditorView { DataContext = _superView };
            panel.Measure(new System.Windows.Size(1400, 900));
            panel.Arrange(new System.Windows.Rect(0, 0, 1400, 900));
            panel.UpdateLayout();
            var browse = Descendants(panel).OfType<System.Windows.Controls.Button>().Single(x => System.Windows.Automation.AutomationProperties.GetAutomationId(x) == "BrowseSoundTab");
            Assert.That(browse.ToolTip, Is.EqualTo("SuperView.BrowseSoundTab.Tooltip"), "the key, as tests have no language loaded");
            foreach (var language in new[] { "En", "Cn", "Fr" })
            {
                var strings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, $"Language_{language}.json")))!;
                Assert.That(strings.GetValueOrDefault("SuperView.BrowseSoundTab.Tooltip"), Is.Not.Empty, language);
            }
        }

        static void Pump()
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject root)
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var below in Descendants(child))
                    yield return below;
            }
        }
    }
}
