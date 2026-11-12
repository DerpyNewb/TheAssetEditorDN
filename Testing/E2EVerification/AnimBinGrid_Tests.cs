using CommonControls.Editors.AnimationPack;
using Editors.AnimationFragmentEditor.AnimationPack.BinGrid;
using Moq;
using Shared.Core.Commands;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.E2EVerification
{
    // The animpack editor's grid for a WH3 battle table: every edit is one step on the editor's own undo stack, and the grid and
    // the XML view always show the same bytes.
    internal class AnimBinGrid_Tests
    {
        const string Bin = "animations/database/battle/bin/";
        const string AnimPackPath = Bin + "derpy_test.animpack";
        const string GraphA = "animations/locomotion_graphs/derpy_a.xml";
        const string KarlAnim = "animations/battle/humanoid01/2handed_hammer/stand/hu1_2hh_stand_idle_01.anim";

        static AnimationBinWh3 Table(string name) => new(Bin + name + ".bin") { Name = name, SkeletonName = "humanoid01", LocomotionGraph = GraphA };

        static AnimationBinEntry Row(string slot, params (string Anim, string Meta, string Sound)[] refs) => new()
        {
            AnimationId = (uint)AnimationSlotTypeHelperWh3.GetfromValue(slot).Id,
            BlendIn = 0.3f,
            SelectionWeight = 1f,
            WeaponBools = 0,
            AnimationRefs = refs.Select(x => new AnimationBinEntry.AnimationRef { AnimationFile = x.Anim, AnimationMetaFile = x.Meta, AnimationSoundMetaFile = x.Sound }).ToList(),
        };

        sealed record Opened(AssetEditorTestRunner Runner, AnimPackViewModel Editor)
        {
            public AnimationBinWh3 Select(string name)
            {
                var table = Editor.AnimationPackItems.PossibleValues.OfType<AnimationBinWh3>().Single(x => x.Name == name);
                Editor.AnimationPackItems.SelectedItem = table;
                return table;
            }

            public byte[] PackBytes() => Runner.PackFileService.FindFile(AnimPackPath)!.DataSource.ReadData();
        }

        // derpy_a: a WH3 table with three slots, one of them two variants. derpy_b: a second WH3 table to paste into. derpy_3k: a
        // Three Kingdoms table (version 2), which keeps the XML view.
        static Opened Open()
        {
            var runner = new AssetEditorTestRunner();
            var a = Table("derpy_a");
            a.AnimationTableEntries.Add(Row("STAND", (KarlAnim, "", "")));
            a.AnimationTableEntries.Add(Row("WALK_1", (KarlAnim, "", ""), (KarlAnim, "", "")));
            a.AnimationTableEntries.Add(Row("RUN_1", (KarlAnim, "", "")));
            var b = Table("derpy_b");
            b.AnimationTableEntries.Add(Row("STAND", ("animations/derpy/b_stand.anim", "", "")));
            var threeKingdoms = new AnimationBinWh3(Bin + "derpy_3k.bin") { TableVersion = 2, Name = "derpy_3k", SkeletonName = "humanoid01" };

            var animPack = new AnimationPackFileDatabase("derpy_test.animpack");
            animPack.AddFile(a);
            animPack.AddFile(b);
            animPack.AddFile(threeKingdoms);
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true);
            var pack = runner.CreateOutputPack();
            runner.PackFileService.AddFilesToPack(pack, [
                new NewPackFileEntry(@"animations\database\battle\bin", PackFile.CreateFromBytes("derpy_test.animpack", AnimationPackSerializer.ConvertToBytes(animPack))),
                new NewPackFileEntry(@"animations\locomotion_graphs", PackFile.CreateFromBytes("derpy_a.xml", [0]))]);
            var editor = (AnimPackViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute(runner.PackFileService.FindFile(AnimPackPath)!, EditorEnums.AnimationPack_Editor);
            return new(runner, editor);
        }

        static void Answer(Opened opened, string title, ShowMessageBoxResult answer) =>
            opened.Runner.Dialogs.Setup(x => x.ShowYesNoBox(It.IsAny<string>(), title)).Returns(answer);

        [Test]
        public void AWh3Table_OpensAGrid_AndAThreeKingdomsTableDoesNot()
        {
            var opened = Open();
            var table = opened.Select("derpy_a");
            Assert.That(opened.Editor.BinGrid?.Table, Is.SameAs(table));
            opened.Select("derpy_3k");
            Assert.That(opened.Editor.BinGrid, Is.Null);
        }

        [Test]
        public void EditTable_IsOneUndoStep()
        {
            var opened = Open();
            var table = opened.Select("derpy_a");
            var before = table.ToByteArray();

            opened.Editor.EditTable(table, x => x.SkeletonName = "humanoid01b");
            var after = table.ToByteArray();
            Assert.That(after, Is.Not.EqualTo(before));
            Assert.That((table.SkeletonName, opened.Editor.HasUnsavedChanges, table.IsChanged.Value), Is.EqualTo(("humanoid01b", true, true)));

            opened.Editor.UndoAction();
            Assert.That(table.ToByteArray(), Is.EqualTo(before));
            opened.Editor.RedoAction();
            Assert.That(table.ToByteArray(), Is.EqualTo(after));
        }

        [Test]
        public void EditTable_ThatChangesNothing_AddsNoUndoStep()
        {
            var opened = Open();
            var table = opened.Select("derpy_a");
            opened.Editor.EditTable(table, x => x.SkeletonName = "humanoid01");
            Assert.That((opened.Editor.HasUnsavedChanges, table.IsChanged.Value), Is.EqualTo((false, false)));
        }

        [Test]
        public void UndoInTheGrid_ReloadsTheSameGrid()
        {
            var opened = Open();
            var table = opened.Select("derpy_a");
            var grid = opened.Editor.BinGrid;
            opened.Editor.EditTable(table, x => x.SkeletonName = "humanoid01b");
            opened.Editor.UndoAction();
            Assert.That(opened.Editor.BinGrid, Is.SameAs(grid));
        }

        [Test]
        public void SaveWithAHardError_AsksAndCancelWritesNothing()
        {
            var opened = Open();
            var table = opened.Select("derpy_a");
            var before = opened.PackBytes();
            opened.Editor.EditTable(table, x => x.Name = "wrong");
            Answer(opened, "Save", ShowMessageBoxResult.Cancel);

            Assert.That(opened.Editor.Save(), Is.False);
            Assert.That(opened.PackBytes(), Is.EqualTo(before));
            opened.Runner.Dialogs.Verify(x => x.ShowYesNoBox(It.Is<string>(m => m.Contains("derpy_a") && m.Contains("wrong")), "Save"), Times.Once);
        }

        [Test]
        public void SaveWithAHardError_OkSavesAnyway()
        {
            var opened = Open();
            var table = opened.Select("derpy_a");
            var before = opened.PackBytes();
            opened.Editor.EditTable(table, x => x.Name = "wrong");
            Answer(opened, "Save", ShowMessageBoxResult.OK);

            Assert.That(opened.Editor.Save(), Is.True);
            Assert.That(opened.PackBytes(), Is.Not.EqualTo(before));
        }

        [Test]
        public void SaveWithNoProblem_DoesNotAsk()
        {
            var opened = Open();
            var table = opened.Select("derpy_a");
            opened.Editor.EditTable(table, x => x.AnimationTableEntries[0].BlendIn = 0.5f);
            Assert.That(opened.Editor.Save(), Is.True);
            opened.Runner.Dialogs.Verify(x => x.ShowYesNoBox(It.IsAny<string>(), "Save"), Times.Never);
        }

        [Test, Apartment(ApartmentState.STA)]
        public void SwitchingToTheGrid_WithUnappliedText_AsksToApply()
        {
            var opened = Open();
            var table = opened.Select("derpy_a");
            opened.Editor.ShowAsXml = true;
            opened.Editor.SelectedItemViewModel.Text = opened.Editor.SelectedItemViewModel.Text.Replace("<SkeletonName>humanoid01</SkeletonName>", "<SkeletonName>humanoid01e</SkeletonName>");
            Answer(opened, "Show grid", ShowMessageBoxResult.OK);

            opened.Editor.ShowAsXml = false;
            Assert.That((opened.Editor.ShowAsXml, table.SkeletonName), Is.EqualTo((false, "humanoid01e")));
        }

        [Test, Apartment(ApartmentState.STA)]
        public void SwitchingToXml_ShowsTheGridsEdits()
        {
            var opened = Open();
            var table = opened.Select("derpy_a");
            opened.Editor.EditTable(table, x => x.SkeletonName = "humanoid01e");
            opened.Editor.ShowAsXml = true;
            Assert.That(opened.Editor.SelectedItemViewModel.Text, Does.Contain("humanoid01e"));
        }
    }
}
