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
                turned.AddRange(TransformBoneCommand.SolveInverseKinematics(frame, skeleton, pin.Bone, pin.EndBone, pin.Target, limits, draggedBones));
            }
            return turned.Distinct().ToList();
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
