using System;
using GameWorld.Core.Commands.Bone;
using Microsoft.Xna.Framework;
using static GameWorld.Core.Animation.AnimationClip;

namespace GameWorld.Core.Animation.Posing
{
    /// <summary>
    /// Turns a limb about the line from its top joint to its end, which moves the elbow or knee round that line and leaves the
    /// hand or foot where it is. The limb is its own: the end bone's parent and grandparent, never the IK end-bone picker's
    /// chain, which defaults to animroot and would swing the whole body.
    /// </summary>
    public static class LimbSwivel
    {
        const int LimitSearchSteps = 20;

        /// <summary>The limb above <paramref name="effector"/>: false when it has no grandparent, or the grandparent is a root bone.</summary>
        public static bool TryGetChain(GameSkeleton skeleton, int effector, out int middle, out int top)
        {
            middle = skeleton.GetParentBoneIndex(effector);
            top = middle == -1 ? -1 : skeleton.GetParentBoneIndex(middle);
            return top != -1 && skeleton.GetParentBoneIndex(top) != -1;
        }

        /// <summary>
        /// Turns the top bone by <paramref name="angleRadians"/> about the top-to-effector line and returns the angle applied. With
        /// limits, it stops where the top bone would leave its range; a pose already outside the range is swivelled unlimited.
        /// </summary>
        public static float Apply(KeyFrame frame, GameSkeleton skeleton, int effector, float angleRadians, JointLimits? limits)
        {
            if (!TryGetChain(skeleton, effector, out _, out var top))
                return 0;

            var start = frame.Rotation[top];
            var topPosition = TransformBoneCommand.WorldTransform(frame, skeleton, top).Translation;
            var line = TransformBoneCommand.WorldTransform(frame, skeleton, effector).Translation - topPosition;
            if (line.LengthSquared() < 1e-12f)
                return 0;
            line.Normalize();

            Quaternion Turned(float angle)
            {
                frame.Rotation[top] = start;
                return TransformBoneCommand.TurnInModelSpace(frame, skeleton, top, Matrix.CreateFromAxisAngle(line, angle));
            }

            bool Inside(Quaternion rotation) => limits == null || IsInside(limits, skeleton, top, rotation);

            var applied = angleRadians;
            var full = Turned(angleRadians);
            if (!Inside(full) && Inside(start))
            {
                // The largest angle of the same sign that stays inside.
                float lo = 0, hi = angleRadians;
                for (var i = 0; i < LimitSearchSteps; i++)
                {
                    var mid = (lo + hi) / 2;
                    if (Inside(Turned(mid))) lo = mid; else hi = mid;
                }
                applied = lo;
                full = Turned(lo);
            }

            frame.Rotation[top] = full;
            return applied;
        }

        // Clamp hands back the very rotation it was given when nothing needed clamping, so equality is exact here.
        static bool IsInside(JointLimits limits, GameSkeleton skeleton, int bone, Quaternion rotation) => limits.Clamp(skeleton, bone, rotation) == rotation;
    }
}
