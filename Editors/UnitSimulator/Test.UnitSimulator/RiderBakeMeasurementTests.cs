using Editors.Shared.Core.Services.RiderBake;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Test.UnitSimulator
{
    // The bake measured against CA (docs/superpowers/specs/2026-11-18-asseteditor-rider-bake-design.md, section 2): a vanilla knight
    // baked onto his own warhorse, every rider bone compared with the clip CA ships for that slot. A baker that cannot give back what
    // it was handed cannot be trusted with a new mount. The Mount Animation Creator's baker, measured the same way before this one
    // replaced it, missed by 9-23 m at its default and by 0.53-0.74 m at its best.
    public class RiderBakeMeasurementTests
    {
        internal const string WarhorseModel = @"variantmeshes\variantmeshdefinitions\emp_warhorse.variantmeshdefinition";
        internal static readonly string[] Slots = ["JUMPING_ATTACK_1", "RUN_2", "CHARGE"];

        internal record Setup(IPackFileService Pfs, AnimationBinWh3 RiderTable, AnimationBinWh3 MountTable,
            GameSkeleton RiderSkeleton, GameSkeleton MountSkeleton, Rmv2MeshNode? MountMesh, int RiderHip);

        internal static Setup Load(bool withMesh)
        {
            var runner = VanillaGame.Load(withDb: withMesh);
            var pfs = runner.PackFileService;
            Rmv2MeshNode? mesh = null;
            if (withMesh)
            {
                var sim = UnitSimulatorAppTests.Open(runner);
                PersonalityTests.SetMount(runner, sim, WarhorseModel);
                // LOD0's mesh with the most vertices: the horse's body, which the saddle is part of.
                mesh = SceneNodeHelper.GetChildrenOfType<Rmv2LodNode>(sim.Mount.Data.ModelNode).First().GetAllModels(false).MaxBy(x => x.Geometry.VertexCount());
            }
            var tables = VanillaGame.Tables(pfs);
            var rider = tables[VanillaGame.KnightTable];
            var mount = tables[VanillaGame.WarhorseTable];
            var riderSkeleton = Skeleton(pfs, rider.SkeletonName);
            return new Setup(pfs, rider, mount, riderSkeleton, Skeleton(pfs, mount.SkeletonName), mesh, riderSkeleton.GetBoneIndexByName("root"));
        }

        internal static GameSkeleton Skeleton(IPackFileService pfs, string name) => new(AnimationFile.Create(pfs.FindFile($"animations/skeletons/{name}.anim")!), null);

        internal static AnimationClip Clip(Setup s, IAnimationBinGenericFormat table, string slot, GameSkeleton skeleton) =>
            new(AnimationFile.Create(s.Pfs.FindFile(table.Entries.First(x => x.SlotName == slot).AnimationFile)!), skeleton);

        static Vector3 BoneAt(AnimationClip clip, GameSkeleton skeleton, int bone, int frame) =>
            AnimationSampler.Sample(frame, 0, skeleton, clip).GetSkeletonAnimatedWorld(skeleton, bone).Translation;

        // Every rider bone, every frame: the baked clip against CA's. Max over bones and frames, the hip's max, and the mean.
        static string Worst = "";

        static (float Max, float Hip, float Mean) Error(Setup s, AnimationClip baked, AnimationClip ca)
        {
            float max = 0, hip = 0, sum = 0;
            var n = 0;
            Assert.That(baked.DynamicFrames, Has.Count.EqualTo(ca.DynamicFrames.Count));
            for (var f = 0; f < ca.DynamicFrames.Count; f++)
            {
                for (var b = 0; b < s.RiderSkeleton.BoneCount; b++)
                {
                    var d = (BoneAt(baked, s.RiderSkeleton, b, f) - BoneAt(ca, s.RiderSkeleton, b, f)).Length();
                    if (d > max)
                        Worst = s.RiderSkeleton.BoneNames[b] + " frame " + f;
                    max = Math.Max(max, d);
                    if (b == s.RiderHip)
                        hip = Math.Max(hip, d);
                    sum += d;
                    n++;
                }
            }
            return (max, hip, sum / n);
        }

        // What a save writes: the clip as a v7 .anim, read back.
        static AnimationClip ThroughFile(AnimationClip clip, GameSkeleton skeleton) =>
            new(AnimationFile.Create(new Shared.ByteParsing.ByteChunk(AnimationFile.ConvertToBytes(clip.ConvertToFileFormat(skeleton)))), skeleton);

        // A rider moved onto a clip of another length stays in step: at every frame of the output his hip sits on the target's seat
        // where it sat on the source's at the matching moment, the two clips' ends lined up. RUN_2's rider (15 frames) onto WALK_1 (27).
        [Test]
        public void ARiderRetimedOntoALongerClip_SitsOnItsSeatAsHeSatOnTheSourcesAtTheMatchingMoment()
        {
            var s = Load(withMesh: false);
            var sourceClip = Clip(s, s.MountTable, "RUN_2", s.MountSkeleton);
            var targetClip = Clip(s, s.MountTable, "WALK_1", s.MountSkeleton);
            var riderClip = Clip(s, s.RiderTable, "RIDER_RUN_2", s.RiderSkeleton);
            var seat = RiderSeat.Bone(s.MountSkeleton, s.MountSkeleton.GetBoneIndexByName("spine_1"));
            var mount = new RiderBakeMount(s.MountSkeleton, seat);

            var baked = RiderBaker.Bake(riderClip, s.RiderSkeleton, mount, sourceClip, mount, targetClip, Matrix.Identity);
            Assert.That(baked.PlayTimeInSec, Is.EqualTo(targetClip.PlayTimeInSec).Within(1e-4f));

            var worst = 0f;
            for (var f = 0; f < baked.DynamicFrames.Count; f++)
            {
                var t = f / (float)(baked.DynamicFrames.Count - 1);
                var hipOnTarget = AnimationSampler.Sample(f, 0, s.RiderSkeleton, baked).GetSkeletonAnimatedWorld(s.RiderSkeleton, s.RiderHip) * Matrix.Invert(seat.Skin(AnimationSampler.Sample(t, s.MountSkeleton, targetClip)));
                var hipOnSource = AnimationSampler.Sample(t, s.RiderSkeleton, riderClip).GetSkeletonAnimatedWorld(s.RiderSkeleton, s.RiderHip) * Matrix.Invert(seat.Skin(AnimationSampler.Sample(t, s.MountSkeleton, sourceClip)));
                worst = Math.Max(worst, (hipOnTarget.Translation - hipOnSource.Translation).Length());
            }
            TestContext.Out.WriteLine($"{riderClip.DynamicFrames.Count} frames onto {targetClip.DynamicFrames.Count}: {baked.DynamicFrames.Count} frames, hip off its seat by at most {worst * 1000:0.0} mm");
            Assert.That(baked.DynamicFrames, Has.Count.EqualTo(targetClip.DynamicFrames.Count));
            Assert.That(worst, Is.LessThan(0.01f));
        }

        // The math alone: seated on the bone CA's rider sits over, on his own horse, the bake must be the identity, and so must the
        // animroot. Fails on a matrix order swapped, a seat inverted on the wrong side, or the horse's animroot copied over the rider's.
        [Test]
        public void AKnightBakedOntoHisOwnHorsesSeatBone_ComesBackAsCaMadeHim()
        {
            var s = Load(withMesh: false);
            foreach (var slot in Slots)
            {
                var mountClip = Clip(s, s.MountTable, slot, s.MountSkeleton);
                var riderClip = Clip(s, s.RiderTable, "RIDER_" + slot, s.RiderSkeleton);
                var seat = RiderSeat.NearestBoneBelow(s.MountSkeleton, mountClip, BoneAt(riderClip, s.RiderSkeleton, s.RiderHip, 0));
                var mount = new RiderBakeMount(s.MountSkeleton, RiderSeat.Bone(s.MountSkeleton, seat));

                var baked = RiderBaker.Bake(riderClip, s.RiderSkeleton, mount, mountClip, mount, mountClip, Matrix.Identity);
                var (max, hip, mean) = Error(s, baked, riderClip);
                var (fileMax, _, _) = Error(s, ThroughFile(baked, s.RiderSkeleton), riderClip);
                TestContext.Out.WriteLine($"{slot}: seat {s.MountSkeleton.BoneNames[seat]}, max {max * 1000:0.00} mm, hip {hip * 1000:0.00} mm, mean {mean * 1000:0.000} mm, worst {Worst}; through a v7 file max {fileMax * 1000:0.00} mm");
                Assert.That(max, Is.LessThan(0.001f), slot);
                Assert.That(fileMax, Is.LessThan(0.002f), slot);
            }
        }

        // The case the simulator offers: the rider seated on a vertex of the horse's mesh (a picked saddle point), not the bone his
        // clip was made against. What is left is how differently the vertex and the bone move, and that is the number reported.
        [Test]
        public void AKnightMovedFromHisSeatBoneToASaddleVertex_StaysWithinHalfAMetre()
        {
            var s = Load(withMesh: true);
            var helper = new MeshAnimationHelper(s.MountMesh!, Matrix.Identity);
            foreach (var slot in Slots)
            {
                var mountClip = Clip(s, s.MountTable, slot, s.MountSkeleton);
                var riderClip = Clip(s, s.RiderTable, "RIDER_" + slot, s.RiderSkeleton);
                var hip0 = BoneAt(riderClip, s.RiderSkeleton, s.RiderHip, 0);
                var seatBone = RiderSeat.NearestBoneBelow(s.MountSkeleton, mountClip, hip0);
                var frame0 = AnimationSampler.Sample(0, 0, s.MountSkeleton, mountClip);
                var vertex = Enumerable.Range(0, s.MountMesh!.Geometry.VertexCount()).MinBy(v => (helper.GetVertexTransformWorld(frame0, v).Translation - hip0).LengthSquared());

                var source = new RiderBakeMount(s.MountSkeleton, RiderSeat.Bone(s.MountSkeleton, seatBone));
                var target = new RiderBakeMount(s.MountSkeleton, RiderSeat.Vertex(s.MountMesh, vertex));
                var baked = RiderBaker.Bake(riderClip, s.RiderSkeleton, source, mountClip, target, mountClip, Matrix.Identity);
                var (max, hip, mean) = Error(s, baked, riderClip);
                var v = s.MountMesh.Geometry.GetVertexExtented(vertex);
                var weights = string.Join(", ", new[] { (v.BlendIndices.X, v.BlendWeights.X), (v.BlendIndices.Y, v.BlendWeights.Y), (v.BlendIndices.Z, v.BlendWeights.Z), (v.BlendIndices.W, v.BlendWeights.W) }
                    .Where(x => x.Item2 > 0).Select(x => $"{s.MountSkeleton.BoneNames[(int)x.Item1]} {x.Item2:0.00}"));
                TestContext.Out.WriteLine($"{slot}: {s.MountSkeleton.BoneNames[seatBone]} to vertex {vertex} ({weights}), max {max:0.000} m, hip {hip:0.000} m, mean {mean:0.000} m");
                Assert.That(max, Is.LessThan(0.5f), slot);
            }
        }
    }
}
