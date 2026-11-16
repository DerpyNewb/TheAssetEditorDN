using GameWorld.Core.Animation;
using GameWorld.Core.Animation.Posing;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Test.Animation.Posing
{
    public class JointLimitMeasurerTests
    {
        const int Lower = TestSkeletons.Lower;   // its axis is +X, toward the hand
        static readonly float Margin = MathHelper.ToRadians(5);

        /// <summary>A clip with one frame per twist of the lower arm, everything else at bind.</summary>
        static AnimationClip TwistClip(GameSkeleton skeleton, params float[] twists)
        {
            var clip = new AnimationClip();
            foreach (var twist in twists)
            {
                var frame = TestSkeletons.BindFrame(skeleton);
                frame.Rotation[Lower] = skeleton.Rotation[Lower] * SwingTwist.Compose(twist, Vector2.Zero, Vector3.UnitX);
                clip.DynamicFrames.Add(frame);
            }
            return clip;
        }

        [Test]
        public void KnownRange_IsMeasuredPlusTheMargin()
        {
            var skeleton = TestSkeletons.Arm();

            var limits = JointLimitMeasurer.Measure(skeleton, [TwistClip(skeleton, -0.4f, 0.1f, 0.6f)]);

            Assert.That(limits.TryGet(Lower, out var limit), Is.True);
            Assert.That(limit.TwistCenter, Is.EqualTo(0.1f).Within(1e-4f));
            Assert.That(limit.TwistHalfWidth, Is.EqualTo(0.5f + Margin).Within(1e-4f));
            Assert.That(limit.SwingMin.X, Is.EqualTo(-Margin).Within(1e-4f), "no swing was seen, so the range is the margin");
            Assert.That(limit.SwingMax.Y, Is.EqualTo(Margin).Within(1e-4f));
        }

        [Test]
        public void TwistStraddlingPi_GivesTheNarrowRange()
        {
            var skeleton = TestSkeletons.Arm();

            var limits = JointLimitMeasurer.Measure(skeleton, [TwistClip(skeleton, 3.0f, -3.0f, 3.1f)]);

            Assert.That(limits.TryGet(Lower, out var limit), Is.True);
            // 3.0 to -3.0 the short way round passes through pi: 0.283 wide, centred 3.0 + 0.1416.
            Assert.That(SwingTwist.WrapAngle(limit.TwistCenter - (3.0f + (MathF.Tau - 6.0f) / 2)), Is.EqualTo(0).Within(1e-4f));
            Assert.That(limit.TwistHalfWidth, Is.EqualTo((MathF.Tau - 6.0f) / 2 + Margin).Within(1e-4f));
        }

        [Test]
        public void TwistAllTheWayRound_IsFree()
        {
            var skeleton = TestSkeletons.Arm();
            var twists = Enumerable.Range(0, 72).Select(i => SwingTwist.WrapAngle(i * MathF.Tau / 72)).ToArray();

            var limits = JointLimitMeasurer.Measure(skeleton, [TwistClip(skeleton, twists)]);

            Assert.That(limits.TryGet(Lower, out var limit), Is.True);
            Assert.That(limit.TwistIsFree, Is.True, "the largest gap is 5 degrees, so the range plus a 5 degree margin each side covers the circle");
        }

        [Test]
        public void BoneNoClipMoves_HasNoLimit()
        {
            var skeleton = TestSkeletons.Arm();

            var limits = JointLimitMeasurer.Measure(skeleton, [TwistClip(skeleton, -0.4f, 0.6f)]);

            Assert.That(limits.TryGet(TestSkeletons.Other, out _), Is.False);
            Assert.That(limits.TryGet(TestSkeletons.Upper, out _), Is.False);
        }

        [Test]
        public void ClipWithAnotherBoneCount_IsSkipped()
        {
            var skeleton = TestSkeletons.Arm();
            var stranger = TwistClip(skeleton, 2.0f);
            foreach (var frame in stranger.DynamicFrames)
            {
                frame.Rotation.RemoveAt(frame.Rotation.Count - 1);
                frame.Position.RemoveAt(frame.Position.Count - 1);
                frame.Scale.RemoveAt(frame.Scale.Count - 1);
            }

            var limits = JointLimitMeasurer.Measure(skeleton, [TwistClip(skeleton, -0.4f, 0.6f), stranger]);

            Assert.That(limits.TryGet(Lower, out var limit), Is.True);
            Assert.That(limit.TwistCenter, Is.EqualTo(0.1f).Within(1e-4f));
        }

        [Test]
        public void Cancelled_Throws()
        {
            var skeleton = TestSkeletons.Arm();
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();

            Assert.Throws<OperationCanceledException>(() => JointLimitMeasurer.Measure(skeleton, [TwistClip(skeleton, 0.1f)], cancel: cancel.Token));
        }
    }
}
