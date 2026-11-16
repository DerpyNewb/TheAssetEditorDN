using GameWorld.Core.Animation.Posing;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Test.Animation.Posing
{
    public class JointLimitsTests
    {
        const int Bone = TestSkeletons.Upper;   // its axis is +X, toward the lower arm

        static Quaternion Local(float twist, Vector2 swing) => SwingTwist.Compose(twist, swing, Vector3.UnitX);   // bind is identity

        static (float Twist, Vector2 Swing) Split(Quaternion local)
        {
            SwingTwist.Decompose(local, Vector3.UnitX, out var twist, out var swing);
            return (twist, swing);
        }

        static JointLimits Limits(BoneLimit limit) => new("test_skeleton", new Dictionary<int, BoneLimit> { [Bone] = limit });

        [Test]
        public void Clamp_LeavesARotationInsideItsRange()
        {
            var limits = Limits(new BoneLimit(0, 0.5f, new Vector2(-0.3f), new Vector2(0.3f)));
            var local = Local(0.2f, new Vector2(0.1f, -0.2f));

            var clamped = limits.Clamp(TestSkeletons.Arm(), Bone, local);

            Assert.That(TestSkeletons.AngleBetween(clamped, local), Is.LessThan(1e-4f));
        }

        [Test]
        public void Clamp_PullsTwistAndSwingBackToTheEdges()
        {
            var limits = Limits(new BoneLimit(0, 0.5f, new Vector2(-0.3f), new Vector2(0.3f)));

            var (twist, swing) = Split(limits.Clamp(TestSkeletons.Arm(), Bone, Local(0.9f, new Vector2(0.6f, 0))));

            Assert.That(twist, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(swing.X, Is.EqualTo(0.3f).Within(1e-4f));
            Assert.That(swing.Y, Is.EqualTo(0).Within(1e-4f));
        }

        [Test]
        public void Clamp_WrapsTwistAroundPi()
        {
            var limits = Limits(new BoneLimit(MathF.PI, 0.2f, new Vector2(-1), new Vector2(1)));
            var skeleton = TestSkeletons.Arm();

            var (inside, _) = Split(limits.Clamp(skeleton, Bone, Local(-3.0f, Vector2.Zero)));
            var (outside, _) = Split(limits.Clamp(skeleton, Bone, Local(2.5f, Vector2.Zero)));

            Assert.That(inside, Is.EqualTo(-3.0f).Within(1e-4f), "-3.0 is 0.14 from pi the short way round");
            Assert.That(outside, Is.EqualTo(MathF.PI - 0.2f).Within(1e-4f));
        }

        [Test]
        public void Clamp_MeasuresFromTheBindRotation()
        {
            var bind = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.6f);
            var skeleton = TestSkeletons.Build(new("root", -1, Vector3.Zero), new("bone", 0, Vector3.UnitX, bind), new("child", 1, Vector3.UnitX));
            var limits = new JointLimits("test_skeleton", new Dictionary<int, BoneLimit> { [1] = new BoneLimit(0, 0.1f, new Vector2(-0.1f), new Vector2(0.1f)) });

            var clamped = limits.Clamp(skeleton, 1, bind);

            Assert.That(TestSkeletons.AngleBetween(clamped, bind), Is.LessThan(1e-4f), "the bind pose itself is inside every range");
        }

        [Test]
        public void UnlimitedBone_IsUntouched()
        {
            var limits = Limits(new BoneLimit(0, 0.1f, new Vector2(-0.1f), new Vector2(0.1f)));
            var local = Local(2f, new Vector2(1f, 1f));

            Assert.That(limits.Clamp(TestSkeletons.Arm(), TestSkeletons.Lower, local), Is.EqualTo(local));
            Assert.That(limits.TryGet(TestSkeletons.Lower, out _), Is.False);
        }

        [Test]
        public void FreeTwist_IsNotClamped()
        {
            var limits = Limits(new BoneLimit(0, MathF.PI, new Vector2(-1), new Vector2(1)));

            var (twist, _) = Split(limits.Clamp(TestSkeletons.Arm(), Bone, Local(3f, Vector2.Zero)));

            Assert.That(twist, Is.EqualTo(3f).Within(1e-4f));
        }

        [Test]
        public void WithOverride_Null_FreesTheBone()
        {
            var limits = Limits(new BoneLimit(0, 0.1f, new Vector2(-0.1f), new Vector2(0.1f)));
            var local = Local(1f, Vector2.Zero);

            var freed = limits.WithOverride(Bone, null);

            Assert.That(freed.TryGet(Bone, out _), Is.False);
            Assert.That(freed.Clamp(TestSkeletons.Arm(), Bone, local), Is.EqualTo(local));
            Assert.That(limits.TryGet(Bone, out _), Is.True, "the original is unchanged");
        }
    }
}
