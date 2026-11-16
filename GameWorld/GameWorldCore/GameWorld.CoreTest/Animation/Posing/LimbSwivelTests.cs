using GameWorld.Core.Animation;
using GameWorld.Core.Animation.Posing;
using GameWorld.Core.Commands.Bone;
using Microsoft.Xna.Framework;
using static GameWorld.Core.Test.Animation.Posing.TestSkeletons;

namespace GameWorld.Core.Test.Animation.Posing
{
    public class LimbSwivelTests
    {
        /// <summary>The arm with its elbow bent 0.8 rad about Z, so the swivel line is not the upper arm's own axis.</summary>
        static (GameSkeleton Skeleton, AnimationClip.KeyFrame Frame) BentArm()
        {
            var skeleton = Arm();
            var frame = BindFrame(skeleton);
            frame.Rotation[Lower] = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.8f);
            return (skeleton, frame);
        }

        [Test]
        public void Swivel_KeepsTheEffectorWhereItWas()
        {
            var (skeleton, frame) = BentArm();
            var hand = At(frame, skeleton, Hand);

            var applied = LimbSwivel.Apply(frame, skeleton, Hand, 1.0f, null);

            Assert.That(applied, Is.EqualTo(1.0f));
            Assert.That(Vector3.Distance(At(frame, skeleton, Hand), hand), Is.LessThan(1e-4f));
        }

        [Test]
        public void Swivel_MovesTheMiddleJoint()
        {
            var (skeleton, frame) = BentArm();
            var elbow = At(frame, skeleton, Lower);

            LimbSwivel.Apply(frame, skeleton, Hand, 1.0f, null);

            Assert.That(Vector3.Distance(At(frame, skeleton, Lower), elbow), Is.GreaterThan(0.1f));
        }

        [Test]
        public void Swivel_TurnsOnlyTheTopBone()
        {
            var (skeleton, frame) = BentArm();
            var before = frame.Clone();

            LimbSwivel.Apply(frame, skeleton, Hand, 1.0f, null);

            for (var bone = 0; bone < skeleton.BoneCount; bone++)
            {
                if (bone != Upper)
                    Assert.That(frame.Rotation[bone], Is.EqualTo(before.Rotation[bone]), $"bone {bone}");
            }
            Assert.That(frame.Position, Is.EqualTo(before.Position));
        }

        [Test]
        public void Swivel_StopsAtTheLimit()
        {
            var (skeleton, frame) = BentArm();
            var limits = new JointLimits("test_arm", new Dictionary<int, BoneLimit> { [Upper] = new BoneLimit(0, 0.2f, new Vector2(-3), new Vector2(3)) });
            var hand = At(frame, skeleton, Hand);

            var applied = LimbSwivel.Apply(frame, skeleton, Hand, 1.0f, limits);

            Assert.That(applied, Is.GreaterThan(0).And.LessThan(1.0f));
            SwingTwist.Decompose(frame.Rotation[Upper], SwingTwist.BoneAxis(skeleton, Upper), out var twist, out _);
            Assert.That(twist, Is.EqualTo(0.2f).Within(1e-3f), "it stops at the edge, not short of it");
            Assert.That(Vector3.Distance(At(frame, skeleton, Hand), hand), Is.LessThan(1e-4f), "a stopped swivel still keeps the hand");
        }

        [Test]
        public void Swivel_FromAPoseAlreadyOutsideItsLimit_IgnoresTheLimit()
        {
            var (skeleton, frame) = BentArm();
            frame.Rotation[Upper] = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f);
            var limits = new JointLimits("test_arm", new Dictionary<int, BoneLimit> { [Upper] = new BoneLimit(0, 0.2f, new Vector2(-3), new Vector2(3)) });

            Assert.That(LimbSwivel.Apply(frame, skeleton, Hand, 0.3f, limits), Is.EqualTo(0.3f));
        }

        [Test]
        public void TryGetChain_NeedsAGrandparentWithAParent()
        {
            var skeleton = Arm();

            Assert.That(LimbSwivel.TryGetChain(skeleton, Hand, out var middle, out var top), Is.True);
            Assert.That((middle, top), Is.EqualTo((Lower, Upper)));
            Assert.That(LimbSwivel.TryGetChain(skeleton, Upper, out _, out _), Is.False, "its grandparent is the root");
            Assert.That(LimbSwivel.TryGetChain(skeleton, Pelvis, out _, out _), Is.False);
        }

        [Test]
        public void ReplaceFrames_UndoAndRedo()
        {
            var skeleton = Arm();
            var clip = new AnimationClip();
            for (var i = 0; i < 3; i++)
                clip.DynamicFrames.Add(BindFrame(skeleton));
            var original = clip.DynamicFrames.Select(x => x.Clone()).ToList();
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);
            var command = new ReplaceFramesCommand();
            command.Configure(clip, [0, 2], c => { c.DynamicFrames[0].Rotation[Lower] = turn; c.DynamicFrames[2].Rotation[Upper] = turn; }, "test");

            command.Execute();
            Assert.That((clip.DynamicFrames[0].Rotation[Lower], clip.DynamicFrames[2].Rotation[Upper]), Is.EqualTo((turn, turn)));

            command.Undo();
            for (var i = 0; i < 3; i++)
                Assert.That(clip.DynamicFrames[i].Rotation, Is.EqualTo(original[i].Rotation), $"frame {i}");

            command.Redo();
            Assert.That((clip.DynamicFrames[0].Rotation[Lower], clip.DynamicFrames[2].Rotation[Upper]), Is.EqualTo((turn, turn)));
            Assert.That(clip.DynamicFrames[1].Rotation, Is.EqualTo(original[1].Rotation));
        }
    }
}
