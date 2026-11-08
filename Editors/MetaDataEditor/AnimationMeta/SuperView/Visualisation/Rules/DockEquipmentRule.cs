using GameWorld.Core.Animation;
using GameWorld.Core.Animation.AnimationChange;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;

namespace Editors.AnimationMeta.SuperView.Visualisation.Rules
{
    public class DockEquipmentRule : IWorldSpaceAnimationRule
    {
        ILogger _logger = Logging.Create<DockEquipmentRule>();
        bool _hasError = false;

        int _equipmentSlotToDock;
        AnimationClip _dockAnimation;
        ISkeletonProvider _skeletonProvider;
        float _startTime;
        float _endTime;
        int _dockTargetkBoneId;
        Matrix _offset;

        public DockEquipmentRule(int dockTargetkBoneId, int equipmentSlotToDock, AnimationClip dockAnimation, ISkeletonProvider skeletonProvider, float startTime, float endTime)
        {
            _dockTargetkBoneId = dockTargetkBoneId;
            _dockAnimation = dockAnimation;
            _skeletonProvider = skeletonProvider;
            _startTime = startTime;
            _endTime = endTime;

            try
            {
                var boneName = "be_prop_" + (equipmentSlotToDock - 1);
                _equipmentSlotToDock = skeletonProvider.Skeleton.GetBoneIndexByName(boneName);
                if (_equipmentSlotToDock == -1)
                {
                    // 0 asks for be_prop_-1, and 7 (common on trebuchets) for a be_prop_6 most skeletons lack.
                    _logger.Here().Warning($"Dock value {equipmentSlotToDock} not drawn: skeleton {skeletonProvider.Skeleton.SkeletonName} has no {boneName}");
                    _hasError = true;
                    return;
                }
                var offsetFrame = AnimationSampler.Sample(0, _skeletonProvider.Skeleton, _dockAnimation);
                _offset = offsetFrame.GetSkeletonAnimatedWorldDiff(_skeletonProvider.Skeleton, _equipmentSlotToDock, _dockTargetkBoneId);
            }
            catch (Exception e)
            {
                _logger.Here().Error($"Error in {nameof(DockEquipmentRule)} - {e.Message}");
                _hasError = true;
            }
        }

        public void TransformFrameWorldSpace(AnimationFrame frame, float time)
        {
            if (_hasError)
                return;

            try
            {
                if (time >= _startTime)
                {
                    var offsetFrame = AnimationSampler.Sample(0, _skeletonProvider.Skeleton, _dockAnimation);
                    _offset = offsetFrame.GetSkeletonAnimatedWorldDiff(_skeletonProvider.Skeleton, _dockTargetkBoneId, _equipmentSlotToDock);

                    var propTransform = _skeletonProvider.Skeleton.GetAnimatedWorldTranform(_dockTargetkBoneId);
                    frame.BoneTransforms[_equipmentSlotToDock].WorldTransform = _offset * propTransform;
                }
            }
            catch (Exception e)
            {
                _logger.Here().Error($"Error in {nameof(DockEquipmentRule)} - {e.Message}");
                _hasError = true;
            }
        }
    }
}
