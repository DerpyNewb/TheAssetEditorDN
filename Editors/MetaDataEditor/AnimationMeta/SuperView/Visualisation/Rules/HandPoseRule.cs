using GameWorld.Core.Animation;
using GameWorld.Core.Animation.AnimationChange;
using Microsoft.Xna.Framework;

namespace Editors.AnimationMeta.SuperView.Visualisation.Rules
{
    // LHAND_POSE / RHAND_POSE: the fingers of one hand take their pose from one of the animation
    // set's HAND_POSE_* slots. Vanilla WH3 uses the tags as keys, not ranges: 7,433 of 21,132 are a
    // single instant (start = end), 13,659 are 0/0 and 40 a range, so the pose set at a key's time holds until
    // that hand's next key. The slot animation poses the whole skeleton; only the fingers are taken.
    public class HandPoseRule : ILocalSpaceAnimationRule
    {
        // Pose null = the key releases the fingers back to the animation (HandPoseId -1, or a slot
        // the animation set does not have).
        public record Key(float Time, AnimationFrame? Pose, float Weight);

        readonly HashSet<int> _fingerBones;
        readonly List<Key> _keys;

        public HandPoseRule(GameSkeleton skeleton, string handBoneName, IEnumerable<Key> keys)
        {
            var hand = skeleton.GetBoneIndexByName(handBoneName);
            _fingerBones = hand < 0 ? [] : skeleton.GetAllChildBones(hand).ToHashSet();
            _keys = keys.OrderBy(x => x.Time).ToList();    // stable: on a tie the later tag wins
        }

        public void TransformFrameLocalSpace(AnimationFrame frame, int boneId, float time)
        {
            if (_fingerBones.Contains(boneId) == false)
                return;

            var key = _keys.LastOrDefault(x => x.Time <= time);
            if (key?.Pose == null)
                return;

            var bone = frame.BoneTransforms[boneId];
            var pose = key.Pose.BoneTransforms[boneId];
            bone.Rotation = Quaternion.Slerp(bone.Rotation, pose.Rotation, key.Weight);
            bone.Translation = Vector3.Lerp(bone.Translation, pose.Translation, key.Weight);
            bone.ComputeWorldMatrixFromComponents();
        }
    }
}
