using GameWorld.Core.Animation;
using GameWorld.Core.Commands.Bone;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace GameWorld.Core.Test.Animation.Posing
{
    /// <summary>Small skeletons for the posing tests, built the way InverseKinematicsTests builds its arm.</summary>
    static class TestSkeletons
    {
        public record Bone(string Name, int Parent, Vector3 At, Quaternion? Rotation = null);

        public static GameSkeleton Build(params Bone[] bones)
        {
            var file = new AnimationFile();
            file.Header.SkeletonName = "test_skeleton";
            var bind = new AnimationFile.Frame();
            file.Bones = bones.Select((x, i) => new AnimationFile.BoneInfo { Id = i, Name = x.Name, ParentId = x.Parent }).ToArray();
            foreach (var bone in bones)
            {
                var r = bone.Rotation ?? Quaternion.Identity;
                bind.Transforms.Add(new RmvVector3(bone.At.X, bone.At.Y, bone.At.Z));
                bind.Quaternion.Add(new RmvVector4(r.X, r.Y, r.Z, r.W));
            }
            var part = new AnimationFile.AnimationPart();
            part.DynamicFrames.Add(bind);
            file.AnimationParts.Add(part);
            return new GameSkeleton(file, null!);
        }

        public const int Root = 0, Pelvis = 1, Upper = 2, Lower = 3, Hand = 4, Other = 5;

        /// <summary>root -> pelvis, 1 up -> upper arm, lower arm and hand, 1 apart along X; and a second bone on the pelvis.</summary>
        public static GameSkeleton Arm() => Build(
            new("root", AnimationFile.BoneIndexNoParent, Vector3.Zero), new("pelvis", Root, Vector3.UnitY), new("upper", Pelvis, Vector3.UnitX),
            new("lower", Upper, Vector3.UnitX), new("hand", Lower, Vector3.UnitX), new("other", Pelvis, -Vector3.UnitX));

        /// <summary>The bind pose as a key frame.</summary>
        public static AnimationClip.KeyFrame BindFrame(GameSkeleton skeleton) => new()
        {
            Position = Enumerable.Range(0, skeleton.BoneCount).Select(i => skeleton.Translation[i]).ToList(),
            Rotation = Enumerable.Range(0, skeleton.BoneCount).Select(i => skeleton.Rotation[i]).ToList(),
            Scale = Enumerable.Range(0, skeleton.BoneCount).Select(_ => Vector3.One).ToList(),
        };

        public static Vector3 At(AnimationClip.KeyFrame frame, GameSkeleton skeleton, int bone) => TransformBoneCommand.WorldTransform(frame, skeleton, bone).Translation;

        /// <summary>The angle in radians between two rotations. Not acos of the dot, which cannot resolve angles under ~1e-3 in floats.</summary>
        public static float AngleBetween(Quaternion a, Quaternion b)
        {
            var d = Quaternion.Normalize(Quaternion.Inverse(Quaternion.Normalize(a)) * Quaternion.Normalize(b));
            return 2 * MathF.Atan2(new Vector3(d.X, d.Y, d.Z).Length(), MathF.Abs(d.W));
        }
    }
}
