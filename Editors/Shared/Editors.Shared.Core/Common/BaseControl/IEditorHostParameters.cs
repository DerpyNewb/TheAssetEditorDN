using Editors.Shared.Core.Common.AnimationPlayer;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Grid;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Services;
using GameWorld.Core.WpfWindow;

namespace Editors.Shared.Core.Common.BaseControl
{
    public interface IEditorHostParameters
    {
        AnimationPlayerViewModel AnimationPlayerViewModel { get; }
        IComponentInserter ComponentInserter { get; }
        FocusSelectableObjectService FocusSelectableObjectService { get; }
        IWpfGame GameWorld { get; }
        SceneObjectViewModelBuilder SceneObjectViewModelBuilder { get; }
        SceneObjectEditor SceneObjectEditor { get; }
        RenderEngineComponent RenderEngine { get; }
        SceneRenderParametersStore SceneParameters { get; }
        GridComponent Grid { get; }
    }

    public class EditorHostParameters : IEditorHostParameters
    {
        public EditorHostParameters(
            IComponentInserter componentInserter,
            AnimationPlayerViewModel animationPlayerViewModel,
            IWpfGame gameWorld,
            FocusSelectableObjectService focusSelectableObjectService,
            SceneObjectViewModelBuilder sceneObjectViewModelBuilder,
            SceneObjectEditor sceneObjectEditor,
            RenderEngineComponent renderEngine,
            SceneRenderParametersStore sceneParameters,
            GridComponent grid)
        {
            ComponentInserter = componentInserter;
            AnimationPlayerViewModel = animationPlayerViewModel;
            GameWorld = gameWorld;
            FocusSelectableObjectService = focusSelectableObjectService;
            SceneObjectViewModelBuilder = sceneObjectViewModelBuilder;
            SceneObjectEditor = sceneObjectEditor;
            RenderEngine = renderEngine;
            SceneParameters = sceneParameters;
            Grid = grid;
        }

        public IComponentInserter ComponentInserter { get; }
        public AnimationPlayerViewModel AnimationPlayerViewModel { get; }
        public IWpfGame GameWorld { get; }
        public FocusSelectableObjectService FocusSelectableObjectService { get; }
        public SceneObjectViewModelBuilder SceneObjectViewModelBuilder { get; }

        public SceneObjectEditor SceneObjectEditor { get; }
        public RenderEngineComponent RenderEngine { get; }
        public SceneRenderParametersStore SceneParameters { get; }
        public GridComponent Grid { get; }
    }
}
