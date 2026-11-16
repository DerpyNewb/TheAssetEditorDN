using GameWorld.Core.Animation.Posing;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Test.Animation.Posing
{
    public class SwingTwistTests
    {
        static readonly Vector3[] Axes = [Vector3.UnitX, Vector3.UnitY, Vector3.Normalize(new Vector3(0.3f, -0.8f, 0.5f))];

        static Quaternion RandomRotation(Random random)
        {
            var q = new Quaternion((float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1);
            return Quaternion.Normalize(q);
        }

        [Test]
        public void Decompose_ThenCompose_ReturnsTheSameRotation()
        {
            var random = new Random(7);
            foreach (var axis in Axes)
            {
                for (var i = 0; i < 200; i++)
                {
                    var q = RandomRotation(random);
                    SwingTwist.Decompose(q, axis, out var twist, out var swing);
                    var back = SwingTwist.Compose(twist, swing, axis);
                    Assert.That(TestSkeletons.AngleBetween(back, q), Is.LessThan(1e-4f), $"axis {axis}, rotation {q}");
                    Assert.That(twist, Is.InRange(-MathF.PI, MathF.PI));
                }
            }
        }

        [Test]
        public void PureTwist_HasNoSwing()
        {
            foreach (var axis in Axes)
            {
                SwingTwist.Decompose(Quaternion.CreateFromAxisAngle(axis, 1.2f), axis, out var twist, out var swing);
                Assert.That(twist, Is.EqualTo(1.2f).Within(1e-5f));
                Assert.That(swing.Length(), Is.LessThan(1e-5f));
            }
        }

        [Test]
        public void PureSwing_HasNoTwist()
        {
            foreach (var axis in Axes)
            {
                var (b, _) = SwingTwist.Basis(axis);
                SwingTwist.Decompose(Quaternion.CreateFromAxisAngle(b, 0.7f), axis, out var twist, out var swing);
                Assert.That(twist, Is.EqualTo(0).Within(1e-5f));
                Assert.That(swing.X, Is.EqualTo(0.7f).Within(1e-5f));
                Assert.That(swing.Y, Is.EqualTo(0).Within(1e-5f));
            }
        }

        [Test]
        public void Basis_IsPerpendicularAndUnit()
        {
            foreach (var axis in Axes)
            {
                var (b, c) = SwingTwist.Basis(axis);
                Assert.That(Vector3.Dot(axis, b), Is.EqualTo(0).Within(1e-6f));
                Assert.That(Vector3.Dot(axis, c), Is.EqualTo(0).Within(1e-6f));
                Assert.That(Vector3.Dot(b, c), Is.EqualTo(0).Within(1e-6f));
                Assert.That((b.Length(), c.Length()), Is.EqualTo((1f, 1f)).Using<(float, float)>((x, y) => MathF.Abs(x.Item1 - y.Item1) < 1e-6f && MathF.Abs(x.Item2 - y.Item2) < 1e-6f));
            }
        }

        // local = bind * delta must mean the delta acts in the bone's own frame, the way WorldTransform composes keys: a twist
        // about the bone's axis then leaves its child exactly where it was.
        [Test]
        public void TwistAboutTheBoneAxis_LeavesTheChildInPlace()
        {
            var bind = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(0.2f, 0.4f, 1)), 0.9f);
            var skeleton = TestSkeletons.Build(new("root", -1, Vector3.Zero), new("bone", 0, Vector3.UnitY, bind), new("child", 1, new Vector3(1, 0.5f, 0)));
            var frame = TestSkeletons.BindFrame(skeleton);
            var before = TestSkeletons.At(frame, skeleton, 2);
            var axis = SwingTwist.BoneAxis(skeleton, 1);

            frame.Rotation[1] = bind * Quaternion.CreateFromAxisAngle(axis, 1.0f);

            Assert.That(Vector3.Distance(TestSkeletons.At(frame, skeleton, 2), before), Is.LessThan(1e-5f));
            SwingTwist.Decompose(Quaternion.Inverse(bind) * frame.Rotation[1], axis, out var twist, out var swing);
            Assert.That(twist, Is.EqualTo(1.0f).Within(1e-5f));
            Assert.That(swing.Length(), Is.LessThan(1e-5f));
        }

        [Test]
        public void BoneAxis_PointsAtTheFirstChild_OrAlongTheLeafInItsOwnFrame()
        {
            var skeleton = TestSkeletons.Arm();
            Assert.That(SwingTwist.BoneAxis(skeleton, TestSkeletons.Upper), Is.EqualTo(Vector3.UnitX));

            // A leaf whose bind is turned 90 degrees about Z: its offset from the parent, +X in the parent's frame, is -Y in its own.
            var turned = TestSkeletons.Build(new("root", -1, Vector3.Zero), new("leaf", 0, Vector3.UnitX, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)));
            var axis = SwingTwist.BoneAxis(turned, 1);
            Assert.That(Vector3.Distance(axis, -Vector3.UnitY), Is.LessThan(1e-5f), axis.ToString());
        }
    }
}
