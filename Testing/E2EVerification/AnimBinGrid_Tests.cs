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

        const uint Unnamed = 99999;

        // derpy_a: a WH3 table with four slots, WALK_1 with two variants and CHARGE with none. derpy_b: a second WH3 table to paste
        // into, holding a slot id past the end of the slot list. derpy_c: one slot the checks find nothing wrong with, for an Apply
        // that opens no findings window. derpy_3k: a Three Kingdoms table (version 2), which keeps the XML view.
        static Opened Open()
        {
            var runner = new AssetEditorTestRunner();
            var a = Table("derpy_a");
            a.AnimationTableEntries.Add(Row("STAND", (KarlAnim, "", "")));
            a.AnimationTableEntries.Add(Row("WALK_1", (KarlAnim, "", ""), (KarlAnim, "", "")));
            a.AnimationTableEntries.Add(Row("RUN_1", (KarlAnim, "", "")));
            a.AnimationTableEntries.Add(Row("CHARGE"));
            var b = Table("derpy_b");
            b.AnimationTableEntries.Add(Row("STAND", ("animations/derpy/b_stand.anim", "", "")));
            b.AnimationTableEntries.Add(new AnimationBinEntry { AnimationId = Unnamed, BlendIn = 0.3f, SelectionWeight = 1f, AnimationRefs = [new() { AnimationFile = KarlAnim, AnimationMetaFile = "", AnimationSoundMetaFile = "" }] });
            var clean = Table("derpy_c");
            clean.AnimationTableEntries.Add(Row("STAND", (KarlAnim, "", "")));
            var threeKingdoms = new AnimationBinWh3(Bin + "derpy_3k.bin") { TableVersion = 2, Name = "derpy_3k", SkeletonName = "humanoid01" };

            var animPack = new AnimationPackFileDatabase("derpy_test.animpack");
            animPack.AddFile(a);
            animPack.AddFile(b);
            animPack.AddFile(clean);
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
            var table = opened.Select("derpy_c");
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

        // Rows and fields

        static BinGridRow RowOf(BinGridViewModel grid, string slot, int variant = 0) => grid.Rows.Single(x => x.Slot == slot && x.Variant == variant);

        static (Opened Opened, AnimationBinWh3 Table, BinGridViewModel Grid, byte[] Before) Grid(string name = "derpy_a")
        {
            var opened = Open();
            var table = opened.Select(name);
            return (opened, table, opened.Editor.BinGrid!, table.ToByteArray());
        }

        [Test]
        public void TickingWeapon3_SetsBit3Only()
        {
            var (opened, table, grid, before) = Grid();
            RowOf(grid, "STAND").Weapon3 = true;
            Assert.That(table.AnimationTableEntries[0].WeaponBools, Is.EqualTo(8));
            opened.Editor.UndoAction();
            Assert.That(table.ToByteArray(), Is.EqualTo(before));
        }

        [Test]
        public void UntickingWeapon0_KeepsABitPastTheSixth()
        {
            var (opened, table, grid, _) = Grid();
            opened.Editor.EditTable(table, x => x.AnimationTableEntries[2].WeaponBools = 0x101);
            RowOf(grid, "RUN_1").Weapon0 = false;
            Assert.That(table.AnimationTableEntries[2].WeaponBools, Is.EqualTo(0x100));
        }

        [Test]
        public void SimpleFlightAndRiderFlag_SetTheirOwnBits()
        {
            var (opened, table, grid, _) = Grid();
            opened.Editor.EditTable(table, x => x.UnknownValue1 = 4);
            grid.SimpleFlight = true;
            grid.RiderFlag = true;
            Assert.That((table.UnknownValue1, grid.SimpleFlight, grid.RiderFlag), Is.EqualTo(((short)(4 + 1 + 256), true, true)));
            grid.SimpleFlight = false;
            Assert.That(table.UnknownValue1, Is.EqualTo((short)(4 + 256)));
        }

        [Test]
        public void EditingBlendAndWeight_WritesTheFloats()
        {
            var (_, table, grid, _) = Grid();
            RowOf(grid, "STAND").BlendIn = "0.25";
            RowOf(grid, "STAND").Weight = "2";
            Assert.That((table.AnimationTableEntries[0].BlendIn, table.AnimationTableEntries[0].SelectionWeight), Is.EqualTo((0.25f, 2f)));
            Assert.That(RowOf(grid, "STAND").BlendIn, Is.EqualTo("0.25"));
        }

        [TestCase("-")]
        [TestCase(",5")]
        [TestCase("abc")]
        public void AHalfTypedNumber_IsKeptMarkedAndNotWritten(string typed)
        {
            var (opened, table, grid, before) = Grid();
            var row = RowOf(grid, "STAND");
            row.BlendIn = typed;
            Assert.That((table.ToByteArray(), row.BlendInvalid, row.BlendIn, opened.Editor.HasUnsavedChanges), Is.EqualTo((before, true, typed, false)));

            row.BlendIn = "0.";
            Assert.That((table.AnimationTableEntries[0].BlendIn, row.BlendInvalid), Is.EqualTo((0f, false)));
        }

        [Test]
        public void ChangingTheSlot_ToOneTheTableHas_ChangesNothing()
        {
            var (_, table, grid, before) = Grid();
            RowOf(grid, "STAND").Slot = "RUN_1";
            Assert.That(table.ToByteArray(), Is.EqualTo(before));
            Assert.That(grid.Status, Does.Contain("RUN_1"));
        }

        [Test]
        public void ChangingTheSlot_ToANewOne_KeepsItsPlace()
        {
            var (_, table, grid, _) = Grid();
            RowOf(grid, "STAND").Slot = "STAND_IDLE_1";
            Assert.That(table.AnimationTableEntries[0].AnimationId, Is.EqualTo((uint)AnimationSlotTypeHelperWh3.GetfromValue("STAND_IDLE_1").Id));
        }

        [Test]
        public void ChangingTheSlot_ToANameThatIsNoSlot_ChangesNothing()
        {
            var (_, table, grid, before) = Grid();
            RowOf(grid, "STAND").Slot = "NOT_A_SLOT";
            Assert.That(table.ToByteArray(), Is.EqualTo(before));
            Assert.That(grid.Status, Does.Contain("NOT_A_SLOT"));
        }

        [Test]
        public void AnUnnamedSlot_KeepsItsIdWhenItsBlendIsEdited()
        {
            var (_, table, grid, _) = Grid("derpy_b");
            var row = grid.Rows.Single(x => x.Entry == 1);
            Assert.That(row.Slot, Is.EqualTo($"Not found [id={Unnamed}]"));
            row.BlendIn = "0.5";
            Assert.That((table.AnimationTableEntries[1].AnimationId, table.AnimationTableEntries[1].BlendIn), Is.EqualTo((Unnamed, 0.5f)));
        }

        [Test]
        public void SettingAPath_WritesThatVariantOnly()
        {
            var (_, table, grid, _) = Grid();
            RowOf(grid, "WALK_1", 1).Animation = "animations/derpy/x.anim";
            RowOf(grid, "WALK_1", 1).Meta = "animations/derpy/x.anm.meta";
            RowOf(grid, "WALK_1", 1).Sound = "animations/derpy/x.snd.meta";
            var refs = table.AnimationTableEntries[1].AnimationRefs;
            Assert.That(refs.Select(x => (x.AnimationFile, x.AnimationMetaFile, x.AnimationSoundMetaFile)),
                Is.EqualTo(new[] { (KarlAnim, "", ""), ("animations/derpy/x.anim", "animations/derpy/x.anm.meta", "animations/derpy/x.snd.meta") }));
        }

        [Test]
        public void ASlotWithNoVariants_IsOneRow_AndTakesAVariant()
        {
            var (_, table, grid, _) = Grid();
            var charge = grid.Rows.Where(x => x.Slot == "CHARGE").ToList();
            Assert.That(charge.Select(x => (x.Variant, x.Animation)), Is.EqualTo(new[] { (-1, "") }));

            charge[0].Animation = "animations/derpy/charge.anim";
            Assert.That(table.AnimationTableEntries[3].AnimationRefs.Select(x => (x.AnimationFile, x.AnimationMetaFile, x.AnimationSoundMetaFile)),
                Is.EqualTo(new[] { ("animations/derpy/charge.anim", "", "") }));
            Assert.That(RowOf(grid, "CHARGE").Animation, Is.EqualTo("animations/derpy/charge.anim"));
        }

        [Test]
        public void AddVariant_AppendsAnEmptyOne()
        {
            var (_, table, grid, _) = Grid();
            grid.AddVariantCommand.Execute(RowOf(grid, "STAND"));
            Assert.That(table.AnimationTableEntries[0].AnimationRefs.Select(x => x.AnimationFile), Is.EqualTo(new[] { KarlAnim, "" }));
            Assert.That(grid.Rows.Count(x => x.Slot == "STAND"), Is.EqualTo(2));
        }

        [Test]
        public void RemoveVariant_OfTheLastOne_LeavesTheSlot()
        {
            var (_, table, grid, _) = Grid();
            grid.RemoveVariantCommand.Execute(RowOf(grid, "STAND"));
            Assert.That(table.AnimationTableEntries[0].AnimationRefs, Is.Empty);
            Assert.That(RowOf(grid, "STAND", -1), Is.Not.Null);
        }

        [Test]
        public void TickingABox_WithThreeRowsSelected_SetsAllThree_InOneUndo()
        {
            var (opened, table, grid, before) = Grid();
            grid.SelectedRows = [RowOf(grid, "STAND"), RowOf(grid, "WALK_1"), RowOf(grid, "RUN_1")];
            RowOf(grid, "STAND").Weapon2 = true;
            Assert.That(table.AnimationTableEntries.Take(3).Select(x => x.WeaponBools), Is.EqualTo(new[] { 4, 4, 4 }));
            Assert.That(table.AnimationTableEntries[3].WeaponBools, Is.EqualTo(0));

            opened.Editor.UndoAction();
            Assert.That(table.ToByteArray(), Is.EqualTo(before));
        }

        [Test]
        public void ABoxTickedOnARowOutsideTheSelection_SetsThatRowOnly()
        {
            var (_, table, grid, _) = Grid();
            grid.SelectedRows = [RowOf(grid, "STAND"), RowOf(grid, "WALK_1")];
            RowOf(grid, "RUN_1").Weapon2 = true;
            Assert.That(table.AnimationTableEntries.Take(3).Select(x => x.WeaponBools), Is.EqualTo(new[] { 0, 0, 4 }));
        }

        [Test]
        public void AFieldEdit_KeepsTheSameRowObjects()
        {
            var (_, _, grid, _) = Grid();
            var rows = grid.Rows.ToList();
            rows[0].Weapon1 = true;
            rows[0].BlendIn = "0.4";
            Assert.That(grid.Rows, Is.EqualTo(rows).Using<BinGridRow>((x, y) => ReferenceEquals(x, y)));
        }

        [Test]
        public void ReadingEveryRow_ChangesNothing()
        {
            var (opened, table, grid, before) = Grid();
            ReadEverything(grid);
            Assert.That((table.ToByteArray(), opened.Editor.HasUnsavedChanges), Is.EqualTo((before, false)));
        }

        /// <summary>Every public property of the grid and its rows, read once, as the view would.</summary>
        internal static void ReadEverything(BinGridViewModel grid)
        {
            foreach (var property in typeof(BinGridViewModel).GetProperties().Where(x => x.GetIndexParameters().Length == 0))
                _ = property.GetValue(grid);
            foreach (var row in grid.Rows)
                foreach (var property in typeof(BinGridRow).GetProperties().Where(x => x.GetIndexParameters().Length == 0))
                    _ = property.GetValue(row);
        }

        [Test]
        public void HeaderEdits_EachWriteTheirField_AndUndoAll()
        {
            var (opened, table, grid, before) = Grid();
            grid.Name = "renamed";
            grid.Skeleton = "humanoid01e";
            grid.MountBin = "derpy_b";
            grid.LocomotionGraph = "animations/locomotion_graphs/other.xml";
            grid.UnknownText = "rider";
            Assert.That((table.Name, table.SkeletonName, table.MountBin, table.LocomotionGraph, table.Unknown),
                Is.EqualTo(("renamed", "humanoid01e", "derpy_b", "animations/locomotion_graphs/other.xml", "rider")));

            for (var i = 0; i < 5; i++)
                opened.Editor.UndoAction();
            Assert.That(table.ToByteArray(), Is.EqualTo(before));
        }

        [Test]
        public void MatchFileName_SetsTheNameToTheFiles()
        {
            var (_, table, grid, _) = Grid();
            grid.Name = "wrong";
            grid.MatchFileNameCommand.Execute(null);
            Assert.That(table.Name, Is.EqualTo("derpy_a"));
        }
    }
}
