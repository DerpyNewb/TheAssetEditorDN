using Editors.KitbasherEditor.ChildEditors.SaveDialog;
using Editors.KitbasherEditor.Core.MenuBarViews;
using GameWorld.Core.Components;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services.SceneSaving;
using Shared.Core.Services;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor.UiCommands
{
    // Runs the checks a save runs, without saving, with the save settings last chosen.
    public class CheckModelCommand : ITransientKitbasherUiCommand
    {
        public const string NothingFound = "No problems found.";

        public string ToolTip { get; set; } = "Check the model for the problems a save would warn about";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey { get; } = null;

        private readonly SceneManager _sceneManager;
        private readonly SaveValidatorService _validator;
        private readonly GeometrySaveSettings _settings;
        private readonly IStandardDialogs _dialogs;

        public CheckModelCommand(SceneManager sceneManager, SaveValidatorService validator, GeometrySaveSettings settings, IStandardDialogs dialogs)
        {
            _sceneManager = sceneManager;
            _validator = validator;
            _settings = settings;
            _dialogs = dialogs;
        }

        public void Execute()
        {
            var mainNode = _sceneManager.GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel);
            var problems = _validator.Validate(mainNode, _settings);
            _dialogs.ShowDialogBox(problems.HasData ? SaveCommandBase.Describe(problems) : NothingFound, "Check model");
        }
    }
}
