using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;

namespace Editors.UnitSimulator.Personalities
{
    /// <summary>An attach bone: its parent on the mount's animation skeleton (-1 for none), and its local matrix there, which no
    /// animation moves.</summary>
    public record AttachBone(string Name, int Parent, Matrix Local);

    /// <summary>A mount's attach bones. A tech skeleton is the animation skeleton with the ap_ bones appended (spec section 2, measured
    /// by TechSkeletonTests), so an ap_ bone's place on a frame is its local matrix times its parent's animated world matrix.</summary>
    public static class AttachBones
    {
        /// <summary>As the other FromTech, from the file: a tech folder also holds destruction animations, proxies and turret parts
        /// (TechSkeletonTests 1a), refused on their bone table before their frames are read, which need not hold one per bone.</summary>
        public static (List<AttachBone> Bones, string? Problem) FromTech(GameSkeleton animation, AnimationFile tech, string techPath) =>
            Differs(animation, tech.Bones.Length, i => (tech.Bones[i].Name, tech.Bones[i].ParentId)) is { } bone
                ? ([], NotBuiltOn(techPath, animation, bone))
                : FromTech(animation, new GameSkeleton(tech, null), techPath);

        /// <summary>The tech skeleton's ap_ bones, or none and why the tech skeleton is not built on this animation skeleton.</summary>
        public static (List<AttachBone> Bones, string? Problem) FromTech(GameSkeleton animation, GameSkeleton tech, string techPath)
        {
            if (Differs(animation, tech.BoneCount, i => (tech.BoneNames[i], tech.GetParentBoneIndex(i))) is { } differs)
                return ([], NotBuiltOn(techPath, animation, differs));

            // An ap_ bone on no parent (Nagash's ap_turret) stands at its own matrix.
            var bones = new List<AttachBone>();
            for (var i = animation.BoneCount; i < tech.BoneCount; i++)
            {
                var parent = tech.GetParentBoneIndex(i);
                if (tech.BoneNames[i].StartsWith("ap_", StringComparison.Ordinal) && parent >= -1 && parent < animation.BoneCount)
                    bones.Add(new AttachBone(tech.BoneNames[i], parent, Local(tech, i)));
            }
            return (bones, null);
        }

        /// <summary>The animation skeleton's own ap_ bones, if it has any: they move with it.</summary>
        public static List<AttachBone> FromAnimation(GameSkeleton animation) =>
            Enumerable.Range(0, animation.BoneCount)
                .Where(i => animation.BoneNames[i].StartsWith("ap_", StringComparison.Ordinal))
                .Select(i => new AttachBone(animation.BoneNames[i], i, Matrix.Identity))
                .ToList();

        public static Matrix World(AttachBone bone, GameSkeleton mount) =>
            bone.Parent < 0 ? bone.Local : bone.Local * mount.GetAnimatedWorldTranform(bone.Parent);

        // The first of the animation skeleton's bones the tech file does not have, by name and parent at its index, or null.
        static int? Differs(GameSkeleton animation, int techCount, Func<int, (string Name, int Parent)> techBone)
        {
            for (var i = 0; i < animation.BoneCount; i++)
                if (i >= techCount || techBone(i) != (animation.BoneNames[i], animation.GetParentBoneIndex(i)))
                    return i;
            return null;
        }

        static string NotBuiltOn(string techPath, GameSkeleton animation, int bone) =>
            $"{techPath} is not the mount's skeleton {animation.SkeletonName} with attach bones added: bone {bone} differs.";

        // As GameSkeleton.RebuildSkeletonMatrix builds a bone's matrix before its parent's is applied.
        static Matrix Local(GameSkeleton skeleton, int bone) =>
            Matrix.CreateScale(skeleton.Scale[bone]) * Matrix.CreateFromQuaternion(skeleton.Rotation[bone]) * Matrix.CreateTranslation(skeleton.Translation[bone]);
    }
}
