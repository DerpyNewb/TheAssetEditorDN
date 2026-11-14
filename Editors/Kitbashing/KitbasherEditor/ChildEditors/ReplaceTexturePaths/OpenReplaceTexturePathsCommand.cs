using Editors.KitbasherEditor.Core.MenuBarViews;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Shared.Core.Misc;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor.ChildEditors.ReplaceTexturePaths
{
    // One window per Kitbash tab, modeless like the Snap tool's. Opened modal, it disabled the main window while it was open,
    // so nothing on the bar answered, not even with a tooltip.
    public class OpenReplaceTexturePathsCommand : IScopedKitbasherUiCommand, IDisposable
    {
        public string ToolTip { get; set; } = "Find and replace in the texture paths of the model's meshes";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey { get; } = null;

        private readonly SceneManager _sceneManager;
        private readonly SelectionManager _selectionManager;
        private readonly IAbstractFormFactory<ReplaceTexturePathsWindow> _formFactory;
        private ReplaceTexturePathsWindow? _window;

        public OpenReplaceTexturePathsCommand(SceneManager sceneManager, SelectionManager selectionManager, IAbstractFormFactory<ReplaceTexturePathsWindow> formFactory)
        {
            _sceneManager = sceneManager;
            _selectionManager = selectionManager;
            _formFactory = formFactory;
        }

        public void Execute()
        {
            if (_window != null)
            {
                _window.Activate();
                return;
            }

            var editableModel = _sceneManager.GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel);
            if (editableModel == null)
                return;

            _window = _formFactory.Create();
            _window.ViewModel.Initialize(editableModel, () => _selectionManager.GetState()?.SelectedObjects() ?? []);
            _window.Closed += OnWindowClosed;
            _window.Show();
        }

        /// <summary>Opens the window, or brings it forward, with this path in the Find box, as the save check's fix for a texture in no loaded pack.</summary>
        public void Open(string find)
        {
            Execute();
            if (_window != null)
                _window.ViewModel.Find = find;
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (_window == null)
                return;
            _window.Closed -= OnWindowClosed;
            _window.ViewModel.Dispose();
            _window = null;
        }

        public void Dispose() => _window?.Close();
    }
}
