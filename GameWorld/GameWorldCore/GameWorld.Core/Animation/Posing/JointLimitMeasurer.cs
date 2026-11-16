using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Animation.Posing
{
    /// <summary>
    /// Joint limits measured from clips: each bone may turn as far as the clips ever turn it, plus a margin. CA's skeletons store
    /// no limits, so CA's own clips are the best record of how far each joint is meant to bend.
    /// </summary>
    public static class JointLimitMeasurer
    {
        /// <summary>A bone whose twist and swing never span more than this, in radians, is treated as never moving.</summary>
        public const float StillEpsilon = 1e-4f;

        // Twist angles go into buckets round the circle, each keeping the least and greatest angle it saw, so the largest
        // empty arc is found exactly without keeping every frame of every clip.
        const int TwistBuckets = 3600;

        public static JointLimits Measure(GameSkeleton skeleton, IEnumerable<AnimationClip> clips, float? marginRadians = null, CancellationToken cancel = default)
        {
            var margin = marginRadians ?? MathHelper.ToRadians(5);
            var boneCount = skeleton.BoneCount;
            var axes = new Vector3[boneCount];
            var bindInverse = new Quaternion[boneCount];
            var bones = new BoneRange[boneCount];
            for (var bone = 0; bone < boneCount; bone++)
            {
                axes[bone] = SwingTwist.BoneAxis(skeleton, bone);
                bindInverse[bone] = Quaternion.Inverse(skeleton.Rotation[bone]);
                bones[bone] = new BoneRange();
            }

            foreach (var clip in clips)
            {
                cancel.ThrowIfCancellationRequested();
                if (clip.DynamicFrames.Count == 0 || clip.DynamicFrames[0].Rotation.Count != boneCount)
                    continue;

                foreach (var frame in clip.DynamicFrames)
                {
                    if (frame.Rotation.Count != boneCount)
                        continue;
                    for (var bone = 0; bone < boneCount; bone++)
                    {
                        SwingTwist.Decompose(bindInverse[bone] * frame.Rotation[bone], axes[bone], out var twist, out var swing);
                        bones[bone].Add(twist, swing);
                    }
                }
            }
            cancel.ThrowIfCancellationRequested();

            var limits = new Dictionary<int, BoneLimit>();
            for (var bone = 0; bone < boneCount; bone++)
            {
                var limit = bones[bone].ToLimit(margin);
                if (limit != null)
                    limits[bone] = limit;
            }
            return new JointLimits(skeleton.SkeletonName, limits);
        }

        sealed class BoneRange
        {
            readonly float[] _bucketMin = new float[TwistBuckets];
            readonly float[] _bucketMax = new float[TwistBuckets];
            Vector2 _swingMin = new(float.MaxValue);
            Vector2 _swingMax = new(float.MinValue);
            bool _any;

            public BoneRange()
            {
                Array.Fill(_bucketMin, float.NaN);
                Array.Fill(_bucketMax, float.NaN);
            }

            public void Add(float twist, Vector2 swing)
            {
                _any = true;
                _swingMin = Vector2.Min(_swingMin, swing);
                _swingMax = Vector2.Max(_swingMax, swing);

                var bucket = Math.Clamp((int)((twist + MathF.PI) / MathF.Tau * TwistBuckets), 0, TwistBuckets - 1);
                if (float.IsNaN(_bucketMin[bucket]) || twist < _bucketMin[bucket]) _bucketMin[bucket] = twist;
                if (float.IsNaN(_bucketMax[bucket]) || twist > _bucketMax[bucket]) _bucketMax[bucket] = twist;
            }

            public BoneLimit? ToLimit(float margin)
            {
                if (!_any)
                    return null;

                // The largest empty arc between one occupied bucket's greatest angle and the next one's least, the wrap included.
                var occupied = new List<int>();
                for (var i = 0; i < TwistBuckets; i++)
                {
                    if (!float.IsNaN(_bucketMin[i]))
                        occupied.Add(i);
                }

                var gap = -1f;
                float rangeStart = 0;
                for (var k = 0; k < occupied.Count; k++)
                {
                    var here = occupied[k];
                    var next = occupied[(k + 1) % occupied.Count];
                    var arc = _bucketMin[next] - _bucketMax[here];
                    if (k == occupied.Count - 1)
                        arc += MathF.Tau;
                    if (arc > gap)
                    {
                        gap = arc;
                        rangeStart = _bucketMin[next];
                    }
                }

                var width = MathF.Tau - gap;
                var twistCenter = SwingTwist.WrapAngle(rangeStart + width / 2);
                var swingSpan = _swingMax - _swingMin;
                if (width <= StillEpsilon && swingSpan.X <= StillEpsilon && swingSpan.Y <= StillEpsilon)
                    return null;

                var halfWidth = MathF.Min(MathF.PI, width / 2 + margin);
                return new BoneLimit(twistCenter, halfWidth, _swingMin - new Vector2(margin), _swingMax + new Vector2(margin));
            }
        }
    }
}
