using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AssetEditor.Services;
using AssetEditor.UiCommands;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shared.Core.Commands;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Events;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;
using Shared.Ui.Common;
using Shared.Ui.Common.MenuSystem;

namespace AssetEditor.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDropTarget<IEditorInterface, bool>
    {
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly HotkeyService _hotkeys;
        private readonly ApplicationSettingsService _settingsService;
        private readonly LocalizationManager _localizationManager;

        public PackFileBrowserViewModel FileTree { get; private set; }
        public MenuBarViewModel MenuBar { get; set; }
        public IEditorDatabase ToolsFactory { get; set; }

        [ObservableProperty] public partial IEditorManager EditorManager { get; set; }
        [ObservableProperty] public partial bool IsClosingWithoutPrompt { get; set; }
        [ObservableProperty] public partial string ApplicationTitle { get; set; }
        [ObservableProperty] public partial string CurrentGame { get; set; }
        [ObservableProperty] public partial string EditablePackFile { get; set; }
        [ObservableProperty] public partial bool IsPackFileExplorerVisible { get; set; } = true;
        [ObservableProperty] public partial GridLength FileTreeColumnWidth { get; set; } = new GridLength(0.28, GridUnitType.Star);


        public MainViewModel(
                IEditorManager editorManager,
                PackFileTreeViewFactory packFileBrowserBuilder,
                MenuBarViewModel menuViewModel, 
                IPackFileService packfileService, 
                IEditorDatabase toolFactory, 
                IUiCommandFactory uiCommandFactory, 
                IEventHub eventHub,
                ApplicationSettingsService applicationSettingsService,
                HotkeyService hotkeys,
                LocalizationManager localizationManager)
        {
            _hotkeys = hotkeys;
            MenuBar = menuViewModel;
            _settingsService = applicationSettingsService;
            _localizationManager = localizationManager;

            EditorManager = editorManager;
            _uiCommandFactory = uiCommandFactory;

            eventHub.Register<PackFileContainerSetAsMainEditableEvent>(this, SetStatusBarEditablePackFile);

            FileTree = packFileBrowserBuilder.Create(ContextMenuType.MainApplication, showCaFiles: true, showFoldersOnly: false);
            FileTree.FileOpen += OpenFile;

            ToolsFactory = toolFactory;

            // "DN v1.1" marks this fork's build and its own version apart from the main repo's; the upstream number stays numeric for the updater's comparison.
            ApplicationTitle = $"AssetEditor v{VersionChecker.GetCurrentVersion()} - DN v{ForkRelease.Current}";
            CurrentGame = $"Current game: {GameInformationDatabase.GetGameById(applicationSettingsService.CurrentSettings.CurrentGame).DisplayName}";
        }

        /// <summary>Ctrl+S by default; the Hotkeys tab in Settings changes it.</summary>
        public const string SaveHotkey = "General.Save";

        /// <summary>Saves the open tab when the key is the save hotkey and the tab is an editor that saves, after beforeSave. False
        /// leaves the key alone.</summary>
        public bool OnKeyPressed(Key key, ModifierKeys modifiers, Action? beforeSave = null)
        {
            if (_hotkeys.Get(SaveHotkey)?.Matches(key, modifiers) != true)
                return false;
            var editors = EditorManager.GetAllEditors();
            var current = EditorManager.GetCurrentEditor();
            if (current < 0 || current >= editors.Count || editors[current] is not ISaveableEditor editor)
                return false;
            beforeSave?.Invoke();
            editor.Save();
            return true;
        }

        public void OpenFile(PackFile file) => _uiCommandFactory.Create<OpenEditorCommand>().Execute(file);

        public void OpenPack(string path) => _uiCommandFactory.Create<OpenPackForEditingCommand>().Open(path);

        [RelayCommand] private void Closing(IEditorInterface editor) 
        {
            var hasUnsavedPackFiles = FileTree.Files.Any(node => node.UnsavedChanges.HasChanges);
            if (EditorManager.ShouldBlockCloseCommand(editor, hasUnsavedPackFiles))
            {
                IsClosingWithoutPrompt = true;
                return;
            }

            IsClosingWithoutPrompt = MessageBox.Show(
                "You have unsaved changes. Do you want to quit without saving?",
                "Quit Without Saving",
                MessageBoxButton.YesNo) == MessageBoxResult.Yes;
        }

        [RelayCommand] void CloseTool(IEditorInterface tool) => EditorManager.CloseTool(tool);
      
        [RelayCommand] void CloseOtherTools(IEditorInterface tool) => EditorManager.CloseOtherTools(tool);
        [RelayCommand] void CloseAllTools(IEditorInterface tool) => EditorManager.CloseAllTools(tool);
        [RelayCommand] void CloseToolsToLeft(IEditorInterface tool) => EditorManager.CloseToolsToLeft(tool);
        [RelayCommand] void CloseToolsToRight(IEditorInterface tool) => EditorManager.CloseToolsToRight(tool);

        public bool AllowDrop(IEditorInterface node, IEditorInterface targetNode = default, bool insertAfterTargetNode = default) => true;
        public bool Drop(IEditorInterface node, IEditorInterface targetNode = default, bool insertAfterTargetNode = default) => EditorManager.Drop(node, targetNode, insertAfterTargetNode);

        private void SetStatusBarEditablePackFile(PackFileContainerSetAsMainEditableEvent e)
        {
            var container = e.Container;
            if (container == null || container.ContainerType != PackFileContainerType.Normal)
            {
                EditablePackFile = container != null ? $"Active project: {container.Name}" : "Active project: None set";
                return;
            }

            // Editing a .pack in place: say so, which game's rules a save follows, and when there is no backup
            var gameType = container.PackFileSettings.GameVersion ?? _settingsService.CurrentSettings.CurrentGame;
            var gameName = GameInformationDatabase.GetGameById(gameType).DisplayName;
            var text = string.Format(_localizationManager.Get("MainWindow.StatusBar.DirectPack"), container.Name, gameName);
            // Either backup counts: the fork's copy to the backup folder or upstream's .pack1
            var settings = _settingsService.CurrentSettings;
            if (settings.BackupBeforeSave == false && settings.BackupPacksOnFirstSave == false)
                text += _localizationManager.Get("MainWindow.StatusBar.NoBackup");
            EditablePackFile = text;
        }
    }
}
