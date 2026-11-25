using Editors.AnimationMeta.SuperView.Editing;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Visualisation.Instances
{
    // A PROP or ANIMATED_PROP tag's model on its bone. A prop with its own animation is placed by the CopyRootTransform
    // rule on its player; one without is placed here, on every update. Either way the place is read from the tag, so the
    // model follows a drag in the viewport before it is rebuilt.
    // The prop's animation runs on the main animation's clock: it starts at the tag's Start Time,
    // holds its first frame before that and its last frame after its end.
    // The prop is drawn only while the tag is on (MetaDataBuilder.IsOn), and not at all while weaponShown says the animation
    // set shows the weapon its Override Prop names. Both measured in game on ANIMATED_PROP, 2026-11-19; PROP shares the path.
    public class AnimatedPropInstance : IMetaDataInstance, IMetaDataMarkerPreview
    {
        private readonly SceneNode _node;
        private readonly AnimationPlayer _player;
        private readonly float _startTime;
        private readonly float _endTime;
        private readonly Func<bool> _weaponShown;
        private readonly SpatialMetaDataBinding? _binding;
        private readonly ISkeletonProvider? _skeleton;
        private readonly bool _placedByAnimation;

        // Null, so the playback controls leave this player alone; Update drives it.
        public AnimationPlayer Player => null;
        public ParsedMetadataAttribute Source => _binding?.Source!;

        public AnimatedPropInstance(SceneNode node, AnimationPlayer player, float startTime, float endTime = 0, Func<bool>? weaponShown = null,
            SpatialMetaDataBinding? binding = null, ISkeletonProvider? skeleton = null, bool placedByAnimation = true)
        {
            _node = node;
            _player = player;
            _startTime = startTime;
            _endTime = endTime;
            _weaponShown = weaponShown ?? (() => false);
            _player.IsEnabled = true;
            _binding = binding;
            _skeleton = skeleton;
            _placedByAnimation = placedByAnimation;
        }

        // The tag's position and orientation, in its bone's frame.
        Matrix Local => _binding == null ? Matrix.Identity : Matrix.CreateFromQuaternion(_binding.Orientation ?? Quaternion.Identity) * Matrix.CreateTranslation(_binding.Position);

        Matrix Bone
        {
            get
            {
                var skeleton = _skeleton?.Skeleton;
                var bone = _binding?.BoneIndex ?? -1;
                return skeleton != null && bone >= 0 && bone < skeleton.BoneCount ? skeleton.GetAnimatedWorldTranform(bone) : Matrix.Identity;
            }
        }

        public bool IsHitTestVisible => _binding != null && _node.IsVisible && _node.Parent != null;
        public float HitTestRadius => 0.3f;
        public bool FollowsTagLive => true;
        public Matrix ReferenceWorldTransform => Bone * MetaDataPreviewMath.WorldOf(_node.Parent);
        public Vector3 FocusPosition => (Local * ReferenceWorldTransform).Translation;
        public IReadOnlyList<MetaDataMarkerHitTarget> HitTargets => _binding == null ? [] : [new(FocusPosition, MetaDataMarkerPoint.Default)];

        public void Update(float currentTime)
        {
            _player.SetTime((long)((currentTime - _startTime) * 1_000_000));
            if (_binding != null && !_placedByAnimation)
                _node.ModelMatrix = Local * Bone;
            _node.IsVisible = MetaDataBuilder.IsOn(_startTime, _endTime, currentTime) && !_weaponShown();
        }

        public void CleanUp()
        {
            _node.Parent.RemoveObject(_node);
        }
    }
}
