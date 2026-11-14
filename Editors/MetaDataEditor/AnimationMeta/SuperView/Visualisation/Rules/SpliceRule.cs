using GameWorld.Core.Animation;
using GameWorld.Core.Animation.AnimationChange;
using Microsoft.Xna.Framework;
using Shared.GameFormats.AnimationMeta.Definitions;

namespace Editors.AnimationMeta.SuperView.Visualisation.Rules
{
    // SPLICE, the generic-bone case MetaDataBuilder draws: while the tag is on, its animation drives the generic bone and
    // every bone under it, blended in by Generic Weight. The spliced animation runs from the tag's Start Time and loops.
    // The loop is a reading, not a measurement. Most vanilla splices start at 0 and run the whole clip, and there it and
    // running on the main clock agree.
    public class SpliceRule : ILocalSpaceAnimationRule
    {
        readonly Splice_v11 _tag;
        readonly AnimationClip _clip;
        readonly GameSkeleton _skeleton;
        readonly HashSet<int> _bones;
        (float Time, AnimationFrame Pose)? _last;

        public SpliceRule(GameSkeleton skeleton, AnimationClip clip, Splice_v11 tag)
        {
            _skeleton = skeleton;
            _clip = clip;
            _tag = tag;
            _bones = [tag.GenericBoneIndex, .. skeleton.GetAllChildBones(tag.GenericBoneIndex)];
        }

        public void TransformFrameLocalSpace(AnimationFrame frame, int boneId, float time)
        {
            if (!_bones.Contains(boneId) || !MetaDataBuilder.IsOn(_tag, time))
                return;
            var weight = _tag.GenericWeight * Ramp(time - _tag.StartTime, _tag.BlendInTime);
            if (_tag.EndTime > _tag.StartTime)
                weight *= Ramp(_tag.EndTime - time, _tag.BlendOutTime);
            if (weight <= 0)
                return;

            var bone = frame.BoneTransforms[boneId];
            var pose = PoseAt(time).BoneTransforms[boneId];
            bone.Rotation = Quaternion.Slerp(bone.Rotation, pose.Rotation, weight);
            bone.Translation = Vector3.Lerp(bone.Translation, pose.Translation, weight);
            bone.ComputeWorldMatrixFromComponents();
        }

        // Blend In Time and Blend Out Time read as linear ramps of the weight at the window's edges.
        static float Ramp(float elapsed, float length) => length > 0 ? Math.Clamp(elapsed / length, 0, 1) : 1;

        // Every bone asks for the same time in turn, so the spliced pose is sampled once per time.
        AnimationFrame PoseAt(float time)
        {
            if (_last is { } last && last.Time == time)
                return last.Pose;
            var length = _clip.PlayTimeInSec;
            var t = length > 0 ? ((time - _tag.StartTime) % length + length) % length / length : 0;
            var pose = AnimationSampler.Sample(t, _skeleton, _clip);
            _last = (time, pose);
            return pose;
        }
    }
}
