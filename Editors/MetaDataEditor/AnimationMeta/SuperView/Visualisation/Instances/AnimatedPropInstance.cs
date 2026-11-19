using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;

namespace Editors.AnimationMeta.SuperView.Visualisation.Instances
{
    // The prop's animation runs on the main animation's clock: it starts at the tag's Start Time,
    // holds its first frame before that and its last frame after its end.
    // The prop is drawn only while the tag is on (MetaDataBuilder.IsOn), and not at all while weaponShown says the animation
    // set shows the weapon its Override Prop names. Both measured in game on ANIMATED_PROP, 2026-11-19; PROP shares the path.
    public class AnimatedPropInstance : IMetaDataInstance
    {
        private readonly SceneNode _node;
        private readonly AnimationPlayer _player;
        private readonly float _startTime;
        private readonly float _endTime;
        private readonly Func<bool> _weaponShown;

        // Null, so the playback controls leave this player alone; Update drives it.
        public AnimationPlayer Player => null;

        public AnimatedPropInstance(SceneNode node, AnimationPlayer player, float startTime, float endTime = 0, Func<bool>? weaponShown = null)
        {
            _node = node;
            _player = player;
            _startTime = startTime;
            _endTime = endTime;
            _weaponShown = weaponShown ?? (() => false);
            _player.IsEnabled = true;
        }

        public void Update(float currentTime)
        {
            _player.SetTime((long)((currentTime - _startTime) * 1_000_000));
            _node.IsVisible = MetaDataBuilder.IsOn(_startTime, _endTime, currentTime) && !_weaponShown();
        }

        public void CleanUp()
        {
            _node.Parent.RemoveObject(_node);
        }
    }
}
