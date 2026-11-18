using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;

namespace Editors.Shared.Core.Services.RiderBake
{
    /// <summary>The Mount Animation Creator's settings, without its view model.</summary>
    public record RiderBakeSettings(
        Vector3 Translation,
        Vector3 RotationDegrees,
        float Scale = 1,
        int LoopCount = 1,
        bool FitAnimation = true,
        bool KeepRiderRotation = true,
        bool IsRootNodeAnimation = false);

    /// <summary>The Mount Animation Creator's baker, moved here unchanged: the rider bone is pinned to a mount vertex each frame.</summary>
    public class MountAnimationGeneratorService
    {
        readonly RiderBakeSettings _settings;
        readonly int _mountVertexId;
        readonly int _riderBoneIndex;
        readonly GameSkeleton _riderSkeleton;
        readonly GameSkeleton _mountSkeleton;
        readonly MeshAnimationHelper _mountVertexPositionResolver;

        public MountAnimationGeneratorService(RiderBakeSettings settings, Rmv2MeshNode mountMesh, int mountVertexId, int riderBoneIndex, GameSkeleton riderSkeleton, GameSkeleton mountSkeleton)
        {
            _settings = settings;
            _mountVertexId = mountVertexId;
            _riderBoneIndex = riderBoneIndex;
            _riderSkeleton = riderSkeleton;
            _mountSkeleton = mountSkeleton;
            _mountVertexPositionResolver = new MeshAnimationHelper(mountMesh, Matrix.CreateScale(settings.Scale));
        }

        public GameSkeleton RiderSkeleton => _riderSkeleton;

        public AnimationClip GenerateMountAnimation(AnimationClip mountAnimation, AnimationClip riderAnimation)
        {
            var translationOffset = _settings.Translation;
            var rotationOffsetVector = _settings.RotationDegrees;
            var rotationOffset = Quaternion.CreateFromYawPitchRoll(MathHelper.ToRadians(rotationOffsetVector.X), MathHelper.ToRadians(rotationOffsetVector.Y), MathHelper.ToRadians(rotationOffsetVector.Z));

            var newRiderAnim = riderAnimation.Clone();

            GameWorld.Core.Animation.AnimationEditor.LoopAnimation(newRiderAnim, _settings.LoopCount);

            if (_settings.FitAnimation)
                newRiderAnim = GameWorld.Core.Animation.AnimationEditor.ReSample(_riderSkeleton, newRiderAnim, mountAnimation.DynamicFrames.Count, mountAnimation.PlayTimeInSec);

            var maxFrameCount = Math.Min(mountAnimation.DynamicFrames.Count, newRiderAnim.DynamicFrames.Count);
            for (var i = 0; i < maxFrameCount; i++)
            {
                var mountFrame = AnimationSampler.Sample(i, 0, _mountSkeleton, mountAnimation);
                var mountBoneWorldMatrix = _mountVertexPositionResolver.GetVertexTransformWorld(mountFrame, _mountVertexId);
                mountBoneWorldMatrix.Decompose(out var _, out var mountVertexRot, out var mountVertexPos);

                // Make sure the rider moves along in the world with the same speed as the mount when there is a root bone
                if (_settings.IsRootNodeAnimation)
                {
                    newRiderAnim.DynamicFrames[i].Position[0] = mountFrame.BoneTransforms[0].Translation;
                    newRiderAnim.DynamicFrames[i].Rotation[0] = Quaternion.Identity;
                }

                var origianlRotation = Quaternion.Identity;
                if (_settings.KeepRiderRotation)
                {
                    var riderFrame = AnimationSampler.Sample(i, 0, _riderSkeleton, newRiderAnim);
                    var riderBoneWorldmatrix = riderFrame.GetSkeletonAnimatedWorld(_riderSkeleton, _riderBoneIndex);
                    riderBoneWorldmatrix.Decompose(out var _, out origianlRotation, out var _);
                }

                var originalPosition = newRiderAnim.DynamicFrames[i].Position[_riderBoneIndex];
                var originalRotation = newRiderAnim.DynamicFrames[i].Rotation[_riderBoneIndex];

                var newRiderPosition = mountVertexPos + translationOffset;
                if (_settings.IsRootNodeAnimation)
                    newRiderPosition = newRiderPosition - mountFrame.BoneTransforms[0].Translation;
                var newRiderRotation = Quaternion.Multiply(Quaternion.Multiply(mountVertexRot, origianlRotation), rotationOffset);

                var riderPositionDiff = newRiderPosition - originalPosition;
                var riderRotationDiff = newRiderRotation * Quaternion.Inverse(originalRotation);

                newRiderAnim.DynamicFrames[i].Position[_riderBoneIndex] = newRiderPosition;
                newRiderAnim.DynamicFrames[i].Rotation[_riderBoneIndex] = newRiderRotation;

                // Process attachment/prop points
                var propBones = new List<int>();
                if (_settings.IsRootNodeAnimation)
                {
                    var parentBoneIndex = _riderSkeleton.GetParentBoneIndex(_riderBoneIndex);
                    if (parentBoneIndex != -1)
                        propBones = _riderSkeleton.GetDirectChildBones(parentBoneIndex);
                }
                else
                {
                    propBones = _riderSkeleton.GetDirectChildBones(-1);
                }

                foreach (var propBoneId in propBones)
                {
                    if (propBoneId == _riderBoneIndex)
                        continue;
                    newRiderAnim.DynamicFrames[i].Position[propBoneId] += riderPositionDiff;
                    newRiderAnim.DynamicFrames[i].Rotation[propBoneId] = riderRotationDiff * newRiderAnim.DynamicFrames[i].Rotation[propBoneId];
                }
            }

            return newRiderAnim;
        }
    }
}
