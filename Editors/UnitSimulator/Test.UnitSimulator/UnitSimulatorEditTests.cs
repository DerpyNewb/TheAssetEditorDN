using Editors.UnitSimulator;
using Editors.UnitSimulator.Pairing;
using Editors.UnitSimulator.Tables;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Test.TestingUtility.Shared;

namespace Test.UnitSimulator
{
    // Edits on a mod's own tables, in an editable pack: CA's cannot be saved.
    public class UnitSimulatorEditTests
    {
        const string AnimPack = "animations/database/battle/bin/derpy_test_tables.animpack";
        const string RiderPath = "animations/database/battle/bin/derpy_test_rider.bin";
        const string MountPath = "animations/database/battle/bin/derpy_test_mount.bin";

        static (AssetEditorTestRunner Runner, UnitSimulatorViewModel Sim, IPackFileContainer Pack) Open()
        {
            var runner = VanillaGame.Load();
            var pack = MadeUpTables.AddToEditablePack(runner, "derpy_test_tables.animpack",
                MadeUpTables.Table("derpy_test_mount", "horse01", "", ("ATTACK_1", "m_a1.anim"), ("ATTACK_2", "m_a2.anim")),
                MadeUpTables.Table("derpy_test_rider", "humanoid01", "derpy_test_mount", ("RIDER_ATTACK_1", "r_a1.anim")));
            var sim = UnitSimulatorAppTests.Open(runner);
            UnitSimulatorAppTests.PickTable(runner, sim, sim.Rider, "humanoid01", RiderPath);
            UnitSimulatorAppTests.PickTable(runner, sim, sim.Mount, "horse01", MountPath);
            UnitSimulatorAppTests.PickMountSlot(sim, "ATTACK_1");
            return (runner, sim, pack);
        }

        static AnimationBinWh3 Saved(AssetEditorTestRunner runner, IPackFileContainer pack, string table) =>
            new(table, AnimTableEdits.ReadEntries(runner.PackFileService.FindFile(AnimPack, pack)!.DataSource.ReadData()).Single(x => x.Name == table).Data);

        [Test]
        public void ASlotEdit_IsUnsaved_UntilSaveWritesThatTable()
        {
            var (runner, sim, pack) = Open();
            var mountBefore = Saved(runner, pack, MountPath).ToByteArray();

            sim.EditPart = "Rider";
            sim.SlotAnim = "animations/derpy/edited.anim";
            sim.ApplySlotEditCommand.Execute(null);
            Assert.That(sim.HasUnsavedChanges, Is.True);

            sim.SaveTablesCommand.Execute(null);
            Assert.That(sim.TableStatus, Is.EqualTo("Tables saved."));
            Assert.That(sim.HasUnsavedChanges, Is.False);
            Assert.That(AnimTableEdits.Row(Saved(runner, pack, RiderPath), 1901).AnimationRefs.Single().AnimationFile, Is.EqualTo("animations/derpy/edited.anim"));
            Assert.That(Saved(runner, pack, MountPath).ToByteArray(), Is.EqualTo(mountBefore));
        }

        [Test]
        public void AMissingRiderSlot_AddedFromTheRidersSlot_Pairs()
        {
            var (_, sim, _) = Open();
            sim.SelectedPair = sim.Pairs.Single(x => x.MountSlot == "ATTACK_2");
            Assert.That(sim.SelectedPair.Status, Is.EqualTo(PairStatus.RiderSlotMissing));

            sim.AddMissingRiderSlotCommand.Execute(null);

            Assert.That(sim.Pairs.Single(x => x.MountSlot == "ATTACK_2").RiderHasSlot, Is.True);
            Assert.That(sim.HasUnsavedChanges, Is.True);
        }

        // Review Focus 4.
        [Test]
        public void SavingWithNoEditablePack_KeepsTheEditAndSaysWhy()
        {
            var (runner, sim, _) = Open();
            sim.SlotAnim = "animations/derpy/edited.anim";
            sim.ApplySlotEditCommand.Execute(null);
            runner.PackFileService.SetEditablePack(null);

            sim.SaveTablesCommand.Execute(null);

            Assert.That(sim.TableStatus, Does.Contain("No editable pack"));
            Assert.That(sim.HasUnsavedChanges, Is.True);
        }

        // CA ships the knight in two animpacks, and after a save the editable pack holds its own copy of one, so a list read again holds
        // the table three times under one path. The next edit has to go to the saved copy, or its save writes CA's back over the first;
        // so too when a pack loaded after the editable one (an animation mod carrying CA's animpack) holds a fourth copy.
        [TestCase(false)]
        [TestCase(true)]
        public void ASecondEdit_AfterTheListIsReadAgain_KeepsTheFirstSavedEdit(bool aLaterPackHasTheAnimpack)
        {
            var runner = VanillaGame.Load();
            var editable = runner.CreateOutputPack();
            if (aLaterPackHasTheAnimpack)
            {
                var pfs = runner.PackFileService;
                var mod = runner.CreateEmptyPackFile("derpy_test_animation_mod", false);
                pfs.AddFilesToPack(mod, [new NewPackFileEntry(@"animations\database\battle\bin", PackFile.CreateFromBytes("animation_tables__.animpack", pfs.FindFile(VanillaGame.TablesAnimPack)!.DataSource.ReadData()))]);
            }
            var sim = UnitSimulatorAppTests.Open(runner);
            UnitSimulatorAppTests.PickTable(runner, sim, sim.Rider, "humanoid01", VanillaGame.KnightTable);
            UnitSimulatorAppTests.PickTable(runner, sim, sim.Mount, "horse01", VanillaGame.WarhorseTable);
            UnitSimulatorAppTests.PickMountSlot(sim, "ATTACK_1");
            sim.SlotAnim = "animations/derpy/first.anim";
            sim.ApplySlotEditCommand.Execute(null);
            var animPack = sim.Rider.FragAndSlotSelection.FragmentList.SelectedItem!.PackFileReference!.FileName;
            sim.SaveTablesCommand.Execute(null);
            Assert.That(sim.TableStatus, Is.EqualTo("Tables saved."));

            sim.Rider.Data.TriggerSkeletonChanged();
            var tables = sim.Rider.FragAndSlotSelection.FragmentList;
            Assert.That(tables.PossibleValues.Count(x => x.FullPath == VanillaGame.KnightTable), Is.EqualTo(1));
            tables.SelectedItem = tables.PossibleValues.Single(x => x.FullPath == VanillaGame.KnightTable);
            UnitSimulatorAppTests.PickMountSlot(sim, "ATTACK_2");
            sim.SlotAnim = "animations/derpy/second.anim";
            sim.ApplySlotEditCommand.Execute(null);
            sim.SaveTablesCommand.Execute(null);

            var saved = AnimTableEdits.ReadEntries(runner.PackFileService.FindFile(animPack, editable)!.DataSource.ReadData()).Single(x => x.Name == VanillaGame.KnightTable).Data;
            var knight = new AnimationBinWh3(VanillaGame.KnightTable, saved);
            Assert.That(AnimTableEdits.Row(knight, 1901).AnimationRefs.Any(x => x.AnimationFile == "animations/derpy/first.anim"), Is.True);
            Assert.That(AnimTableEdits.Row(knight, 1902).AnimationRefs.Any(x => x.AnimationFile == "animations/derpy/second.anim"), Is.True);
        }

        // Review Focus 5: a model picked after an edit re-reads the part's tables from the packs; the edit must stay in the list and the save.
        [Test]
        public void AnUnsavedEdit_SurvivesTheTableListBeingReadAgain()
        {
            var (runner, sim, pack) = Open();
            sim.SlotAnim = "animations/derpy/edited.anim";
            sim.ApplySlotEditCommand.Execute(null);

            sim.Rider.Data.TriggerSkeletonChanged();

            var listed = sim.Rider.FragAndSlotSelection.FragmentList.PossibleValues.Single(x => x.FullPath == RiderPath);
            Assert.That(listed.Entries.Any(x => x.AnimationFile == "animations/derpy/edited.anim"), Is.True);
            sim.SaveTablesCommand.Execute(null);
            Assert.That(AnimTableEdits.Row(Saved(runner, pack, RiderPath), 1901).AnimationRefs.Single().AnimationFile, Is.EqualTo("animations/derpy/edited.anim"));
        }

        // Undo takes edits back one at a time; one back to the table as saved is no longer an unsaved change.
        [Test]
        public void UndoTableEdit_TakesEditsBackOneAtATime()
        {
            var (_, sim, _) = Open();
            string RiderAnim() => AnimTableEdits.Row((AnimationBinWh3)sim.Rider.FragAndSlotSelection.FragmentList.SelectedItem!, 1901).AnimationRefs.Single().AnimationFile;
            var original = RiderAnim();
            sim.EditPart = "Rider";
            sim.SlotAnim = "animations/derpy/first.anim";
            sim.ApplySlotEditCommand.Execute(null);
            sim.SlotAnim = "animations/derpy/second.anim";
            sim.ApplySlotEditCommand.Execute(null);

            sim.UndoTableEditCommand.Execute(null);
            Assert.That((RiderAnim(), sim.SlotAnim, sim.HasUnsavedChanges), Is.EqualTo(("animations/derpy/first.anim", "animations/derpy/first.anim", true)));

            sim.UndoTableEditCommand.Execute(null);
            Assert.That((RiderAnim(), sim.HasUnsavedChanges), Is.EqualTo((original, false)));
            sim.UndoTableEditCommand.Execute(null);
            Assert.That(sim.TableStatus, Is.EqualTo("No table edit to undo."));
        }

        // A saved edit undone is an unsaved change again, and saving writes the table as it was.
        [Test]
        public void UndoTableEdit_AfterASave_IsSavedBackByTheNextSave()
        {
            var (runner, sim, pack) = Open();
            var before = Saved(runner, pack, RiderPath).ToByteArray();
            sim.EditPart = "Rider";
            sim.SlotAnim = "animations/derpy/edited.anim";
            sim.ApplySlotEditCommand.Execute(null);
            sim.SaveTablesCommand.Execute(null);

            sim.UndoTableEditCommand.Execute(null);
            Assert.That(sim.HasUnsavedChanges, Is.True);
            sim.SaveTablesCommand.Execute(null);

            Assert.That(Saved(runner, pack, RiderPath).ToByteArray(), Is.EqualTo(before));
        }

        // An added slot is undone too, and the pairing shows it missing again.
        [Test]
        public void UndoTableEdit_TakesBackAnAddedRiderSlot()
        {
            var (_, sim, _) = Open();
            sim.SelectedPair = sim.Pairs.Single(x => x.MountSlot == "ATTACK_2");
            sim.AddMissingRiderSlotCommand.Execute(null);

            sim.UndoTableEditCommand.Execute(null);

            Assert.That((sim.Pairs.Single(x => x.MountSlot == "ATTACK_2").RiderHasSlot, sim.HasUnsavedChanges), Is.EqualTo((false, false)));
        }
    }
}
