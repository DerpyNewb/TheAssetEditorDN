using Editors.KitbasherEditor.ChildEditors.SaveDialog;
using Editors.KitbasherEditor.Core.MenuBarViews;
using GameWorld.Core.Components;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services.SceneSaving;
using Shared.Core.Misc;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor.UiCommands
{
    // Runs the checks a save runs, without saving, with the save settings last chosen. The result is a modeless window, one per
    // Kitbash tab, so the model can be worked on with the list open.
    public class CheckModelCommand : IScopedKitbasherUiCommand, IDisposable
    {
        public string ToolTip { get; set; } = "Check the model for the problems a save would warn about";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey { get; } = null;

        private readonly SceneManager _sceneManager;
        private readonly SaveValidatorService _validator;
        private readonly GeometrySaveSettings _settings;
        private readonly IAbstractFormFactory<SaveCheckWindow> _windowFactory;
        private SaveCheckWindow? _window;

        public CheckModelCommand(SceneManager sceneManager, SaveValidatorService validator, GeometrySaveSettings settings, IAbstractFormFactory<SaveCheckWindow> windowFactory)
        {
            _sceneManager = sceneManager;
            _validator = validator;
            _settings = settings;
            _windowFactory = windowFactory;
        }

        public void Execute()
        {
            if (_window != null)
            {
                _window.ViewModel.Refresh();
                _window.Activate();
                return;
            }

            var mainNode = _sceneManager.GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel);
            _window = _windowFactory.Create();
            _window.ViewModel.Initialize(mainNode, _validator.Validate(mainNode, _settings), forSave: false);
            _window.Closed += OnWindowClosed;
            _window.Show();
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
