using Editors.Shared.Core.Common;
using Editors.Shared.Core.Services.RiderBake;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationPack;
using Test.TestingUtility.Shared;

namespace Test.UnitSimulator
{
    // Sub-project 2 measured before it is built (docs/superpowers/specs/2026-11-18-asseteditor-rider-bake-design.md): a vanilla knight
    // baked onto his own warhorse, compared bone by bone with the rider clip CA ships for that slot. A baker that cannot give back
    // what it was handed cannot be trusted with a new mount.
    public class RiderBakeMeasurementTests
    {
        internal const string WarhorseModel = @"variantmeshes\variantmeshdefinitions\emp_warhorse.variantmeshdefinition";
        internal static readonly string[] Slots = ["JUMPING_ATTACK_1", "RUN_2", "CHARGE"];

        internal record Setup(IPackFileService Pfs, IAnimationBinGenericFormat RiderTable, IAnimationBinGenericFormat MountTable,
            GameSkeleton RiderSkeleton, GameSkeleton MountSkeleton, Rmv2MeshNode MountMesh, int RiderHip);

        internal static Setup Load()
        {
            var runner = VanillaGame.Load(withDb: true);
            var sim = UnitSimulatorAppTests.Open(runner);
            PersonalityTests.SetMount(runner, sim, WarhorseModel);
            var pfs = runner.PackFileService;
            var tables = VanillaGame.Tables(pfs);
            var rider = tables[VanillaGame.KnightTable];
            var mount = tables[VanillaGame.WarhorseTable];
            var riderSkeleton = Skeleton(pfs, rider.SkeletonName);
            var mountSkeleton = Skeleton(pfs, mount.SkeletonName);
            // LOD0's mesh with the most vertices: the horse's body, which the saddle is part of.
            var mesh = SceneNodeHelper.GetChildrenOfType<Rmv2LodNode>(sim.Mount.Data.ModelNode).First()
                .GetAllModels(false).MaxBy(x => x.Geometry.VertexCount())!;
            return new Setup(pfs, rider, mount, riderSkeleton, mountSkeleton, mesh, riderSkeleton.GetBoneIndexByName("root"));
        }

        internal static GameSkeleton Skeleton(IPackFileService pfs, string name) => new(AnimationFile.Create(pfs.FindFile($"animations/skeletons/{name}.anim")!), null);

        internal static AnimationClip Clip(Setup s, IAnimationBinGenericFormat table, string slot, GameSkeleton skeleton) =>
            new(AnimationFile.Create(s.Pfs.FindFile(table.Entries.First(x => x.SlotName == slot).AnimationFile)!), skeleton);

        internal static Vector3 BoneAt(AnimationClip clip, GameSkeleton skeleton, int bone, int frame) =>
            AnimationSampler.Sample(frame, 0, skeleton, clip).GetSkeletonAnimatedWorld(skeleton, bone).Translation;

        // The vertex of the mount mesh nearest the rider's hip at frame 0, where the mount stands in its slot's first frame.
        internal static int SeatVertex(Setup s, AnimationClip mountClip, Vector3 hip)
        {
            var helper = new MeshAnimationHelper(s.MountMesh, Matrix.Identity);
            var frame = AnimationSampler.Sample(0, 0, s.MountSkeleton, mountClip);
            return Enumerable.Range(0, s.MountMesh.Geometry.VertexCount())
                .MinBy(v => (helper.GetVertexTransformWorld(frame, v).Translation - hip).LengthSquared());
        }

        // Every rider bone, every frame: the baked clip against CA's. Max over bones and frames, the hip's max, and the mean.
        internal static (float Max, float Hip, float Mean) Error(Setup s, AnimationClip baked, AnimationClip ca)
        {
            float max = 0, hip = 0, sum = 0;
            var n = 0;
            var frames = Math.Min(baked.DynamicFrames.Count, ca.DynamicFrames.Count);
            for (var f = 0; f < frames; f++)
            {
                for (var b = 0; b < s.RiderSkeleton.BoneCount; b++)
                {
                    var d = (BoneAt(baked, s.RiderSkeleton, b, f) - BoneAt(ca, s.RiderSkeleton, b, f)).Length();
                    max = Math.Max(max, d);
                    if (b == s.RiderHip)
                        hip = Math.Max(hip, d);
                    sum += d;
                    n++;
                }
            }
            return (max, hip, sum / n);
        }

        [Test]
        public void TheOldGenerator_OnAKnightsOwnHorse()
        {
            var s = Load();
            TestContext.Out.WriteLine($"rider bones 0-2: {string.Join(", ", Enumerable.Range(0, 3).Select(i => s.RiderSkeleton.BoneNames[i]))}; hip {s.RiderHip}");
            TestContext.Out.WriteLine($"mount bones 0-2: {string.Join(", ", Enumerable.Range(0, 3).Select(i => s.MountSkeleton.BoneNames[i]))}; mesh {s.MountMesh.Name}, {s.MountMesh.Geometry.VertexCount()} vertices");
            foreach (var slot in Slots)
            {
                var mountClip = Clip(s, s.MountTable, slot, s.MountSkeleton);
                var riderClip = Clip(s, s.RiderTable, "RIDER_" + slot, s.RiderSkeleton);
                var hip0 = BoneAt(riderClip, s.RiderSkeleton, s.RiderHip, 0);
                var vertex = SeatVertex(s, mountClip, hip0);
                var vertexPos0 = new MeshAnimationHelper(s.MountMesh, Matrix.Identity).GetVertexTransformWorld(AnimationSampler.Sample(0, 0, s.MountSkeleton, mountClip), vertex).Translation;
                TestContext.Out.WriteLine($"{slot}: mount {mountClip.DynamicFrames.Count} frames, rider {riderClip.DynamicFrames.Count}; seat vertex {vertex} {(vertexPos0 - hip0).Length():0.000} m from the hip; mount bone 0 travels {(BoneAt(mountClip, s.MountSkeleton, 0, mountClip.DynamicFrames.Count - 1) - BoneAt(mountClip, s.MountSkeleton, 0, 0)).Length():0.00} m, rider bone 0 {(BoneAt(riderClip, s.RiderSkeleton, 0, riderClip.DynamicFrames.Count - 1) - BoneAt(riderClip, s.RiderSkeleton, 0, 0)).Length():0.00} m");

                foreach (var keep in new[] { true, false })
                {
                    foreach (var root in new[] { false, true })
                    {
                        // The translation offset that puts the hip where CA has it at frame 0: the best the old generator can be set to.
                        var settings = new RiderBakeSettings(hip0 - vertexPos0, Vector3.Zero, KeepRiderRotation: keep, IsRootNodeAnimation: root);
                        var baked = new MountAnimationGeneratorService(settings, s.MountMesh, vertex, s.RiderHip, s.RiderSkeleton, s.MountSkeleton).GenerateMountAnimation(mountClip, riderClip);
                        var (max, hip, mean) = Error(s, baked, riderClip);
                        TestContext.Out.WriteLine($"  old generator keepRotation={keep} rootNode={root}: max {max:0.000} m, hip {hip:0.000} m, mean {mean:0.000} m");
                    }
                }
            }
        }
    }
}
