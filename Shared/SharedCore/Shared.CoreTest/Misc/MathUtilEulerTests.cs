using Microsoft.Xna.Framework;
using Shared.Core.Misc;

namespace Shared.CoreTest.Misc
{
    // The X, Y and Z rotation boxes of the meta editor and the Skeleton editor go through these two. Box X turned about Y, Y about
    // Z and Z about X, and 180 in any box divided by zero and wrote NaN.
    public class MathUtilEulerTests
    {
        static readonly Vector3[] Axes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
        static readonly float[] Steps = [-180, -135, -90, -45, 0, 45, 90, 135, 180];

        // The middle turn of the three is about Z, so a Z near +-90 is where X and Y start turning about one axis.
        static readonly float[] NearStraightUp = [-90, -89.9f, -88, -87, 87, 88, 89.9f, 90];

        [Test]
        public void EachBoxAlone_TurnsAboutItsOwnAxis([Values(0, 1, 2)] int box, [Values(30, 90, -45, 180, -180)] float degrees)
        {
            var boxes = Vector3.Zero;
            boxes.SetAxis(box, degrees);

            var q = MathUtil.EulerDegreesToQuaternion(boxes);

            AssertSameRotation(Quaternion.CreateFromAxisAngle(Axes[box], MathHelper.ToRadians(degrees)), q, $"box {box} at {degrees}");
            // Z, the middle turn, reads back between -90 and 90: Z at 180 is the same rotation as X and Y at 180, and shows as that.
            if (box != 2 || Math.Abs(degrees) <= 90)
                Assert.That(Math.IEEERemainder(MathUtil.QuaternionToEulerDegree(q).GetAxis(box) - degrees, 360), Is.EqualTo(0).Within(1e-3),
                    "and reads back in the same box");
        }

        [Test]
        public void EveryAngle_GivesAFiniteUnitQuaternion_AndReadsBackAsTheSameRotation()
        {
            foreach (var x in Steps)
                foreach (var y in Steps)
                    foreach (var z in Steps.Concat(NearStraightUp))
                    {
                        var q = MathUtil.EulerDegreesToQuaternion(new Vector3(x, y, z));
                        var at = $"({x}, {y}, {z})";

                        Assert.That(float.IsFinite(q.X) && float.IsFinite(q.Y) && float.IsFinite(q.Z) && float.IsFinite(q.W), Is.True, at);
                        Assert.That(q.Length(), Is.EqualTo(1).Within(1e-5), at);
                        Assert.That(q.W, Is.GreaterThanOrEqualTo(0), $"{at}: w kept as the file has always had it");
                        AssertSameRotation(q, MathUtil.EulerDegreesToQuaternion(MathUtil.QuaternionToEulerDegree(q)), $"{at} read back");
                    }
        }

        // Built by the old EulerToQuaternions from heading 30 (about Y), attitude 20 (about Z) and bank 10 (about X); the old
        // QuaternionToEulerDegree read it as (30, 20, 10).
        static readonly Quaternion OldThirtyTwentyTen = new(0.127679441f, 0.268535823f, 0.144878125f, 0.943714364f);

        [Test]
        public void ASavedRotation_ShowsTheSameAngles_EachInItsOwnAxisBox()
        {
            var shown = MathUtil.QuaternionToEulerDegree(OldThirtyTwentyTen);

            Assert.That(shown.X, Is.EqualTo(10).Within(1e-3), "about X");
            Assert.That(shown.Y, Is.EqualTo(30).Within(1e-3), "about Y");
            Assert.That(shown.Z, Is.EqualTo(20).Within(1e-3), "about Z");

            var written = MathUtil.EulerDegreesToQuaternion(shown);
            Assert.That(new[] { written.X, written.Y, written.Z, written.W },
                Is.EqualTo(new[] { OldThirtyTwentyTen.X, OldThirtyTwentyTen.Y, OldThirtyTwentyTen.Z, OldThirtyTwentyTen.W }).Within(1e-6),
                "and writes back the numbers it was read from");
        }

        static void AssertSameRotation(Quaternion expected, Quaternion actual, string because)
        {
            foreach (var v in new[] { Vector3.UnitX, Vector3.UnitY })
            {
                var distance = Vector3.Distance(Vector3.Transform(v, expected), Vector3.Transform(v, actual));
                Assert.That(distance, Is.LessThan(1e-4), $"{because}: {v} lands {distance} apart");
            }
        }
    }

    static class VectorAxis
    {
        public static float GetAxis(this Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

        public static void SetAxis(ref this Vector3 v, int axis, float value)
        {
            if (axis == 0) v.X = value;
            else if (axis == 1) v.Y = value;
            else v.Z = value;
        }
    }
}
