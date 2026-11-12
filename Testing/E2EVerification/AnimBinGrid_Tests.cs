using Microsoft.Extensions.DependencyInjection;
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

        // The view shows the grid on ShowGrid and the text editor otherwise.
        [Test]
        public void ShowGrid_IsAWh3TableNotShownAsXml()
        {
            var opened = Open();
            var told = new List<string?>();
            opened.Editor.PropertyChanged += (_, e) => told.Add(e.PropertyName);
            opened.Select("derpy_a");
            Assert.That((opened.Editor.ShowGrid, told.Contains(nameof(AnimPackViewModel.ShowGrid))), Is.EqualTo((true, true)));

            told.Clear();
            opened.Editor.ShowAsXml = true;
            Assert.That((opened.Editor.ShowGrid, told.Contains(nameof(AnimPackViewModel.ShowGrid))), Is.EqualTo((false, true)));

            opened.Editor.ShowAsXml = false;
            opened.Select("derpy_3k");
            Assert.That(opened.Editor.ShowGrid, Is.False);
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

        // Slots, clipboard, find and replace

        static List<string> SlotNames(AnimationBinWh3 table) => table.AnimationTableEntries.Select(x => AnimationSlotTypeHelperWh3.GetInstance().TryGetFromId((int)x.AnimationId)?.Value ?? x.AnimationId.ToString()).ToList();

        [Test]
        public void AddSlot_GoesInIdOrder_WithTheSurveyDefaults()
        {
            var (opened, table, grid, _) = Grid();
            opened.Editor.PickSlot = _ => "STAND_IDLE_1";
            grid.AddSlotCommand.Execute(null);

            Assert.That(SlotNames(table), Is.EqualTo(new[] { "STAND", "STAND_IDLE_1", "WALK_1", "RUN_1", "CHARGE" }));
            var added = table.AnimationTableEntries[1];
            Assert.That((added.BlendIn, added.SelectionWeight, added.WeaponBools, added.Unk), Is.EqualTo((0.3f, 1f, 63, false)));
            Assert.That(added.AnimationRefs.Select(x => (x.AnimationFile, x.AnimationMetaFile, x.AnimationSoundMetaFile)), Is.EqualTo(new[] { ("", "", "") }));
            Assert.That(RowOf(grid, "STAND_IDLE_1"), Is.Not.Null);
        }

        [Test]
        public void AddSlot_PickerSeesTheTablesSlots_AndACancelAddsNothing()
        {
            var (opened, table, grid, before) = Grid();
            IReadOnlyCollection<string>? offered = null;
            opened.Editor.PickSlot = x => { offered = x; return null; };
            grid.AddSlotCommand.Execute(null);
            Assert.That(offered, Is.EquivalentTo(new[] { "STAND", "WALK_1", "RUN_1", "CHARGE" }));
            Assert.That(table.ToByteArray(), Is.EqualTo(before));
        }

        [Test]
        public void DuplicateTwoSlots_IsOneUndo()
        {
            var (opened, table, grid, before) = Grid();
            var picks = new Queue<string>(["STAND_IDLE_1", "STAND_IDLE_2"]);
            opened.Editor.PickSlot = _ => picks.Dequeue();
            grid.SelectedRows = [RowOf(grid, "STAND"), RowOf(grid, "RUN_1")];
            grid.DuplicateSlotsCommand.Execute(null);

            Assert.That(SlotNames(table), Is.EqualTo(new[] { "STAND", "STAND_IDLE_1", "STAND_IDLE_2", "WALK_1", "RUN_1", "CHARGE" }));
            Assert.That(table.AnimationTableEntries[2].AnimationRefs.Single().AnimationFile, Is.EqualTo(KarlAnim));
            opened.Editor.UndoAction();
            Assert.That(table.ToByteArray(), Is.EqualTo(before));
        }

        [Test]
        public void RemoveSlots_IsOneUndo()
        {
            var (opened, table, grid, before) = Grid();
            grid.SelectedRows = [RowOf(grid, "WALK_1", 0), RowOf(grid, "WALK_1", 1), RowOf(grid, "RUN_1")];
            grid.RemoveSlotsCommand.Execute(null);
            Assert.That(SlotNames(table), Is.EqualTo(new[] { "STAND", "CHARGE" }));
            opened.Editor.UndoAction();
            Assert.That(table.ToByteArray(), Is.EqualTo(before));
        }

        [Test]
        public void CopyThenPaste_IntoAnotherTable_CopiesTheRows()
        {
            var (opened, _, grid, _) = Grid();
            grid.SelectedRows = [RowOf(grid, "WALK_1"), RowOf(grid, "RUN_1")];
            var copied = "";
            grid.SetClipboard = x => copied = x;
            grid.CopyCommand.Execute(null);

            var b = opened.Select("derpy_b");
            opened.Editor.BinGrid!.PasteSlots(copied);
            Assert.That(SlotNames(b), Is.EqualTo(new[] { "STAND", "WALK_1", "RUN_1", Unnamed.ToString() }));
            Assert.That(b.AnimationTableEntries[1].AnimationRefs.Select(x => x.AnimationFile), Is.EqualTo(new[] { KarlAnim, KarlAnim }));
        }

        [TestCase(ShowMessageBoxResult.OK, KarlAnim)]
        [TestCase(ShowMessageBoxResult.Cancel, "animations/derpy/b_stand.anim")]
        public void PasteAnExistingSlot_OkReplaces_CancelKeeps(ShowMessageBoxResult answer, string standAfter)
        {
            var (opened, _, grid, _) = Grid();
            grid.SelectedRows = [RowOf(grid, "STAND"), RowOf(grid, "RUN_1")];
            var copied = grid.CopySlots();
            var b = opened.Select("derpy_b");
            Answer(opened, "Paste", answer);

            opened.Editor.BinGrid!.PasteSlots(copied);
            Assert.That(b.AnimationTableEntries[0].AnimationRefs.Single().AnimationFile, Is.EqualTo(standAfter));
            Assert.That(SlotNames(b), Does.Contain("RUN_1"));
            opened.Runner.Dialogs.Verify(x => x.ShowYesNoBox(It.Is<string>(m => m.Contains("STAND")), "Paste"), Times.Once);
        }

        [TestCase("hello")]
        [TestCase("")]
        public void PastingTextThatIsNotAnimations_ChangesNothing(string text)
        {
            var (opened, table, grid, before) = Grid();
            grid.PasteSlots(text);
            Assert.That((table.ToByteArray(), opened.Editor.HasUnsavedChanges), Is.EqualTo((before, false)));
            Assert.That(grid.Status, Does.Contain("Nothing to paste"));
        }

        [Test]
        public void PasteTheXmlViewsText_Works()
        {
            var opened = Open();
            opened.Select("derpy_a");
            var text = opened.Editor.SelectedItemViewModel.Text;
            var b = opened.Select("derpy_b");
            Answer(opened, "Paste", ShowMessageBoxResult.Cancel);

            opened.Editor.BinGrid!.PasteSlots(text);
            Assert.That(SlotNames(b), Is.EqualTo(new[] { "STAND", "WALK_1", "RUN_1", "CHARGE", Unnamed.ToString() }));
            Assert.That(b.AnimationTableEntries[0].AnimationRefs.Single().AnimationFile, Is.EqualTo("animations/derpy/b_stand.anim"));
        }

        [Test]
        public void ReplaceInPaths_OverSelection_CountsCells()
        {
            var (_, table, grid, _) = Grid();
            grid.SelectedRows = [RowOf(grid, "WALK_1", 0), RowOf(grid, "WALK_1", 1)];
            Assert.That(grid.ReplaceInPaths("2HANDED_hammer", "spear"), Is.EqualTo(2));
            Assert.That(table.AnimationTableEntries.Select(x => x.AnimationRefs.Select(r => r.AnimationFile.Contains("/spear/")).ToList()),
                Is.EqualTo(new[] { new[] { false }, new[] { true, true }, new[] { false }, Array.Empty<bool>() }));
            Assert.That(grid.Status, Does.Contain("2"));
        }

        [Test]
        public void ReplaceInPaths_WithOneRowSelected_ChangesEveryRow_AsOneUndo()
        {
            var (opened, table, grid, before) = Grid();
            grid.SelectedRows = [RowOf(grid, "STAND")];
            grid.FindText = "2handed_hammer";
            grid.ReplaceText = "spear";
            grid.ReplaceCommand.Execute(null);
            Assert.That(table.AnimationTableEntries.SelectMany(x => x.AnimationRefs).Count(x => x.AnimationFile.Contains("/spear/")), Is.EqualTo(4));
            opened.Editor.UndoAction();
            Assert.That(table.ToByteArray(), Is.EqualTo(before));
        }

        [TestCase("nothing_like_this")]
        [TestCase("")]
        public void ReplaceInPaths_WithNoMatch_AddsNoUndoStep(string find)
        {
            var (opened, table, grid, before) = Grid();
            Assert.That(grid.ReplaceInPaths(find, "spear"), Is.EqualTo(0));
            Assert.That((table.ToByteArray(), opened.Editor.HasUnsavedChanges), Is.EqualTo((before, false)));
        }

        // Findings

        [Test]
        public void AMissingAnimation_MarksItsRow()
        {
            var (_, _, grid, _) = Grid();
            RowOf(grid, "STAND").Animation = "animations/nope.anim";
            var row = RowOf(grid, "STAND");
            Assert.That((row.HasWarning, row.HasError), Is.EqualTo((true, false)));
            Assert.That(row.Findings, Does.Contain("animations/nope.anim"));
            Assert.That(RowOf(grid, "RUN_1").Findings, Is.Null);
        }

        [Test]
        public void FixingThePath_ClearsTheMark()
        {
            var (_, _, grid, _) = Grid();
            RowOf(grid, "STAND").Animation = "animations/nope.anim";
            RowOf(grid, "STAND").Animation = KarlAnim;
            Assert.That((RowOf(grid, "STAND").HasWarning, RowOf(grid, "STAND").Findings), Is.EqualTo((false, (string?)null)));
        }

        [Test]
        public void AnEmptySlot_IsMarkedOnOpening()
        {
            var (_, _, grid, _) = Grid();
            Assert.That(RowOf(grid, "CHARGE", -1).Findings, Does.Contain("Slot does not have any animations"));
            Assert.That(grid.Problems, Does.Contain("1 warning"));
        }

        [Test]
        public void AWrongName_MarksTheHeader()
        {
            var (_, _, grid, _) = Grid();
            Assert.That(grid.NameProblem, Is.Null);
            grid.Name = "wrong";
            Assert.That(grid.NameProblem, Does.Contain("wrong"));
            Assert.That(grid.Problems, Does.Contain("1 error"));
        }

        // An empty graph is only a warning, and its field says so in orange; a graph that is not found is an error, in red.
        [Test]
        public void AnEmptyGraph_IsAWarning_AndAMissingOneAnError()
        {
            var (_, _, grid, _) = Grid();
            grid.LocomotionGraph = "";
            Assert.That((grid.GraphProblem, grid.GraphIsWarning), Is.EqualTo(("LocomotionGraph not provided", true)));
            grid.LocomotionGraph = "animations/locomotion_graphs/derpy_missing.xml";
            Assert.That((grid.GraphProblem, grid.GraphIsWarning), Is.EqualTo(("LocomotionGraph animations/locomotion_graphs/derpy_missing.xml is not found", false)));
        }

        // A header field's finding is written out under the header, not shown by a coloured border alone.
        [Test]
        public void HeaderFindings_AreWrittenOut()
        {
            var (_, _, grid, _) = Grid();
            Assert.That(grid.HeaderFindings, Is.Null);
            grid.Name = "wrong";
            grid.LocomotionGraph = "";
            Assert.That(grid.HeaderFindings, Does.Contain("wrong").And.Contain("LocomotionGraph not provided"));
        }

        [Test]
        public async Task TheDeepChecks_SayWhileTheyRun()
        {
            var (_, _, grid, _) = Grid();
            grid.DeepDelay = TimeSpan.Zero;
            var real = grid.DeepRunner;
            var entered = new TaskCompletionSource();
            var gate = new TaskCompletionSource();
            grid.DeepRunner = table =>
            {
                entered.TrySetResult();
                gate.Task.Wait();
                return real(table);
            };
            grid.StartDeepChecks();
            await entered.Task;
            Assert.That(grid.Checking, Is.True);
            gate.SetResult();
            await grid.DeepChecks;
            Assert.That(grid.Checking, Is.False);
        }

        [Test]
        public async Task DeepChecksThatFail_SaySo()
        {
            var (_, _, grid, _) = Grid();
            grid.DeepDelay = TimeSpan.Zero;
            grid.DeepRunner = _ => throw new InvalidOperationException("derpy broke");
            grid.StartDeepChecks();
            await grid.DeepChecks;
            Assert.That((grid.Checking, grid.Status), Is.EqualTo((false, "The file checks stopped: derpy broke")));
        }

        [Test]
        public async Task ADeepFinding_ShowsOnItsRow()
        {
            var (_, _, grid, _) = Grid();
            grid.DeepDelay = TimeSpan.Zero;
            RowOf(grid, "STAND").Meta = GraphA; // a file that is there, but no meta
            await grid.DeepChecks;
            Assert.That(RowOf(grid, "STAND").Findings, Does.Contain("does not appear to be a valid meta animation"));
        }

        // A run started before an edit that finishes after the next one's has checked a table that is no longer there.
        [Test]
        public async Task AStaleDeepResult_IsDropped()
        {
            var (_, _, grid, _) = Grid();
            grid.DeepDelay = TimeSpan.Zero;
            var real = grid.DeepRunner;
            var entered = new TaskCompletionSource();
            var gate = new TaskCompletionSource();
            var calls = 0;
            grid.DeepRunner = table =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    entered.SetResult();
                    gate.Task.Wait();
                }
                return real(table);
            };

            RowOf(grid, "STAND").Meta = GraphA;
            var first = grid.DeepChecks;
            await entered.Task;
            RowOf(grid, "STAND").Meta = "";
            await grid.DeepChecks;
            gate.SetResult();
            await first;

            Assert.That(RowOf(grid, "STAND").Findings, Is.Null);
        }

        // Browse and Open

        static List<(List<string> Extensions, Func<string, bool>? Filter)> BrowseAnswers(Opened opened, string? path)
        {
            var asked = new List<(List<string>, Func<string, bool>?)>();
            var file = path == null ? null : opened.Runner.PackFileService.FindFile(path);
            opened.Runner.Dialogs.Setup(x => x.DisplayBrowseDialog(It.IsAny<List<string>>(), It.IsAny<Func<string, bool>?>()))
                .Callback<List<string>, Func<string, bool>?>((extensions, filter) => asked.Add((extensions, filter)))
                .Returns(new BrowseDialogResultFile(file != null, file!));
            return asked;
        }

        // CHARGE has no variant, so the pick also gives it one, still as one undo step.
        [Test]
        public void BrowseAnimation_OffersThisSkeletonsAnimations_AndWritesThePickedPath()
        {
            var (opened, table, grid, before) = Grid();
            var asked = BrowseAnswers(opened, KarlAnim);
            grid.BrowseAnimationCommand.Execute(RowOf(grid, "CHARGE", -1));

            var (extensions, filter) = asked.Single();
            Assert.That(extensions, Is.EqualTo(new[] { ".anim" }));
            Assert.That(filter, Is.Not.Null);
            Assert.That((filter!(KarlAnim.Replace('/', '\\').ToUpperInvariant()), filter("animations/battle/humanoid01/not_his.anim")), Is.EqualTo((true, false)));
            Assert.That(RowOf(grid, "CHARGE").Animation, Is.EqualTo(KarlAnim));
            opened.Editor.UndoAction();
            Assert.That(table.ToByteArray(), Is.EqualTo(before));
        }

        [Test]
        public void BrowseAnimation_NotFilteredToTheSkeleton_OffersEveryAnimation()
        {
            var (opened, _, grid, _) = Grid();
            var asked = BrowseAnswers(opened, null);
            grid.AnyAnimation = true;
            grid.BrowseAnimationCommand.Execute(RowOf(grid, "STAND"));
            Assert.That(asked.Single().Filter, Is.Null);
        }

        [Test]
        public void BrowseMetaSoundAndGraph_AskForTheirOwnFileTypes()
        {
            var (opened, _, grid, _) = Grid();
            var asked = BrowseAnswers(opened, GraphA);
            grid.BrowseMetaCommand.Execute(RowOf(grid, "STAND"));
            grid.BrowseSoundCommand.Execute(RowOf(grid, "STAND"));
            grid.BrowseGraphCommand.Execute(null);

            Assert.That(asked.Select(x => string.Join(" ", x.Extensions)), Is.EqualTo(new[] { ".anm.meta .meta", ".snd.meta", ".xml" }));
            Assert.That(asked.Select(x => x.Filter), Is.All.Null);
            Assert.That((RowOf(grid, "STAND").Meta, RowOf(grid, "STAND").Sound, grid.LocomotionGraph), Is.EqualTo((GraphA, GraphA, GraphA)));
        }

        [Test]
        public void ABrowseCancelled_ChangesNothing()
        {
            var (opened, table, grid, before) = Grid();
            BrowseAnswers(opened, null);
            grid.BrowseAnimationCommand.Execute(RowOf(grid, "STAND"));
            grid.BrowseGraphCommand.Execute(null);
            Assert.That(table.ToByteArray(), Is.EqualTo(before));
            Assert.That(opened.Editor.HasUnsavedChanges, Is.False);
        }

        [Test]
        public void OpenPath_OpensTheFileInItsEditor()
        {
            var (opened, _, grid, _) = Grid();
            grid.OpenPathCommand.Execute(GraphA);
            var graph = opened.Runner.PackFileService.FindFile(GraphA);
            Assert.That(opened.Runner.ServiceProvider.GetRequiredService<IEditorManager>().GetAllEditors().OfType<IFileEditor>().Select(x => x.CurrentFile), Does.Contain(graph));
        }

        [Test]
        public void OpenPath_NotInTheLoadedPacks_SaysSo()
        {
            var (_, _, grid, _) = Grid();
            grid.OpenPathCommand.Execute("animations/derpy/missing.anim");
            Assert.That(grid.Status, Is.EqualTo("animations/derpy/missing.anim is not in the loaded packs."));
        }
    }
}
