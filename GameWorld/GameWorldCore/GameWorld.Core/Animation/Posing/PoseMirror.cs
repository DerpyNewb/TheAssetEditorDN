using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using static GameWorld.Core.Animation.AnimationClip;

namespace GameWorld.Core.Animation.Posing
{
    public enum MirrorMode { Flip, LeftOntoRight, RightOntoLeft }

    /// <summary>
    /// Mirrors a pose left to right. Bones pair by name (<c>left</c> for <c>right</c>), and the mirror plane is the model axis
    /// the pairs' bind positions differ along. Each bone's change from its bind pose is what gets reflected, in model space,
    /// and laid onto its partner's bind pose, so a slightly asymmetric bind still mirrors cleanly and mirroring twice gives
    /// back the original. Bone scale is not mirrored.
    /// </summary>
    public static class PoseMirror
    {
        /// <summary>Each bone's partner; a bone with none is its own.</summary>
        public static int[] Pairs(GameSkeleton skeleton)
        {
            var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var bone = 0; bone < skeleton.BoneCount; bone++)
                byName.TryAdd(skeleton.BoneNames[bone], bone);

            var pairs = Enumerable.Range(0, skeleton.BoneCount).ToArray();
            for (var bone = 0; bone < skeleton.BoneCount; bone++)
            {
                var name = skeleton.BoneNames[bone];
                var partnerName = SwapSide(name);
                if (partnerName != null && byName.TryGetValue(partnerName, out var partner) && partner != bone
                    && string.Equals(SwapSide(skeleton.BoneNames[partner]), name, StringComparison.OrdinalIgnoreCase))
                    pairs[bone] = partner;
            }
            return pairs;
        }

        /// <summary>The model axis (0 X, 1 Y, 2 Z) the paired bones' bind positions differ along most; null when nothing pairs.</summary>
        public static int? MirrorAxis(GameSkeleton skeleton)
        {
            var pairs = Pairs(skeleton);
            var bind = ModelPose.Of(BindFrame(skeleton), skeleton);
            var spread = Vector3.Zero;
            var any = false;
            for (var bone = 0; bone < pairs.Length; bone++)
            {
                if (pairs[bone] == bone)
                    continue;
                any = true;
                var d = bind.Positions[bone] - bind.Positions[pairs[bone]];
                spread += new Vector3(MathF.Abs(d.X), MathF.Abs(d.Y), MathF.Abs(d.Z));
            }
            if (!any)
                return null;
            return spread.X >= spread.Y && spread.X >= spread.Z ? 0 : spread.Y >= spread.Z ? 1 : 2;
        }

        public static void Apply(KeyFrame frame, GameSkeleton skeleton, MirrorMode mode)
        {
            var axis = MirrorAxis(skeleton) ?? throw new InvalidOperationException("no left/right bone pairs");
            var pairs = Pairs(skeleton);
            var bind = ModelPose.Of(BindFrame(skeleton), skeleton);
            var original = ModelPose.Of(frame, skeleton);
            var source = frame.Clone();

            // Which bones change, and which bone each takes its pose from.
            var sourceOf = new int?[skeleton.BoneCount];
            for (var bone = 0; bone < skeleton.BoneCount; bone++)
            {
                var side = Side(skeleton.BoneNames[bone], pairs[bone] != bone);
                sourceOf[bone] = mode switch
                {
                    MirrorMode.Flip => pairs[bone],
                    MirrorMode.LeftOntoRight when side == 1 => pairs[bone],
                    MirrorMode.RightOntoLeft when side == -1 => pairs[bone],
                    _ => null,
                };
            }

            var rotations = new Quaternion[skeleton.BoneCount];
            foreach (var bone in ParentsFirst(skeleton))
            {
                var parent = skeleton.GetParentBoneIndex(bone);
                var parentRotation = parent == -1 ? Quaternion.Identity : rotations[parent];
                if (sourceOf[bone] is not int from)
                {
                    rotations[bone] = parentRotation * source.Rotation[bone];
                    continue;
                }

                // The source's change from bind, in model space, reflected and laid onto this bone's bind.
                var delta = original.Rotations[from] * Quaternion.Inverse(bind.Rotations[from]);
                rotations[bone] = Quaternion.Normalize(Reflect(delta, axis) * bind.Rotations[bone]);
                frame.Rotation[bone] = Quaternion.Normalize(Quaternion.Inverse(parentRotation) * rotations[bone]);

                // The source's local offset from its bind position, in model space, reflected and brought into this bone's parent.
                var fromParent = skeleton.GetParentBoneIndex(from);
                var fromParentRotation = fromParent == -1 ? Quaternion.Identity : original.Rotations[fromParent];
                var offset = Vector3.Transform(source.Position[from] - skeleton.Translation[from], fromParentRotation);
                frame.Position[bone] = skeleton.Translation[bone] + Vector3.Transform(Reflect(offset, axis), Quaternion.Inverse(parentRotation));
            }
        }

        /// <summary>The name with its first "left" swapped for "right" or the other way; null when it has neither.</summary>
        static string? SwapSide(string name)
        {
            var left = name.IndexOf("left", StringComparison.OrdinalIgnoreCase);
            var right = name.IndexOf("right", StringComparison.OrdinalIgnoreCase);
            if (left != -1 && (right == -1 || left < right))
                return name[..left] + "right" + name[(left + 4)..];
            if (right != -1)
                return name[..right] + "left" + name[(right + 5)..];
            return null;
        }

        /// <summary>-1 for a paired left bone, 1 for a paired right one, 0 otherwise.</summary>
        static int Side(string name, bool paired)
        {
            if (!paired)
                return 0;
            var left = name.IndexOf("left", StringComparison.OrdinalIgnoreCase);
            var right = name.IndexOf("right", StringComparison.OrdinalIgnoreCase);
            return left != -1 && (right == -1 || left < right) ? -1 : 1;
        }

        static Vector3 Reflect(Vector3 v, int axis) => axis switch
        {
            0 => new Vector3(-v.X, v.Y, v.Z),
            1 => new Vector3(v.X, -v.Y, v.Z),
            _ => new Vector3(v.X, v.Y, -v.Z),
        };

        /// <summary>A rotation seen in the mirror: its axis is reflected and its sense reversed, so the two parts off the axis flip.</summary>
        static Quaternion Reflect(Quaternion q, int axis) => axis switch
        {
            0 => new Quaternion(q.X, -q.Y, -q.Z, q.W),
            1 => new Quaternion(-q.X, q.Y, -q.Z, q.W),
            _ => new Quaternion(-q.X, -q.Y, q.Z, q.W),
        };

        /// <summary>Every bone after its parent, whatever the index order.</summary>
        static List<int> ParentsFirst(GameSkeleton skeleton)
        {
            var depth = new int[skeleton.BoneCount];
            for (var bone = 0; bone < skeleton.BoneCount; bone++)
            {
                for (var p = skeleton.GetParentBoneIndex(bone); p != -1; p = skeleton.GetParentBoneIndex(p))
                    depth[bone]++;
            }
            return Enumerable.Range(0, skeleton.BoneCount).OrderBy(x => depth[x]).ToList();
        }

        static KeyFrame BindFrame(GameSkeleton skeleton) => new()
        {
            Position = skeleton.Translation.ToList(),
            Rotation = skeleton.Rotation.ToList(),
            Scale = Enumerable.Repeat(Vector3.One, skeleton.BoneCount).ToList(),
        };

        /// <summary>Model-space rotations and positions of a frame, scale ignored.</summary>
        sealed record ModelPose(Quaternion[] Rotations, Vector3[] Positions)
        {
            public static ModelPose Of(KeyFrame frame, GameSkeleton skeleton)
            {
                var rotations = new Quaternion[skeleton.BoneCount];
                var positions = new Vector3[skeleton.BoneCount];
                foreach (var bone in ParentsFirst(skeleton))
                {
                    var parent = skeleton.GetParentBoneIndex(bone);
                    var parentRotation = parent == -1 ? Quaternion.Identity : rotations[parent];
                    var parentPosition = parent == -1 ? Vector3.Zero : positions[parent];
                    rotations[bone] = Quaternion.Normalize(parentRotation * frame.Rotation[bone]);
                    positions[bone] = parentPosition + Vector3.Transform(frame.Position[bone], parentRotation);
                }
                return new ModelPose(rotations, positions);
            }
        }
    }
}
