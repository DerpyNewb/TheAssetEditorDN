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
            var weight = Weight(_metadata.StartTime, _metadata.EndTime, _metadata.BlendInTime, _metadata.BlendOutTime, time);
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

        /// <summary>Full strength from start to end, fading in over blendIn before start and out over blendOut after end, as
        /// vanilla's timing implies: 1,744 tags have blends longer than their window, and 225 start and end at the same moment
        /// with blends. A tag at 0 to 0 with no blends holds for the whole clip; vanilla has 2,255.</summary>
        public static float Weight(float start, float end, float blendIn, float blendOut, float time)
        {
            if (start == 0 && end == 0 && blendIn == 0 && blendOut == 0)
                return 1;
            if (time < start)
                return blendIn > 0 && time > start - blendIn ? (time - (start - blendIn)) / blendIn : 0;
            if (time <= end)
                return 1;
            return blendOut > 0 && time < end + blendOut ? 1 - (time - end) / blendOut : 0;
        }
    }
}
