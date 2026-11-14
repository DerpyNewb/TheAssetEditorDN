using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;

namespace Editors.AnimationMeta.SuperView.Visualisation.Instances
{
    // An event tag's marker, drawn in MetaDataBuilder.FiringColour while the tag fires (MetaDataBuilder.IsFiring).
    public class FiringInstance(IMetaDataInstance marker, SimpleDrawableNode node, float startTime, float endTime) : IMetaDataInstance
    {
        public AnimationPlayer Player => null;

        public void Update(float currentTime)
        {
            marker.Update(currentTime);
            node.Tint = MetaDataBuilder.IsFiring(startTime, endTime, currentTime) ? MetaDataBuilder.FiringColour : null;
        }

        public void CleanUp() => marker.CleanUp();
    }
}
