using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;

namespace Editors.AnimationMeta.SuperView.Visualisation.Instances
{
    // DISABLE_MODEL and ALPHA: the main model is not drawn while `hidden` says so. Every tag that hides the model answers
    // through this one instance, since two would overwrite each other's answer. Props, effects and the skeleton are not the
    // model and stay. A model the user hid stays hidden.
    public class DisableModelInstance(SceneObject scene, Func<float, bool> hidden) : IMetaDataInstance
    {
        public AnimationPlayer Player => null;

        public void Update(float currentTime)
        {
            if (scene.ModelNode != null)
                scene.ModelNode.IsVisible = scene.ShowMesh.Value && !hidden(currentTime);
        }

        public void CleanUp() => scene.SetMeshVisability(scene.ShowMesh.Value);
    }
}
