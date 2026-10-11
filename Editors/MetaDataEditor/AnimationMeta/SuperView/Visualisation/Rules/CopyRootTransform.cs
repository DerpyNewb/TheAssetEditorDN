using GameWorld.Core.Animation;
using GameWorld.Core.Animation.AnimationChange;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;

namespace Editors.AnimationMeta.SuperView.Visualisation.Rules
{
    public class CopyRootTransform : ILocalSpaceAnimationRule
    {
        readonly ILogger _logger = Logging.Create<CopyRootTransform>();
        readonly ISkeletonProvider _skeletonProvider;
        readonly int _boneId;

        bool _hasError = false;
        Vector3 _offsetPos;
        Quaternion _offsetRot;
        float _scale;

        public CopyRootTransform(ISkeletonProvider skeleton, int boneId, Vector3 offsetPos, Quaternion offsetRot, float scale)
        {
            _skeletonProvider = skeleton;
            _boneId = boneId;
            _offsetPos = offsetPos;
            _offsetRot = offsetRot;
            _scale = scale;
        }

        public void TransformFrameLocalSpace(AnimationFrame frame, int boneId, float v)
        {
            if (boneId != 0 || _hasError || _boneId == -1)
                return;

            try
            {
                // The prop's root keeps its own transform (zeppelin01_cannon's is turned 180 degrees),
                // so its whole model space is scaled, then offset in the bone's space.
                var transform = _skeletonProvider.Skeleton.GetAnimatedWorldTranform(_boneId);
                var m = frame.BoneTransforms[0].WorldTransform * Matrix.CreateScale(_scale) * Matrix.CreateFromQuaternion(_offsetRot) * Matrix.CreateTranslation(_offsetPos) * transform;
                frame.BoneTransforms[0].WorldTransform = m;
            }
            catch (Exception e)
            {
                _logger.Here().Error($"Error in {nameof(CopyRootTransform)} - {e.Message}");
                _hasError = true;
            }
        }
    }
}
