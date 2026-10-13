using Editors.Shared.Core.Common;
using Editors.UnitSimulator;
using Editors.UnitSimulator.Pairing;
using Shared.Core.Commands;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;

namespace Test.UnitSimulator
{
    public class UnitSimulatorAppTests
    {
        internal static UnitSimulatorViewModel Open(AssetEditorTestRunner runner) =>
            (UnitSimulatorViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute(EditorEnums.UnitSimulator_Editor);

        // Files first, as the spec has it: the rider's skeleton (a model would give it the same), then its table.
        internal static void PickRiderTable(AssetEditorTestRunner runner, UnitSimulatorViewModel sim, string skeleton, string table)
        {
            var editor = runner.ScopeRepository.GetRequiredService<SceneObjectEditor>(sim);
            editor.SetSkeleton(sim.Rider.Data, runner.PackFileService.FindFile($@"animations\skeletons\{skeleton}.anim")!);
            var tables = sim.Rider.FragAndSlotSelection.FragmentList;
            tables.SelectedItem = tables.PossibleValues.First(x => x.FullPath == table);
        }

        internal static void PickMountSlot(UnitSimulatorViewModel sim, string slot)
        {
            var slots = sim.Mount.FragAndSlotSelection.FragmentSlotList;
            slots.SelectedItem = slots.PossibleValues.First(x => x.SlotName == slot);
        }

        [Test]
        public void TheKnightsTable_PicksTheWarhorsesTable_AndPairsIt()
        {
            var runner = VanillaGame.Load();
            var sim = Open(runner);
            PickRiderTable(runner, sim, "humanoid01", VanillaGame.KnightTable);

            Assert.That(sim.LinkStatus, Is.Empty);
            Assert.That(sim.Mount.Data.SkeletonName.Value, Does.EndWith("horse01.anim"));
            Assert.That(sim.Mount.FragAndSlotSelection.FragmentList.SelectedItem?.FullPath, Is.EqualTo(VanillaGame.WarhorseTable));
            Assert.That(sim.Pairs, Has.Count.EqualTo(79));
            Assert.That(sim.Pairs.Count(x => x.Status == PairStatus.RiderSlotMissing), Is.EqualTo(16));
            Assert.That(sim.RiderOnlySlots, Has.Count.EqualTo(8));
        }

        [Test]
        public void AMountSlot_PlaysItsRiderSlot()
        {
            var runner = VanillaGame.Load();
            var sim = Open(runner);
            PickRiderTable(runner, sim, "humanoid01", VanillaGame.KnightTable);

            PickMountSlot(sim, "ATTACK_1");
            Assert.That(sim.Rider.FragAndSlotSelection.FragmentSlotList.SelectedItem?.SlotName, Is.EqualTo("RIDER_ATTACK_1"));

            sim.SelectedPair = sim.Pairs.First(x => x.MountSlot == "ATTACK_2");
            Assert.That(sim.Mount.FragAndSlotSelection.FragmentSlotList.SelectedItem?.SlotName, Is.EqualTo("ATTACK_2"));
            Assert.That(sim.Rider.FragAndSlotSelection.FragmentSlotList.SelectedItem?.SlotName, Is.EqualTo("RIDER_ATTACK_2"));
        }

        [Test]
        public void ARiderTableNamingNoLoadedTable_SaysWhich()
        {
            var runner = VanillaGame.Load();
            MadeUpTables.AddToEditablePack(runner, "derpy_test_lost.animpack",
                MadeUpTables.Table("derpy_test_lost_rider", "humanoid01", "derpy_no_such_mount", ("RIDER_ATTACK_1", "a.anim")));
            var sim = Open(runner);
            PickRiderTable(runner, sim, "humanoid01", "animations/database/battle/bin/derpy_test_lost_rider.bin");
            Assert.That(sim.LinkStatus, Is.EqualTo("No loaded animpack has the mount table derpy_no_such_mount."));
            Assert.That(sim.Pairs, Is.Empty);
        }

        // Review Focus 2.
        [Test]
        public void AMountTableWhoseSkeletonIsMissing_SaysWhich()
        {
            var runner = VanillaGame.Load();
            MadeUpTables.AddToEditablePack(runner, "derpy_test_ghost.animpack",
                MadeUpTables.Table("derpy_test_ghost_mount", "derpy_no_such_skeleton", "", ("ATTACK_1", "m.anim")),
                MadeUpTables.Table("derpy_test_ghost_rider", "humanoid01", "derpy_test_ghost_mount", ("RIDER_ATTACK_1", "r.anim")));
            var sim = Open(runner);
            PickRiderTable(runner, sim, "humanoid01", "animations/database/battle/bin/derpy_test_ghost_rider.bin");
            Assert.That(sim.LinkStatus, Does.Contain("derpy_no_such_skeleton"));
        }

        // Review Focus 1: the row the Pairing tab marks anim missing, clicked. The picker threw here before this task's guard.
        [Test]
        public void ASlotWhoseAnimNoPackHas_PlaysNothing()
        {
            var runner = VanillaGame.Load();
            MadeUpTables.AddToEditablePack(runner, "derpy_test_lame.animpack",
                MadeUpTables.Table("derpy_test_lame_mount", "horse01", "", ("ATTACK_1", "animations/derpy/no_such_mount.anim")),
                MadeUpTables.Table("derpy_test_lame_rider", "humanoid01", "derpy_test_lame_mount", ("RIDER_ATTACK_1", "animations/derpy/no_such_rider.anim")));
            var sim = Open(runner);
            PickRiderTable(runner, sim, "humanoid01", "animations/database/battle/bin/derpy_test_lame_rider.bin");
            Assert.That(sim.Pairs.Single().Status, Is.EqualTo(PairStatus.AnimMissing));

            Assert.DoesNotThrow(() => sim.SelectedPair = sim.Pairs.Single());

            Assert.That(sim.Rider.FragAndSlotSelection.FragmentSlotList.SelectedItem?.SlotName, Is.EqualTo("RIDER_ATTACK_1"));
            Assert.That(sim.Mount.Data.AnimationClip, Is.Null);
            Assert.That(sim.Rider.Data.AnimationClip, Is.Null);
        }

        // Review Focus 3: a mount model picked after the link rebuilds the mount's table list, which clears its selection.
        [Test]
        public void TheMountsTable_IsPickedAgain_WhenItsSkeletonChanges()
        {
            var runner = VanillaGame.Load();
            var sim = Open(runner);
            PickRiderTable(runner, sim, "humanoid01", VanillaGame.KnightTable);
            sim.Mount.Data.TriggerSkeletonChanged();
            Assert.That(sim.Mount.FragAndSlotSelection.FragmentList.SelectedItem?.FullPath, Is.EqualTo(VanillaGame.WarhorseTable));
        }
    }
}
