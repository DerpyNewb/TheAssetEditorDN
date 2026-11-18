using Editors.AnimationVisualEditors.AnimationKeyframeEditor;
using GameWorld.Core.Animation;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.ToolCreation;

namespace Test.UnitSimulator
{
    // "Open in KeyFrame Tool" on CA's knight and warhorse with the bake previewing. The mount has a mesh and the rider only a
    // skeleton, so both ways a part is loaded are used.
    public class KeyFrameHandoffTests
    {
        [Test]
        public void TheKeyFrameTool_GetsBothParts_TheirTablesAndSlots_AndTheBakedClip()
        {
            var runner = VanillaGame.Load(withDb: true);
            var sim = UnitSimulatorAppTests.Open(runner);
            PersonalityTests.SetMount(runner, sim, RiderBakeMeasurementTests.WarhorseModel);
            UnitSimulatorAppTests.PickTable(runner, sim, sim.Rider, "humanoid01", VanillaGame.KnightTable);
            UnitSimulatorAppTests.PickTable(runner, sim, sim.Mount, "horse01", VanillaGame.WarhorseTable);
            UnitSimulatorAppTests.PickMountSlot(sim, "RUN_2");
            sim.Bake.Preview = true;
            Assert.That(sim.Rider.Data.AnimationName.Value, Does.StartWith("baked"), sim.Bake.Status);

            sim.OpenInKeyFrameToolCommand.Execute(null);

            var keyFrame = runner.ServiceProvider.GetRequiredService<IEditorManager>().GetAllEditors().OfType<AnimationKeyframeEditorViewModel>().Single();
            var rider = keyFrame.SceneObjects.First(x => x.Data == keyFrame.Rider);
            var mount = keyFrame.SceneObjects.First(x => x.Data == keyFrame.Mount);

            Assert.That(mount.Data.MeshFile, Is.Not.Null.And.SameAs(sim.Mount.Data.MeshFile));
            Assert.That(mount.Data.ModelNode, Is.Not.Null, "the mount's mesh is in the KeyFrame Tool's scene");
            Assert.That(rider.Data.MeshFile, Is.Null, "the rider had no mesh");
            Assert.That((rider.Data.SkeletonName.Value, mount.Data.SkeletonName.Value), Is.EqualTo((sim.Rider.Data.SkeletonName.Value, sim.Mount.Data.SkeletonName.Value)));
            Assert.That((rider.FragAndSlotSelection.FragmentList.SelectedItem?.FullPath, mount.FragAndSlotSelection.FragmentList.SelectedItem?.FullPath),
                Is.EqualTo((VanillaGame.KnightTable, VanillaGame.WarhorseTable)));
            Assert.That((rider.FragAndSlotSelection.FragmentSlotList.SelectedItem?.SlotName, mount.FragAndSlotSelection.FragmentSlotList.SelectedItem?.SlotName),
                Is.EqualTo((sim.Rider.FragAndSlotSelection.FragmentSlotList.SelectedItem!.SlotName, "RUN_2")));

            // The baked clip, not the slot's, and a copy: posing it in the KeyFrame Tool leaves the simulator's alone.
            var baked = sim.Rider.Data.AnimationClip;
            var copied = rider.Data.AnimationClip;
            Assert.That(rider.Data.AnimationName.Value, Is.EqualTo(sim.Rider.Data.AnimationName.Value));
            Assert.That(copied, Is.Not.Null.And.Not.SameAs(baked));
            Assert.That(copied.DynamicFrames, Has.Count.EqualTo(baked.DynamicFrames.Count));
            var skeleton = rider.Data.Skeleton;
            for (var frame = 0; frame < baked.DynamicFrames.Count; frame += 5)
            {
                var hip = RiderBakePanelHip(baked, skeleton, frame);
                Assert.That((RiderBakePanelHip(copied, skeleton, frame) - hip).Length(), Is.LessThan(1e-4f), $"frame {frame}");
            }
        }

        static Microsoft.Xna.Framework.Vector3 RiderBakePanelHip(AnimationClip clip, GameSkeleton skeleton, int frame) =>
            AnimationSampler.Sample(frame, 0, skeleton, clip).GetSkeletonAnimatedWorld(skeleton, Editors.UnitSimulator.Bake.RiderBakePanel.Hip(skeleton)).Translation;
    }
}
