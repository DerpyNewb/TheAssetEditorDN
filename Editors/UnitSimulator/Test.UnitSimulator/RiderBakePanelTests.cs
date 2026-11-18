using Editors.Shared.Core.Common;
using Editors.UnitSimulator;
using Editors.UnitSimulator.Bake;
using GameWorld.Core.Animation;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Test.TestingUtility.Shared;

namespace Test.UnitSimulator
{
    // The Bake pane in the simulator, on CA's knight and warhorse.
    public class RiderBakePanelTests
    {
        static (AssetEditorTestRunner Runner, UnitSimulatorViewModel Sim) KnightOnHisHorse(bool withMesh = false)
        {
            var runner = VanillaGame.Load(withDb: withMesh);
            var sim = UnitSimulatorAppTests.Open(runner);
            if (withMesh)
                PersonalityTests.SetMount(runner, sim, RiderBakeMeasurementTests.WarhorseModel);
            UnitSimulatorAppTests.PickTable(runner, sim, sim.Rider, "humanoid01", VanillaGame.KnightTable);
            UnitSimulatorAppTests.PickTable(runner, sim, sim.Mount, "horse01", VanillaGame.WarhorseTable);
            return (runner, sim);
        }

        static Vector3 Hip(AnimationClip clip, GameSkeleton skeleton, int frame) =>
            AnimationSampler.Sample(frame, 0, skeleton, clip).GetSkeletonAnimatedWorld(skeleton, RiderBakePanel.Hip(skeleton)).Translation;

        static AnimationClip CaClip(AssetEditorTestRunner runner, UnitSimulatorViewModel sim) =>
            new(AnimationFile.Create(runner.PackFileService.FindFile(sim.Rider.FragAndSlotSelection.FragmentSlotList.SelectedItem!.AnimationFile)!), sim.Rider.Data.Skeleton);

        // Made on starts at the table's MountBin, both seats at the bone the knight sits over; previewed on his own horse the rider plays
        // CA's clip, and a 0.5 m offset lifts every frame's hip by 0.5 m.
        [Test]
        public void Preview_PlaysTheBakeOnTheRider_AndTheOffsetMovesHim()
        {
            var (runner, sim) = KnightOnHisHorse();
            var bake = sim.Bake;
            Assert.That(bake.Source, Is.EqualTo("hr1_warhorse"));
            // A spine bone under the hip, found on the table's first fitted pair, and the same bone on this mount.
            Assert.That(bake.SourceBone, Does.StartWith("spine_"));
            Assert.That(bake.TargetBone, Is.EqualTo(bake.SourceBone));
            Assert.That(bake.TableName, Is.EqualTo("hu1_empire_hr1_warhorse_lance_sword_shield_on_hr1_warhorse"));

            UnitSimulatorAppTests.PickMountSlot(sim, "RUN_2");
            bake.Preview = true;
            var skeleton = sim.Rider.Data.Skeleton;
            var ca = CaClip(runner, sim);
            Assert.That(sim.Rider.Data.AnimationName.Value, Does.StartWith("baked"), bake.Status);
            Assert.That(Hip(sim.Rider.Data.AnimationClip, skeleton, 7), Is.EqualTo(Hip(ca, skeleton, 7)).Using<Vector3>((a, b) => (a - b).Length() < 0.001f));

            bake.OffsetY = 0.5f;
            for (var f = 0; f < ca.DynamicFrames.Count; f++)
                Assert.That((Hip(sim.Rider.Data.AnimationClip, skeleton, f) - Hip(ca, skeleton, f)).Length(), Is.EqualTo(0.5f).Within(0.002f), $"frame {f}");

            // Off, the rider plays the table's clip again.
            bake.Preview = false;
            Assert.That(sim.Rider.Data.AnimationName.Value, Does.Not.StartWith("baked"));
        }

        // A vertex picked in the 3D view seats the rider on it: the Pick vertex button puts the body mesh into vertex picking, and the
        // vertex clicked there is the one used.
        [Test]
        public void APickedVertex_SeatsTheRider()
        {
            var (runner, sim) = KnightOnHisHorse(withMesh: true);
            var bake = sim.Bake;
            UnitSimulatorAppTests.PickMountSlot(sim, "RUN_2");
            bake.Preview = true;
            var onBone = sim.Rider.Data.AnimationClip;

            bake.PickVertexCommand.Execute(null);
            var selection = runner.ScopeRepository.GetRequiredService<SelectionManager>(sim).GetState<VertexSelectionState>()!;
            Assert.That(selection.RenderObject, Is.InstanceOf<Rmv2MeshNode>(), bake.Status);
            selection.ModifySelection([1527], false);
            bake.UsePickedVertexCommand.Execute(null);

            Assert.That(bake.SeatOnVertex, Is.True);
            Assert.That(bake.VertexText, Does.EndWith("vertex 1527"));
            Assert.That(bake.Status, Does.Contain("vertex 1527"));
            // The vertex (75% spine_0) moves unlike spine_1, so the rider does too: RiderBakeMeasurementTests measured 5 cm at the hip on RUN_2.
            var moved = Enumerable.Range(0, onBone.DynamicFrames.Count).Max(f => (Hip(sim.Rider.Data.AnimationClip, sim.Rider.Data.Skeleton, f) - Hip(onBone, sim.Rider.Data.Skeleton, f)).Length());
            Assert.That(moved, Is.GreaterThan(0.01f));
        }

        // Bake all writes the table and its clips into the editable pack; with no editable pack it writes nothing and says so.
        [Test]
        public void BakeAll_SavesTheTableAndItsClips()
        {
            var (runner, sim) = KnightOnHisHorse();
            sim.Bake.TableName = "derpy_test_baked_knight";
            sim.Bake.BakeAll();
            Assert.That(sim.Bake.Status, Is.EqualTo("No editable pack is selected."));

            var editable = runner.CreateOutputPack();
            sim.Bake.BakeAll();
            Assert.That(sim.Bake.Status, Does.StartWith("53 clips"), sim.Bake.Status);
            var animPack = runner.PackFileService.FindFile(AnimTableEdits.OwnAnimPackPath(editable), editable)!.DataSource.ReadData();
            var table = new AnimationBinWh3("t", AnimTableEdits.ReadEntries(animPack).Single(x => x.Name.EndsWith("derpy_test_baked_knight.bin")).Data);
            var files = table.AnimationTableEntries.SelectMany(x => x.AnimationRefs).Select(x => x.AnimationFile).Where(x => x.Contains("derpy_test_baked_knight")).ToList();
            Assert.That(files, Has.Count.EqualTo(53));
            Assert.That(files.All(x => runner.PackFileService.FindFile(x, editable) != null));
            Assert.That(sim.Bake.Report, Has.Some.Contains("copy-only"));
        }
    }
}
