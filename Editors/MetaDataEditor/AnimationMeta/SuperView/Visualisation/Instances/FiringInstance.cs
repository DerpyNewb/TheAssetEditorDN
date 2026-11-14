using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;

namespace Editors.AnimationMeta.SuperView.Visualisation.Instances
{
    // An event tag's marker, with a flash that shows only while the tag fires (MetaDataBuilder.IsFiring). The flash is a
    // child of the marker, so it rides the marker's bone and goes when the marker is cleaned up.
    public class FiringInstance(IMetaDataInstance marker, SceneNode flash, float startTime, float endTime) : IMetaDataInstance
    {
        public AnimationPlayer Player => null;

        public void Update(float currentTime)
        {
            marker.Update(currentTime);
            flash.IsVisible = MetaDataBuilder.IsFiring(startTime, endTime, currentTime);
        }

        public void CleanUp() => marker.CleanUp();
    }
}
