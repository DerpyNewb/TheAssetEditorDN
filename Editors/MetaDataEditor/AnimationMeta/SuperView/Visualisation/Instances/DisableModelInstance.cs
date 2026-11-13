using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Visualisation.Instances
{
    // DISABLE_MODEL: the main model is not drawn while any of the tags is on (MetaDataBuilder.IsOn). Props, effects and
    // the skeleton are not the model and stay. A model the user hid stays hidden.
    public class DisableModelInstance(SceneObject scene, IReadOnlyList<DecodedMetaEntryBase> tags) : IMetaDataInstance
    {
        public AnimationPlayer Player => null;

        public void Update(float currentTime)
        {
            if (scene.ModelNode != null)
                scene.ModelNode.IsVisible = scene.ShowMesh.Value && !tags.Any(x => MetaDataBuilder.IsOn(x, currentTime));
        }

        public void CleanUp() => scene.SetMeshVisability(scene.ShowMesh.Value);
    }
}
