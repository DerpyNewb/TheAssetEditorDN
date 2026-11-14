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
        private readonly SceneManager _sceneManager;
        private readonly SaveService _saveService;
        private readonly SaveValidatorService _saveValidator;
        private readonly IAbstractFormFactory<SaveDialogWindow> _saveWindowFactory;
        private readonly IAbstractFormFactory<SaveCheckWindow> _checkWindowFactory;
        private readonly GeometrySaveSettings _settings;

        /// <summary>Shows what the check found, with its fixes, and says whether to save anyway. The window is modal, which a test
        /// cannot click, so a test puts its own answer here.</summary>
        public Func<MainEditableNode, ErrorList, bool> AskToSaveAnyway { get; set; }

        public SaveCommandBase(GeometrySaveSettings settings, SceneManager sceneManager, SaveService saveService, SaveValidatorService saveValidator, IAbstractFormFactory<SaveDialogWindow> saveWindowFactory, IAbstractFormFactory<SaveCheckWindow> checkWindowFactory)
        {
            _settings = settings;
            _sceneManager = sceneManager;
            _saveService = saveService;
            _saveValidator = saveValidator;
            _saveWindowFactory = saveWindowFactory;
            _checkWindowFactory = checkWindowFactory;
            AskToSaveAnyway = ShowCheck;
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
            if (problems.HasData && AskToSaveAnyway(mainNode, problems) == false)
                return null;

            _settings.AttachmentPoints = mainNode.AttachmentPoints; // Bit of a hack, clean up at some point
            var res = _saveService.Save(mainNode, _settings);
            return res;
        }

        bool ShowCheck(MainEditableNode mainNode, ErrorList problems)
        {
            var window = _checkWindowFactory.Create();
            window.ViewModel.Initialize(mainNode, problems, forSave: true);
            window.ShowDialog();
            window.ViewModel.Dispose();
            window.ViewModel.AfterClose?.Invoke();
            return window.ViewModel.SaveAnyway;
        }
    }

    public class SaveCommand : SaveCommandBase, ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Save";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey { get; } = null;

        public SaveCommand(GeometrySaveSettings settings, SceneManager sceneManager, SaveService saveService, SaveValidatorService saveValidator, IAbstractFormFactory<SaveDialogWindow> saveWindowFactory, IAbstractFormFactory<SaveCheckWindow> checkWindowFactory)
            : base(settings, sceneManager, saveService, saveValidator, saveWindowFactory, checkWindowFactory)
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

        public SaveAsCommand(GeometrySaveSettings settings, SceneManager sceneManager, SaveService saveService, SaveValidatorService saveValidator, IAbstractFormFactory<SaveDialogWindow> saveWindowFactory, IAbstractFormFactory<SaveCheckWindow> checkWindowFactory)
            : base(settings, sceneManager, saveService, saveValidator, saveWindowFactory, checkWindowFactory)
        {
        }

        public void Execute() => Save(true);
    }
}
