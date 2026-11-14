using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.GameFormats.AnimationMeta.Definitions;

namespace Editors.AnimationMeta.SuperView.Visualisation.Instances
{
    // RESCALE: while a tag is on (MetaDataBuilder.IsOn) the asset is drawn at the tag's Scale, the scale itself and not a
    // factor; the latest tag that is on wins. SceneObject.Update sets the asset back to its Offset every frame before the
    // instances run, so this sets the whole matrix rather than multiplying into it.
    public class RescaleInstance(SceneObject scene, IReadOnlyList<ReScale> tags) : IMetaDataInstance
    {
        public AnimationPlayer Player => null;

        public void Update(float currentTime)
        {
            var scale = tags.LastOrDefault(x => MetaDataBuilder.IsOn(x, currentTime))?.Scale ?? 1;
            scene.MainNode.ModelMatrix = Matrix.CreateScale(scale) * scene.Offset;
        }

        public void CleanUp() => scene.MainNode.ModelMatrix = scene.Offset;
    }
}
