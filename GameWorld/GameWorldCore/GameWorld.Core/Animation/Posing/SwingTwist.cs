using System;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Animation.Posing
{
    /// <summary>
    /// Splits a bone's rotation into a twist about the bone's own axis and a swing that tilts that axis. Joint limits are kept in
    /// these terms because per-axis Euler ranges wrap and gimbal. Rotations here are a bone's change from its bind rotation,
    /// <c>delta = inverse(bind) * local</c>, so <c>local = bind * delta</c> and the delta acts in the bone's own frame.
    /// </summary>
    public static class SwingTwist
    {
        /// <summary>
        /// The bone's axis in its own frame: toward its first child, else along its offset from its parent, else +X.
        /// </summary>
        public static Vector3 BoneAxis(GameSkeleton skeleton, int bone)
        {
            var children = skeleton.GetDirectChildBones(bone);
            foreach (var child in children)
            {
                if (skeleton.Translation[child].LengthSquared() > 1e-12f)
                    return Vector3.Normalize(skeleton.Translation[child]);
            }

            // The bone's own offset is in its parent's frame; bring it into the bone's.
            var own = Vector3.Transform(skeleton.Translation[bone], Quaternion.Inverse(skeleton.Rotation[bone]));
            return own.LengthSquared() > 1e-12f ? Vector3.Normalize(own) : Vector3.UnitX;
        }

        /// <summary>Two unit vectors perpendicular to the axis and to each other, the same every time for the same axis.</summary>
        public static (Vector3 B, Vector3 C) Basis(Vector3 axis)
        {
            var helper = MathF.Abs(axis.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
            var b = Vector3.Normalize(Vector3.Cross(axis, helper));
            var c = Vector3.Cross(axis, b);
            return (b, c);
        }

        /// <summary>
        /// <paramref name="delta"/> = swing * twist: the twist about <paramref name="axis"/>, in (-pi, pi], is applied first. The swing is
        /// its axis-angle vector, which is perpendicular to the axis, projected onto <see cref="Basis"/>.
        /// </summary>
        public static void Decompose(Quaternion delta, Vector3 axis, out float twist, out Vector2 swing)
        {
            delta = Quaternion.Normalize(delta);
            var along = delta.X * axis.X + delta.Y * axis.Y + delta.Z * axis.Z;
            var twistQuat = new Quaternion(axis * along, delta.W);
            if (twistQuat.LengthSquared() < 1e-12f)
            {
                // A half turn of swing: no twist can be told apart.
                twist = 0;
                twistQuat = Quaternion.Identity;
            }
            else
            {
                twistQuat = Quaternion.Normalize(twistQuat);
                twist = WrapAngle(2 * MathF.Atan2(along, delta.W));
            }

            var swingQuat = Quaternion.Normalize(delta * Quaternion.Inverse(twistQuat));
            if (swingQuat.W < 0)
                swingQuat = -swingQuat;
            var vector = new Vector3(swingQuat.X, swingQuat.Y, swingQuat.Z);
            var sin = vector.Length();
            var (b, c) = Basis(axis);
            if (sin < 1e-12f)
            {
                swing = Vector2.Zero;
                return;
            }
            var angle = 2 * MathF.Atan2(sin, swingQuat.W);
            var rotationVector = vector / sin * angle;
            swing = new Vector2(Vector3.Dot(rotationVector, b), Vector3.Dot(rotationVector, c));
        }

        public static Quaternion Compose(float twist, Vector2 swing, Vector3 axis)
        {
            var (b, c) = Basis(axis);
            var rotationVector = b * swing.X + c * swing.Y;
            var angle = rotationVector.Length();
            var swingQuat = angle < 1e-12f ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(rotationVector / angle, angle);
            return Quaternion.Normalize(swingQuat * Quaternion.CreateFromAxisAngle(axis, twist));
        }

        /// <summary>An angle brought into (-pi, pi].</summary>
        public static float WrapAngle(float angle)
        {
            angle = MathF.IEEERemainder(angle, MathF.Tau);
            return angle <= -MathF.PI ? angle + MathF.Tau : angle;
        }
    }
}
