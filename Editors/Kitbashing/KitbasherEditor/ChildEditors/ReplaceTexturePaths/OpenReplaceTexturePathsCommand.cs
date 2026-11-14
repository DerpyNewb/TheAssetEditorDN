using Editors.KitbasherEditor.Core.MenuBarViews;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Shared.Core.Misc;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor.ChildEditors.ReplaceTexturePaths
{
    public class OpenReplaceTexturePathsCommand : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Find and replace in the texture paths of the model's meshes";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey { get; } = null;

        private readonly SceneManager _sceneManager;
        private readonly SelectionManager _selectionManager;
        private readonly IAbstractFormFactory<ReplaceTexturePathsWindow> _formFactory;

        public OpenReplaceTexturePathsCommand(SceneManager sceneManager, SelectionManager selectionManager, IAbstractFormFactory<ReplaceTexturePathsWindow> formFactory)
        {
            _sceneManager = sceneManager;
            _selectionManager = selectionManager;
            _formFactory = formFactory;
        }

        public void Execute()
        {
            var editableModel = _sceneManager.GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel);
            if (editableModel == null)
                return;

            var window = _formFactory.Create();
            window.ViewModel.Initialize(editableModel, _selectionManager.GetState()?.SelectedObjects() ?? []);
            window.ShowDialog();
        }
    }
}
