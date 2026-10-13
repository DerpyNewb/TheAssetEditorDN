using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.ReferenceModel;
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

        // Files first, as the spec has it: a part's skeleton (a model would give it the same), then its table. Both parts' tables are
        // picked this way: WH3 does not read a table's MountBin.
        internal static void PickTable(AssetEditorTestRunner runner, UnitSimulatorViewModel sim, SceneObjectViewModel part, string skeleton, string table)
        {
            var editor = runner.ScopeRepository.GetRequiredService<SceneObjectEditor>(sim);
            editor.SetSkeleton(part.Data, runner.PackFileService.FindFile($@"animations\skeletons\{skeleton}.anim")!);
            var tables = part.FragAndSlotSelection.FragmentList;
            tables.SelectedItem = tables.PossibleValues.First(x => x.FullPath == table);
        }

        internal static void PickMountSlot(UnitSimulatorViewModel sim, string slot)
        {
            var slots = sim.Mount.FragAndSlotSelection.FragmentSlotList;
            slots.SelectedItem = slots.PossibleValues.First(x => x.SlotName == slot);
        }

        [Test]
        public void TheKnightsTable_PicksNoMountTable_AndBothPickedByHand_Pair()
        {
            var runner = VanillaGame.Load();
            var sim = Open(runner);
            PickTable(runner, sim, sim.Rider, "humanoid01", VanillaGame.KnightTable);
            Assert.That(sim.Mount.FragAndSlotSelection.FragmentList.SelectedItem, Is.Null);
            Assert.That(sim.Mount.Data.SkeletonName.Value, Is.Empty);

            PickTable(runner, sim, sim.Mount, "horse01", VanillaGame.WarhorseTable);
            Assert.That(sim.Pairs, Has.Count.EqualTo(79));
            Assert.That(sim.Pairs.Count(x => x.Status == PairStatus.RiderSlotMissing), Is.EqualTo(16));
            Assert.That(sim.RiderOnlySlots, Has.Count.EqualTo(8));
        }

        // A made-up rider whose MountBin names a loaded mount table: nothing links.
        [Test]
        public void ARidersMountBin_LinksNothing()
        {
            var runner = VanillaGame.Load();
            MadeUpTables.AddToEditablePack(runner, "derpy_test_named.animpack",
                MadeUpTables.Table("derpy_test_named_mount", "horse01", "", ("ATTACK_1", "m.anim")),
                MadeUpTables.Table("derpy_test_named_rider", "humanoid01", "derpy_test_named_mount", ("RIDER_ATTACK_1", "r.anim")));
            var sim = Open(runner);
            PickTable(runner, sim, sim.Rider, "humanoid01", "animations/database/battle/bin/derpy_test_named_rider.bin");
            Assert.That(sim.Mount.FragAndSlotSelection.FragmentList.SelectedItem, Is.Null);
            Assert.That(sim.Pairs, Is.Empty);
        }

        [Test]
        public void TheSimulator_ShowsNoMountBin() =>
            Assert.That(typeof(UnitSimulatorViewModel).GetMembers().Select(x => x.Name).Where(x => x.Contains("MountBin") || x == "LinkStatus"), Is.Empty);

        [Test]
        public void AMountSlot_PlaysItsRiderSlot()
        {
            var runner = VanillaGame.Load();
            var sim = Open(runner);
            PickTable(runner, sim, sim.Rider, "humanoid01", VanillaGame.KnightTable);
            PickTable(runner, sim, sim.Mount, "horse01", VanillaGame.WarhorseTable);

            PickMountSlot(sim, "ATTACK_1");
            Assert.That(sim.Rider.FragAndSlotSelection.FragmentSlotList.SelectedItem?.SlotName, Is.EqualTo("RIDER_ATTACK_1"));

            sim.SelectedPair = sim.Pairs.First(x => x.MountSlot == "ATTACK_2");
            Assert.That(sim.Mount.FragAndSlotSelection.FragmentSlotList.SelectedItem?.SlotName, Is.EqualTo("ATTACK_2"));
            Assert.That(sim.Rider.FragAndSlotSelection.FragmentSlotList.SelectedItem?.SlotName, Is.EqualTo("RIDER_ATTACK_2"));
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
            PickTable(runner, sim, sim.Rider, "humanoid01", "animations/database/battle/bin/derpy_test_lame_rider.bin");
            PickTable(runner, sim, sim.Mount, "horse01", "animations/database/battle/bin/derpy_test_lame_mount.bin");
            Assert.That(sim.Pairs.Single().Status, Is.EqualTo(PairStatus.AnimMissing));

            Assert.DoesNotThrow(() => sim.SelectedPair = sim.Pairs.Single());

            Assert.That(sim.Rider.FragAndSlotSelection.FragmentSlotList.SelectedItem?.SlotName, Is.EqualTo("RIDER_ATTACK_1"));
            Assert.That(sim.Mount.Data.AnimationClip, Is.Null);
            Assert.That(sim.Rider.Data.AnimationClip, Is.Null);
        }
    }
}
