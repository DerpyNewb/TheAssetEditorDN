using System;
using System.Collections.Generic;
using System.Linq;
using GameWorld.Core.Commands.Bone;
using Microsoft.Xna.Framework;
using static GameWorld.Core.Animation.AnimationClip;

namespace GameWorld.Core.Animation.Posing
{
    /// <summary>
    /// Holds a bone's model-space position while other bones are edited, by turning its own short chain (up to
    /// <see cref="EndBone"/>). Position only: a pinned foot can still tilt.
    /// </summary>
    public sealed record Pin(int Bone, int EndBone, Vector3 Target)
    {
        /// <summary>A pin where the bone is now, its chain running up to its grandparent (a foot: shin and thigh), else its parent.</summary>
        public static Pin Create(KeyFrame frame, GameSkeleton skeleton, int bone)
        {
            var parent = skeleton.GetParentBoneIndex(bone);
            var grandparent = parent == -1 ? -1 : skeleton.GetParentBoneIndex(parent);
            return new Pin(bone, grandparent != -1 ? grandparent : parent, TransformBoneCommand.WorldTransform(frame, skeleton, bone).Translation);
        }
    }

    public static class PinSolver
    {
        /// <summary>
        /// Moves each pinned bone back to its target with limited IK on its own chain. A pin on a dragged bone gives way, and no
        /// pin turns a dragged bone. Returns every bone it turned.
        /// </summary>
        public static IReadOnlyList<int> Resolve(KeyFrame frame, GameSkeleton skeleton, IReadOnlyList<Pin> pins, IReadOnlyCollection<int> draggedBones, JointLimits? limits)
        {
            var turned = new List<int>();
            foreach (var pin in pins)
            {
                if (draggedBones.Contains(pin.Bone) || pin.Bone >= frame.Rotation.Count)
                    continue;
                if (Vector3.Distance(TransformBoneCommand.WorldTransform(frame, skeleton, pin.Bone).Translation, pin.Target) < 1e-6f)
                    continue;
                var chain = TransformBoneCommand.InverseKinematicsChain(skeleton, pin.Bone, pin.EndBone);
                chain.RemoveAll(draggedBones.Contains);
                if (chain.Count == 2 && chain[0] == skeleton.GetParentBoneIndex(pin.Bone) && chain[1] == skeleton.GetParentBoneIndex(chain[0]))
                {
                    SolveTwoBones(frame, skeleton, pin.Bone, chain[0], chain[1], pin.Target);
                    // The exact answer may break a joint limit; CCD then gets as close as the limits allow.
                    if (limits == null)
                    {
                        turned.AddRange(chain);
                        continue;
                    }
                }
                turned.AddRange(TransformBoneCommand.SolveInverseKinematics(frame, skeleton, pin.Bone, pin.EndBone, pin.Target, limits, draggedBones));
            }
            return turned.Distinct().ToList();
        }

        /// <summary>
        /// Exact IK for a two-bone limb: bend the middle joint in its current bend plane until the limb is as long as the target is
        /// far, then aim the top bone at the target. CCD crawls when the limb is nearly straight, as CA's idle legs are.
        /// </summary>
        static void SolveTwoBones(KeyFrame frame, GameSkeleton skeleton, int effector, int middle, int top, Vector3 target)
        {
            Vector3 At(int bone) => TransformBoneCommand.WorldTransform(frame, skeleton, bone).Translation;

            var (u, m, e) = (At(top), At(middle), At(effector));
            float a = Vector3.Distance(u, m), b = Vector3.Distance(m, e);
            if (a < 1e-6f || b < 1e-6f)
                return;
            var reach = Math.Clamp(Vector3.Distance(u, target), MathF.Abs(a - b) + 1e-5f, a + b - 1e-5f);

            // 1. The interior angle at the middle joint that makes the limb exactly that long.
            var toTop = u - m;
            var toEnd = e - m;
            var plane = Vector3.Cross(toTop, toEnd);
            if (plane.LengthSquared() < 1e-12f)
                plane = SwingTwist.Basis(Vector3.Normalize(toTop)).B;   // dead straight: any plane through the limb
            plane.Normalize();
            var current = MathF.Atan2(Vector3.Dot(Vector3.Cross(toTop, toEnd), plane), Vector3.Dot(toTop, toEnd));
            var wanted = MathF.Acos(Math.Clamp((a * a + b * b - reach * reach) / (2 * a * b), -1, 1));
            frame.Rotation[middle] = TransformBoneCommand.TurnInModelSpace(frame, skeleton, middle, Matrix.CreateFromAxisAngle(plane, wanted - current));

            // 2. Aim the top bone so the end lands on the target.
            var toEffector = At(effector) - u;
            var toTarget = target - u;
            var axis = Vector3.Cross(toEffector, toTarget);
            if (axis.LengthSquared() < 1e-12f)
                return;
            var angle = MathF.Atan2(axis.Length(), Vector3.Dot(toEffector, toTarget));
            frame.Rotation[top] = TransformBoneCommand.TurnInModelSpace(frame, skeleton, top, Matrix.CreateFromAxisAngle(Vector3.Normalize(axis), angle));
        }

        /// <summary>Holds the pinned bone on every frame from A to B (either order) where it is on frame A.</summary>
        public static void HoldAcross(AnimationClip clip, GameSkeleton skeleton, Pin pin, int frameA, int frameB, JointLimits? limits)
        {
            var held = pin with { Target = TransformBoneCommand.WorldTransform(clip.DynamicFrames[frameA], skeleton, pin.Bone).Translation };
            for (var i = Math.Min(frameA, frameB); i <= Math.Max(frameA, frameB); i++)
                Resolve(clip.DynamicFrames[i], skeleton, [held], [], limits);
        }
    }
}
