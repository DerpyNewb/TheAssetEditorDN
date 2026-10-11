using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;

namespace Editors.AnimationMeta.SuperView.Visualisation.Instances
{
    // The prop's animation runs on the main animation's clock: it starts at the tag's Start Time,
    // holds its first frame before that and its last frame after its end.
    public class AnimatedPropInstance : IMetaDataInstance
    {
        private readonly SceneNode _node;
        private readonly AnimationPlayer _player;
        private readonly float _startTime;

        // Null, so the playback controls leave this player alone; Update drives it.
        public AnimationPlayer Player => null;

        public AnimatedPropInstance(SceneNode node, AnimationPlayer player, float startTime)
        {
            _node = node;
            _player = player;
            _startTime = startTime;
            _player.IsEnabled = true;
        }

        public void Update(float currentTime) => _player.SetTime((long)((currentTime - _startTime) * 1_000_000));

        public void CleanUp()
        {
            _node.Parent.RemoveObject(_node);
        }
    }
}
