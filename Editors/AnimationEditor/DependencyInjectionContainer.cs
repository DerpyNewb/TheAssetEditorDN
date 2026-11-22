using AnimationEditor.Common.BaseControl;
using AnimationEditor.MountAnimationCreator;
using Editors.AnimationVisualEditors.AnimationKeyframeEditor;
using Editors.Shared.Core.Common.BaseControl;
using GameWorld.Core.Animation.Posing;
using GameWorld.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.DependencyInjection;
using Shared.Core.DevConfig;
using Shared.Core.ToolCreation;

namespace Editors.AnimationVisualEditors
{
    public class DependencyInjectionContainer : DependencyContainer
    {
        public override void Register(IServiceCollection serviceCollection)
        {
            serviceCollection.AddScoped<MountAnimationCreatorViewModel>();

            serviceCollection.AddScoped<AnimationKeyframeEditorViewModel>();
            serviceCollection.AddScoped(x => new JointLimitStore(x.GetRequiredService<ISkeletonAnimationLookUpHelper>(), JointLimitStore.DefaultFolder));

            RegisterAllAsInterface<IDeveloperConfiguration>(serviceCollection, ServiceLifetime.Transient);
        }

        public override void RegisterTools(IEditorDatabase database)
        {
            EditorInfoBuilder
                .Create<MountAnimationCreatorViewModel, EditorHostView>(EditorEnums.MountTool_Editor)
                .AddToToolbar("Rider Animation Editor", true, ToolsMenuSection.Animation)
                .Build(database);
        
            EditorInfoBuilder
              .Create<AnimationKeyframeEditorViewModel, EditorHostView>(EditorEnums.AnimationKeyFrame_Editor)
              .AddToToolbar("Keyframe Editor", true, ToolsMenuSection.Animation)
              .Build(database);
        }
    }
}
