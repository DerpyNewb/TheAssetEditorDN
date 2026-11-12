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

        readonly bool _rootFollowsBone;
        readonly Matrix? _bindRoot;

        /// <summary>Whether a prop is animated in the main model's space: its root starts on the attach bone, as 121 of 156 vanilla
        /// props with a bone do (the Doomseeker's flails). The rest are animated in the bone's own space, their root at the origin.</summary>
        public static bool RootFollowsBone(GameSkeleton prop, AnimationClip propClip, Matrix boneAtStart)
        {
            var bone = boneAtStart.Translation;
            var root = AnimationSampler.Sample(0, prop, propClip).GetSkeletonAnimatedWorld(prop, 0).Translation;
            return bone.Length() > 0.01f && Vector3.Distance(root, bone) < 0.01f;
        }

        /// <summary>Whether the prop's root is keyed to one fixed turn, in place, that is neither identity nor its bind pose: the
        /// ratling gun's root is held a quarter turn about its barrels, and the game fires the gatling built on it unturned. The
        /// game's own props key their roots at identity or at the bind pose (zeppelin01_cannon), and those keys are kept.</summary>
        // ponytail: two in-game observations, Azeros's gatling and the zeppelin turret; 79 vanilla tags fall here, mostly
        // Nagash's book, unchecked in game. If one of them draws wrong, the rule is narrower than this.
        public static bool RootKeyIsFixedTurn(GameSkeleton prop, AnimationClip propClip)
        {
            static bool Same(Quaternion a, Quaternion b) => Math.Abs(Quaternion.Dot(a, b)) > 0.9998f; // within about 2 degrees
            prop.GetWorldTransform(0).Decompose(out _, out var bind, out var bindPos);
            Quaternion? first = null;
            for (var i = 0; i <= 8; i++)
            {
                AnimationSampler.Sample(propClip.PlayTimeInSec * i / 8, prop, propClip).GetSkeletonAnimatedWorld(prop, 0).Decompose(out _, out var key, out var pos);
                first ??= key;
                if (Vector3.Distance(pos, bindPos) > 0.01f || !Same(key, first.Value))
                    return false;
            }
            return !Same(first!.Value, Quaternion.Identity) && !Same(first.Value, bind);
        }

        public CopyRootTransform(ISkeletonProvider skeleton, int boneId, Vector3 offsetPos, Quaternion offsetRot, float scale, bool rootFollowsBone = false, Matrix? bindRoot = null)
        {
            _skeletonProvider = skeleton;
            _boneId = boneId;
            _offsetPos = offsetPos;
            _offsetRot = offsetRot;
            _scale = scale;
            _rootFollowsBone = rootFollowsBone;
            _bindRoot = bindRoot;
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
                // A root animated in the model's space is taken into the bone's first, so it is not carried to the bone twice.
                // A root keyed to a fixed turn (RootKeyIsFixedTurn) sits in its bind pose instead.
                var root = _rootFollowsBone ? frame.BoneTransforms[0].WorldTransform * Matrix.Invert(transform) : _bindRoot ?? frame.BoneTransforms[0].WorldTransform;
                var m = root * Matrix.CreateScale(_scale) * Matrix.CreateFromQuaternion(_offsetRot) * Matrix.CreateTranslation(_offsetPos) * transform;
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
