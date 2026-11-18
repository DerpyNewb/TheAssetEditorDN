using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;

namespace Editors.Shared.Core.Services.RiderBake
{
    /// <summary>The point on a mount a rider follows: one bone, or a mesh vertex skinned by up to four. Skin gives its skinning
    /// matrix in a frame, the move from the mount's bind pose to that frame, which is identity in the bind pose.</summary>
    public record RiderSeat(string Name, Func<AnimationFrame, Matrix> Skin)
    {
        public static RiderSeat Bone(GameSkeleton skeleton, int bone) => new(skeleton.BoneNames[bone], f => f.BoneTransforms[bone].WorldTransform);

        public static RiderSeat Vertex(Rmv2MeshNode mesh, int vertex)
        {
            var helper = new MeshAnimationHelper(mesh, Matrix.Identity);
            return new($"vertex {vertex}", f => helper.GetVertexTransform(f, vertex));
        }

        /// <summary>The mount bone nearest the rider's hip among those below it: what the rider sits on.</summary>
        public static int NearestBoneBelow(GameSkeleton mount, AnimationClip mountClip, Vector3 hip)
        {
            var frame = AnimationSampler.Sample(0, 0, mount, mountClip);
            return Enumerable.Range(0, mount.BoneCount)
                .Select(b => (Bone: b, At: frame.GetSkeletonAnimatedWorld(mount, b).Translation))
                .Where(x => x.At.Y < hip.Y)
                .MinBy(x => (x.At - hip).LengthSquared()).Bone;
        }
    }

    /// <summary>A mount for the bake: its skeleton and the seat the rider follows on it.</summary>
    public record RiderBakeMount(GameSkeleton Skeleton, RiderSeat Seat);

    /// <summary>Moves a rider clip from the mount it was made on to another. The rider keeps every move it makes against the source
    /// mount's seat (a lean, a rise to strike), and the seat's motion is swapped for the target's: the rider is taken back to the
    /// source mount's bind pose through the source seat and skinned forward by the target seat. A clip baked onto the mount and seat
    /// it was made on comes back as it was. docs/superpowers/specs/2026-11-18-asseteditor-rider-bake-design.md.</summary>
    public static class RiderBaker
    {
        // The Mount Animation Creator's list: slots a rider keeps as they are instead of having them fitted to the mount.
        static readonly string[] s_copyOnlyPrefixes = ["HAND_POSE_", "DOCK_", "PERSISTENT_METADATA_", "PORTHOLE_", "RIDER_CAST_SPELL_", "RIDER_MOVING_ATTACK_", "RIDER_CELEBRATE_", "RIDER_FIRE_"];
        static readonly HashSet<string> s_copyOnlyNames = ["STAND", "MISSING_ANIM", "RIDER_SHOOT_READY", "RIDER_COMBAT_READY", "RIDER_RELOAD",
            "RIDER_STAND_TO_RIDER_COMBAT_READY", "RIDER_COMBAT_READY_TO_RIDER_STAND", "RIDER_SHOOT_READY_TO_RIDER_STAND",
            "RIDER_STAND_TO_RIDER_SHOOT_READY", "RIDER_SHOOT_READY_TO_RIDER_RELOAD", "RIDER_RELOAD_TO_RIDER_SHOOT_READY"];

        public static bool IsCopyOnly(string slot) => s_copyOnlyNames.Contains(slot) || s_copyOnlyPrefixes.Any(slot.StartsWith);

        /// <summary>The offset in the target mount's bind pose: metres, then degrees as yaw, pitch and roll.</summary>
        public static Matrix Offset(Vector3 translation, Vector3 rotationDegrees) =>
            Matrix.CreateFromYawPitchRoll(MathHelper.ToRadians(rotationDegrees.X), MathHelper.ToRadians(rotationDegrees.Y), MathHelper.ToRadians(rotationDegrees.Z)) * Matrix.CreateTranslation(translation);

        /// <param name="source">The mount the rider clip was made on, playing <paramref name="sourceClip"/>; null for a clip made on foot,
        /// which is then placed as it stands in the target mount's bind pose.</param>
        /// <returns>The rider clip, retimed by the target clip's length over the source's, so a rider in step with the mount it was made on
        /// stays in step with the new one (CA ships some pairs of unequal length, the knight's REFUSE_1 at 29 frames on 33, and those
        /// keep their offset). A clip from foot is fitted to the target's length. Its animroot moves from the source mount's animroot to
        /// the target's (WH3 rider clips carry the mount's locomotion there), and every bone under the animroot moves with the seat.</returns>
        public static AnimationClip Bake(AnimationClip riderClip, GameSkeleton riderSkeleton, RiderBakeMount? source, AnimationClip? sourceClip,
            RiderBakeMount target, AnimationClip targetClip, Matrix offset)
        {
            if (source == null)
                sourceClip = null;
            var ratio = targetClip.PlayTimeInSec / (sourceClip?.PlayTimeInSec ?? riderClip.PlayTimeInSec);
            var rider = riderClip.Clone();
            if (Math.Abs(ratio - 1) > 1e-4f)
            {
                var count = Math.Max(2, (int)Math.Round((riderClip.DynamicFrames.Count - 1) * ratio) + 1);
                rider = GameWorld.Core.Animation.AnimationEditor.ReSample(riderSkeleton, riderClip, count, riderClip.PlayTimeInSec * ratio);
            }
            var frames = rider.DynamicFrames.Count;
            var output = rider.Clone();
            var underRoot = riderSkeleton.GetDirectChildBones(0);

            for (var f = 0; f < frames; f++)
            {
                // Seconds into the output, and each mount sampled at that moment of its own clip (the source's slowed by the ratio).
                var seconds = frames <= 1 ? 0 : rider.PlayTimeInSec * f / (frames - 1);
                var sourceFrame = sourceClip == null ? null : AnimationSampler.Sample(At(seconds / ratio, sourceClip), source!.Skeleton, sourceClip);
                var targetFrame = AnimationSampler.Sample(At(seconds, targetClip), target.Skeleton, targetClip);
                var move = (sourceFrame == null ? Matrix.Identity : Matrix.Invert(source!.Seat.Skin(sourceFrame))) * offset * target.Seat.Skin(targetFrame);
                var riderFrame = AnimationSampler.Sample(f, 0, riderSkeleton, rider);

                // The animroot keeps its own offset from the mount's: CA's knight's runs up to 0.735 m from his horse's mid-clip, though
                // both travel the clip's 11.13 m.
                var sourceRoot = sourceFrame == null ? Matrix.Identity : sourceFrame.GetSkeletonAnimatedWorld(source!.Skeleton, 0);
                var root = riderFrame.GetSkeletonAnimatedWorld(riderSkeleton, 0) * Matrix.Invert(sourceRoot) * targetFrame.GetSkeletonAnimatedWorld(target.Skeleton, 0);
                root.Decompose(out _, out var rootRotation, out var rootPosition);
                var key = output.DynamicFrames[f];
                key.Position[0] = rootPosition;
                key.Rotation[0] = Quaternion.Normalize(rootRotation);
                var toRoot = Matrix.Invert(Matrix.CreateFromQuaternion(key.Rotation[0]) * Matrix.CreateTranslation(key.Position[0]));

                foreach (var bone in underRoot)
                {
                    (riderFrame.GetSkeletonAnimatedWorld(riderSkeleton, bone) * move * toRoot).Decompose(out _, out var rotation, out var position);
                    key.Position[bone] = position;
                    key.Rotation[bone] = Quaternion.Normalize(rotation);
                }
            }
            return output;
        }

        // A moment in seconds as a clip's 0-1, held at its last frame past its end.
        static float At(float seconds, AnimationClip clip) => clip.PlayTimeInSec <= 0 ? 0 : Math.Clamp(seconds / clip.PlayTimeInSec, 0, 1);
    }
}
