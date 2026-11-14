using Editors.KitbasherEditor.Core.MenuBarViews;
using Editors.KitbasherEditor.ViewModels.SaveDialog;
using GameWorld.Core.Components;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services.SceneSaving;
using Shared.Core.ErrorHandling;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor.ChildEditors.SaveDialog
{
    public class SaveCommandBase
    {
        // A message box taller than the screen hides its buttons.
        const int MaxProblemsListed = 20;

        private readonly SceneManager _sceneManager;
        private readonly SaveService _saveService;
        private readonly SaveValidatorService _saveValidator;
        private readonly IStandardDialogs _dialogs;
        private readonly IAbstractFormFactory<SaveDialogWindow> _saveWindowFactory;
        private readonly GeometrySaveSettings _settings;

        public SaveCommandBase(GeometrySaveSettings settings, SceneManager sceneManager, SaveService saveService, SaveValidatorService saveValidator, IStandardDialogs dialogs, IAbstractFormFactory<SaveDialogWindow> saveWindowFactory)
        {
            _settings = settings;
            _sceneManager = sceneManager;
            _saveService = saveService;
            _saveValidator = saveValidator;
            _dialogs = dialogs;
            _saveWindowFactory = saveWindowFactory;
        }

        protected SaveResult? Save(bool forceShowDialog)
        {
            if (_settings.IsUserInitialized == false || forceShowDialog || _settings.DisplayDialogOnNextSave)
            {
                var window = _saveWindowFactory.Create();
                window.Initialize(_settings);
                var saveScene = window.ShowDialog();
                if (saveScene != true)
                    return null;
            }

            var mainNode = _sceneManager.GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel);

            // Checked after the dialog, which decides the LODs and whether hidden meshes are saved.
            var problems = _saveValidator.Validate(mainNode, _settings);
            if (problems.HasData && _dialogs.ShowYesNoBox(Describe(problems) + "\n\nSave anyway?", "Save") != ShowMessageBoxResult.OK)
                return null;

            _settings.AttachmentPoints = mainNode.AttachmentPoints; // Bit of a hack, clean up at some point
            var res = _saveService.Save(mainNode, _settings);
            return res;
        }

        public static string Describe(ErrorList problems)
        {
            var lines = problems.Errors
                .OrderByDescending(x => x.IsError)
                .Select(x => $"{x.ErrorType} - {x.ItemName}: {x.Description}")
                .ToList();
            var text = string.Join("\n", lines.Take(MaxProblemsListed));
            return lines.Count > MaxProblemsListed ? $"{text}\n...and {lines.Count - MaxProblemsListed} more." : text;
        }
    }

    public class SaveCommand : SaveCommandBase, ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Save";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey { get; } = null;

        public SaveCommand(GeometrySaveSettings settings, SceneManager sceneManager, SaveService saveService, SaveValidatorService saveValidator, IStandardDialogs dialogs, IAbstractFormFactory<SaveDialogWindow> saveWindowFactory)
            : base(settings, sceneManager, saveService, saveValidator, dialogs, saveWindowFactory)
        {
        }

        public void Execute() => Save(false);
        public SaveResult? ExecuteWithResult() => Save(false);
    }

    public class SaveAsCommand : SaveCommandBase, ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Save As";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey { get; } = null;

        public SaveAsCommand(GeometrySaveSettings settings, SceneManager sceneManager, SaveService saveService, SaveValidatorService saveValidator, IStandardDialogs dialogs, IAbstractFormFactory<SaveDialogWindow> saveWindowFactory)
            : base(settings, sceneManager, saveService, saveValidator, dialogs, saveWindowFactory)
        {
        }

        public void Execute() => Save(true);
    }
}
