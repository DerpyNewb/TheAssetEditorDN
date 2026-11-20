using System.IO;
using Editors.AnimationMeta.SuperView;
using Editors.Shared.Core.Common.BaseControl;
using Moq;
using Shared.Core.Commands;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
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
    // drawn in the scene. The persistent row's .snd.meta is loaded for sound but gets no tab.
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

        // Two SOUND_TRIGGER v11 tags, at 0.2 s and 0.6 s, in the editable pack.
        string WriteSndMeta(string path)
        {
            var parser = _runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>();
            var file = new ParsedMetadataFile { Version = 2 };
            foreach (var at in new[] { 0.2f, 0.6f })
            {
                var tag = (SoundTrigger_v11)parser.CreateDefault("SOUND_TRIGGER_11");
                (tag.StartTime, tag.EndTime, tag.SoundEvent) = (at, at, "Derpy_Test_Swing");
                file.Attributes.Add(tag);
            }
            var pfs = _runner.PackFileService;
            pfs.AddFilesToPack(pfs.GetEditablePack()!, [new NewPackFileEntry(Path.GetDirectoryName(path)!, PackFile.CreateFromBytes(Path.GetFileName(path), parser.GenerateBytes(2, file)))]);
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
    }
}
