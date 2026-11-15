using GameWorld.Core.Animation;
using GameWorld.Core.Animation.AnimationChange;
using Microsoft.Xna.Framework;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.DB;

namespace Editors.AnimationMeta.SuperView.Visualisation.Rules
{
    // SPLICE: while the tag is on, its animation drives each bone at that bone's weight. The spliced animation runs from the
    // tag's Start Time and loops; most vanilla splices start at 0 and run the whole clip, where that and the main clock agree.
    public class SpliceRule : ILocalSpaceAnimationRule
    {
        readonly Splice_v11 _tag;
        readonly AnimationClip _clip;
        readonly GameSkeleton _skeleton;
        readonly float[] _weights;
        readonly bool _additive;
        readonly AnimationFrame? _reference;
        (float Time, AnimationFrame Pose)? _last;

        /// <param name="reference">An additive splice adds its difference from this pose; null is the bind pose.</param>
        public SpliceRule(GameSkeleton skeleton, AnimationClip clip, Splice_v11 tag, float[] weights, AnimationFrame? reference)
        {
            _skeleton = skeleton;
            _clip = clip;
            _tag = tag;
            _weights = weights;
            _additive = On(tag.Additive);
            _reference = reference;
        }

        public static bool On(string flag) => flag is not ("" or "false");

        public void TransformFrameLocalSpace(AnimationFrame frame, int boneId, float time)
        {
            if (boneId >= _weights.Length || _weights[boneId] <= 0)
                return;
            var strength = _weights[boneId] * TagWindow.Strength(_tag.StartTime, _tag.EndTime, _tag.BlendInTime, _tag.BlendOutTime, time);
            if (strength <= 0)
                return;

            var bone = frame.BoneTransforms[boneId];
            var pose = PoseAt(time).BoneTransforms[boneId];
            var (rotation, translation) = (pose.Rotation, pose.Translation);
            if (_additive)
            {
                // The bone's own pose, then the step from the reference to the spliced pose, the usual additive layering.
                var (refRotation, refTranslation) = _reference is { } r
                    ? (r.BoneTransforms[boneId].Rotation, r.BoneTransforms[boneId].Translation)
                    : (_skeleton.Rotation[boneId], _skeleton.Translation[boneId]);
                rotation = Quaternion.Concatenate(Quaternion.Concatenate(bone.Rotation, Quaternion.Inverse(refRotation)), pose.Rotation);
                translation = bone.Translation + pose.Translation - refTranslation;
            }
            bone.Rotation = Quaternion.Slerp(bone.Rotation, rotation, strength);
            bone.Translation = Vector3.Lerp(bone.Translation, translation, strength);
            bone.ComputeWorldMatrixFromComponents();
        }

        // Every bone asks for the same time in turn, so the spliced pose is sampled once per time. A blend-in before Start
        // Time shows the spliced animation's first frame.
        AnimationFrame PoseAt(float time)
        {
            if (_last is { } last && last.Time == time)
                return last.Pose;
            var length = _clip.PlayTimeInSec;
            var t = length > 0 ? Math.Max(0, time - _tag.StartTime) % length / length : 0;
            var pose = AnimationSampler.Sample(t, _skeleton, _clip);
            _last = (time, pose);
            return pose;
        }

        /// <summary>How strongly the tag drives each bone, the strongest part that reaches it. The generic bone reaches Generic
        /// Bone Depth levels counting itself, or every bone under it at 0 or -1; vanilla's capes use 4 and 8, shoulder pads and
        /// jaws 1. Bone -1 with a weight and no body part is the whole skeleton. The neck, arms, legs and weapons are the bones
        /// the skeleton's battle_skeletons row names and all under them: an arm from its shoulder node, a leg from its upper
        /// leg node, which on a horse are the front and back legs. The torso is the row's torso splice node, or its spine
        /// node, less the neck, arms and legs. Weapon Pose drives the weapon bones fully; without a row they are the be_prop
        /// bones. These are readings of the field names and the table, not measured in game.</summary>
        public static float[] BoneWeights(Splice_v11 tag, GameSkeleton skeleton, DbRow? row)
        {
            var weights = new float[skeleton.BoneCount];
            void Cover(IEnumerable<int> bones, float weight)
            {
                foreach (var bone in bones)
                    weights[bone] = Math.Max(weights[bone], weight);
            }
            int Node(params string[] columns) => columns.Select(x => row == null ? -1 : skeleton.GetBoneIndexByName(row.Text(x))).FirstOrDefault(x => x >= 0, -1);
            HashSet<int> Under(int bone) => bone < 0 ? [] : [bone, .. skeleton.GetAllChildBones(bone)];

            var neck = Under(Node("neck_node"));
            var leftArm = Under(Node("leftshoulder_node", "leftarm_node"));
            var rightArm = Under(Node("rightshoulder_node", "rightarm_node"));
            var legs = Under(Node("leftupleg_node")).Union(Under(Node("rightupleg_node"))).ToHashSet();
            var torso = Under(Node("torso_splice_node", "spine_node")).Except(neck).Except(leftArm).Except(rightArm).Except(legs);
            Cover(neck, tag.NeckWeight);
            Cover(torso, tag.TorsoWeight);
            Cover(leftArm, tag.LeftArmWeight);
            Cover(rightArm, tag.RightArmWeight);
            Cover(legs, tag.LegsWeight);

            var parts = new[] { tag.NeckWeight, tag.TorsoWeight, tag.LeftArmWeight, tag.RightArmWeight, tag.LegsWeight }.Any(x => x > 0);
            if (tag.GenericWeight > 0 && tag.GenericBoneIndex >= 0 && tag.GenericBoneIndex < skeleton.BoneCount)
                Cover(Below(skeleton, tag.GenericBoneIndex, tag.GenericBoneDepth), tag.GenericWeight);
            else if (tag.GenericWeight > 0 && tag.GenericBoneIndex < 0 && !parts)
                Cover(Enumerable.Range(0, skeleton.BoneCount), tag.GenericWeight);

            if (On(tag.WeaponPose))
            {
                var weapons = row == null
                    ? Enumerable.Range(0, skeleton.BoneCount).Where(x => skeleton.BoneNames[x].StartsWith("be_prop", StringComparison.OrdinalIgnoreCase))
                    : Enumerable.Range(1, 6).Select(x => Node($"weapon{x}_node")).Where(x => x >= 0).SelectMany(Under);
                Cover(weapons, 1);
            }
            return weights;
        }

        static IEnumerable<int> Below(GameSkeleton skeleton, int bone, int depth)
        {
            IEnumerable<int> Levels(int b, int left) =>
                left == 1 ? [b] : [b, .. skeleton.GetDirectChildBones(b).SelectMany(x => Levels(x, left - 1))];
            return depth > 0 ? Levels(bone, depth) : [bone, .. skeleton.GetAllChildBones(bone)];
        }
    }
}
