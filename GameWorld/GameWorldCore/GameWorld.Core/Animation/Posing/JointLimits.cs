using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Animation.Posing
{
    /// <summary>
    /// How far a bone may turn from its bind rotation, in <see cref="SwingTwist"/> terms. Twist is a circular range, a centre and a
    /// half-width, so a range across +-pi needs no special case; a half-width of pi or more leaves twist free. Swing is a box over
    /// the swing vector's two components.
    /// </summary>
    public sealed record BoneLimit(float TwistCenter, float TwistHalfWidth, Vector2 SwingMin, Vector2 SwingMax)
    {
        public bool TwistIsFree => TwistHalfWidth >= MathF.PI;
    }

    /// <summary>The joint limits of one skeleton, by bone index. A bone with no entry is free.</summary>
    public sealed class JointLimits
    {
        readonly IReadOnlyDictionary<int, BoneLimit> _limits;

        public JointLimits(string skeletonName, IReadOnlyDictionary<int, BoneLimit> limits)
        {
            SkeletonName = skeletonName;
            _limits = limits;
        }

        public string SkeletonName { get; }
        public IReadOnlyDictionary<int, BoneLimit> Limits => _limits;

        public bool TryGet(int bone, out BoneLimit limit) => _limits.TryGetValue(bone, out limit!);

        /// <summary><paramref name="local"/> moved to the nearest rotation inside the bone's range; unchanged for a free bone.</summary>
        public Quaternion Clamp(GameSkeleton skeleton, int bone, Quaternion local)
        {
            if (!TryGet(bone, out var limit))
                return local;

            var bind = skeleton.Rotation[bone];
            var axis = SwingTwist.BoneAxis(skeleton, bone);
            SwingTwist.Decompose(Quaternion.Inverse(bind) * local, axis, out var twist, out var swing);

            var clampedTwist = twist;
            if (!limit.TwistIsFree)
            {
                var offset = SwingTwist.WrapAngle(twist - limit.TwistCenter);
                clampedTwist = SwingTwist.WrapAngle(limit.TwistCenter + Math.Clamp(offset, -limit.TwistHalfWidth, limit.TwistHalfWidth));
            }
            var clampedSwing = Vector2.Clamp(swing, limit.SwingMin, limit.SwingMax);
            if (clampedTwist == twist && clampedSwing == swing)
                return local;

            return Quaternion.Normalize(bind * SwingTwist.Compose(clampedTwist, clampedSwing, axis));
        }

        /// <summary>
        /// Whether <paramref name="local"/> is inside the bone's range, judged on its twist and swing. Not Clamp(q) == q: Clamp's
        /// recentring round trip is not exact in floats, so a quarter of poses well inside an off-centre range read as outside.
        /// </summary>
        public bool Contains(GameSkeleton skeleton, int bone, Quaternion local, float epsilon = 1e-5f)
        {
            if (!TryGet(bone, out var limit))
                return true;
            SwingTwist.Decompose(Quaternion.Inverse(skeleton.Rotation[bone]) * local, SwingTwist.BoneAxis(skeleton, bone), out var twist, out var swing);
            var twistInside = limit.TwistIsFree || MathF.Abs(SwingTwist.WrapAngle(twist - limit.TwistCenter)) <= limit.TwistHalfWidth + epsilon;
            return twistInside
                && swing.X >= limit.SwingMin.X - epsilon && swing.X <= limit.SwingMax.X + epsilon
                && swing.Y >= limit.SwingMin.Y - epsilon && swing.Y <= limit.SwingMax.Y + epsilon;
        }

        /// <summary>A copy with the bone's limit replaced, or removed (the bone made free) when <paramref name="limit"/> is null.</summary>
        public JointLimits WithOverride(int bone, BoneLimit? limit)
        {
            var copy = new Dictionary<int, BoneLimit>(_limits);
            if (limit == null)
                copy.Remove(bone);
            else
                copy[bone] = limit;
            return new JointLimits(SkeletonName, copy);
        }
    }
}
