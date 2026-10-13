using Editors.UnitSimulator;
using Editors.UnitSimulator.Pairing;
using Editors.UnitSimulator.Tables;
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
            UnitSimulatorAppTests.PickRiderTable(runner, sim, "humanoid01", RiderPath);
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

        [Test]
        public void AMountBinNoTableHas_IsNamed()
        {
            var (_, sim, _) = Open();
            sim.RiderMountBin = "derpy_nothing";
            sim.ApplyMountBinCommand.Execute(null);
            Assert.That(sim.LinkStatus, Is.EqualTo("No loaded animpack has the mount table derpy_nothing."));
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
    }
}
