using GameWorld.Core.Animation;
using GameWorld.Core.Animation.AnimationChange;
using Microsoft.Xna.Framework;
using Shared.GameFormats.AnimationMeta.Definitions;

namespace Editors.AnimationMeta.SuperView.Visualisation.Rules
{
    public class TransformBoneRule : ILocalSpaceAnimationRule
    {
        ILogger _logger = Logging.Create<CopyRootTransform>();
        bool _hasError = false;
        Transform_v10 _metadata;

        public TransformBoneRule(Transform_v10 metadata)
        {
            _metadata = metadata;
        }

        public int TargetNode => _metadata.TargetNode;

        public void TransformFrameLocalSpace(AnimationFrame frame, int boneId, float time)
        {
            if (boneId == _metadata.TargetNode)
                Apply(frame, time);
        }

        /// <summary>Turns and moves the target bone in its own space at the tag's strength at this time. A dock calls this again
        /// after placing the prop, since the dock replaces the bone's whole transform.</summary>
        public void Apply(AnimationFrame frame, float time)
        {
            var weight = TagWindow.Strength(_metadata.StartTime, _metadata.EndTime, _metadata.BlendInTime, _metadata.BlendOutTime, time);
            if (_hasError || weight <= 0)
                return;

            try
            {
                // 298 vanilla orientations are not unit length, such as (0.4, 0, -0.1, 1); unnormalised, they scaled the bone too.
                var quat = Quaternion.Slerp(Quaternion.Identity, Quaternion.Normalize(new Quaternion(_metadata.Orientation)), weight);
                var bone = frame.BoneTransforms[_metadata.TargetNode];
                bone.WorldTransform = Matrix.CreateFromQuaternion(quat) * Matrix.CreateTranslation(_metadata.Position * weight) * bone.WorldTransform;
            }
            catch (Exception e)
            {
                _logger.Here().Error($"Error in {nameof(TransformBoneRule)} - {e.Message}");
                _hasError = true;
            }
        }
    }
}
