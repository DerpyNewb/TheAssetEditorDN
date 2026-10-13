using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationPack;

namespace Test.UnitSimulator
{
    // The simulator's working assumption (spec section 3), measured before anything is built on it: the game seats a rider by
    // playing its animation from the mount's origin, with no bone joining the two. Held against a vanilla knight on his warhorse.
    public class SeatingTests
    {
        // Metres. A rider leans and rises to strike; one that is not seated this way misses by a metre or more.
        const float AllowedDrift = 0.25f;
        const int Samples = 60;

        [Test]
        public void AVanillaKnight_StaysInTheSaddle_WhenBothPlayFromOneOrigin()
        {
            var pfs = VanillaGame.Load().PackFileService;
            var tables = VanillaGame.Tables(pfs);
            IAnimationBinGenericFormat rider = tables[VanillaGame.KnightTable];
            IAnimationBinGenericFormat mount = tables[VanillaGame.WarhorseTable];
            Assert.That(rider.MountBin, Is.EqualTo(mount.Name));

            var riderSkeleton = Skeleton(pfs, rider.SkeletonName);
            var mountSkeleton = Skeleton(pfs, mount.SkeletonName);
            // humanoid01 has no pelvis bone: root is the hip, and the Mount Animation Creator's default rider bone.
            var riderHip = riderSkeleton.GetBoneIndexByName("root");
            var mountRoot = mountSkeleton.GetBoneIndexByName("root");
            Assert.That(riderHip, Is.Not.EqualTo(-1));
            Assert.That(mountRoot, Is.Not.EqualTo(-1));

            // The table has no RUN (locomotion is not a slot), so the three pairs whose mount travels farthest. Not the DEATH_ slots:
            // in those the rider is animated falling off.
            var pairs = mount.Entries
                .Where(m => !m.SlotName.StartsWith("DEATH_", StringComparison.Ordinal))
                .GroupBy(m => m.SlotName).Select(g => g.First())
                .Select(m => (Mount: m, Rider: rider.Entries.FirstOrDefault(r => r.SlotName == "RIDER_" + m.SlotName)))
                .Where(x => x.Rider != null && Exists(pfs, x.Mount.AnimationFile) && Exists(pfs, x.Rider.AnimationFile))
                .Select(x => (Slot: x.Mount.SlotName, MountClip: Clip(pfs, x.Mount.AnimationFile, mountSkeleton), RiderClip: Clip(pfs, x.Rider!.AnimationFile, riderSkeleton)))
                .OrderByDescending(x => Travel(x.MountClip, mountSkeleton, mountRoot))
                .Take(3)
                .ToList();
            Assert.That(pairs, Has.Count.EqualTo(3));

            foreach (var (slot, mountClip, riderClip) in pairs)
            {
                var duration = Math.Min(mountClip.PlayTimeInSec, riderClip.PlayTimeInSec);
                var hipAtStart = World(riderClip, riderSkeleton, riderHip, 0).Translation;
                var seat = SeatBone(mountClip, mountSkeleton, hipAtStart);
                // In the seat bone's own frame: the bone pitches as the horse moves, and a hip riding on it moves with it.
                var hipOnSeatAtStart = Vector3.Transform(hipAtStart, Matrix.Invert(World(mountClip, mountSkeleton, seat, 0)));

                var drift = 0f;
                var lowest = float.MaxValue;
                for (var i = 0; i <= Samples; i++)
                {
                    var t = duration * i / Samples;
                    var hip = World(riderClip, riderSkeleton, riderHip, t).Translation;
                    var seatNow = World(mountClip, mountSkeleton, seat, t);
                    drift = Math.Max(drift, (Vector3.Transform(hip, Matrix.Invert(seatNow)) - hipOnSeatAtStart).Length());
                    lowest = Math.Min(lowest, hip.Y - seatNow.Translation.Y);
                }

                TestContext.Out.WriteLine($"{slot}: seat bone {mountSkeleton.BoneNames[seat]}, drift {drift:0.000} m, hip at least {lowest:0.000} m above it, {duration:0.00} s");
                Assert.That(drift, Is.LessThan(AllowedDrift), slot);
                Assert.That(lowest, Is.GreaterThan(0f), slot);
            }
        }

        static GameSkeleton Skeleton(IPackFileService pfs, string name) => new(AnimationFile.Create(pfs.FindFile($"animations/skeletons/{name}.anim")!), null);

        static bool Exists(IPackFileService pfs, string path) => !string.IsNullOrWhiteSpace(path) && pfs.FindFile(path) != null;

        static AnimationClip Clip(IPackFileService pfs, string path, GameSkeleton skeleton) => new(AnimationFile.Create(pfs.FindFile(path)!), skeleton);

        // A bone's world transform at a time in seconds, sampled as AnimationPlayer.Refresh samples it.
        static Matrix World(AnimationClip clip, GameSkeleton skeleton, int bone, float seconds) =>
            AnimationSampler.Sample(clip.PlayTimeInSec == 0 ? 0 : seconds / clip.PlayTimeInSec, skeleton, clip).GetSkeletonAnimatedWorld(skeleton, bone);

        // The mount bone nearest the hip at frame 0 among those below it: what the rider sits on.
        static int SeatBone(AnimationClip clip, GameSkeleton skeleton, Vector3 hip)
        {
            var bones = Enumerable.Range(0, skeleton.BoneCount).Select(b => World(clip, skeleton, b, 0).Translation).ToArray();
            return Enumerable.Range(0, bones.Length).Where(b => bones[b].Y < hip.Y).MinBy(b => (bones[b] - hip).LengthSquared());
        }

        static float Travel(AnimationClip clip, GameSkeleton skeleton, int bone)
        {
            var total = 0f;
            for (var i = 1; i <= Samples; i++)
                total += (World(clip, skeleton, bone, clip.PlayTimeInSec * i / Samples).Translation - World(clip, skeleton, bone, clip.PlayTimeInSec * (i - 1) / Samples).Translation).Length();
            return total;
        }
    }
}
