using System;
using AssetEditor.UiCommands;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shared.Core.Events;
using Shared.Core.Settings;

namespace AssetEditor.ViewModels
{
    /// <summary>The panel the app opens with: a project (recommended) or a .pack edited in place, as the File menu offers them.
    /// A pick closes the panel; its command runs once the panel has closed, so its own dialog is not left behind it.</summary>
    public partial class StartPanelViewModel(ApplicationSettingsService settings, IUiCommandFactory commands) : ObservableObject
    {
        Action? _picked;

        [ObservableProperty] bool _dontShowAgain;

        public event Action? CloseRequested;

        [RelayCommand] void NewProject() => Pick(() => commands.Create<CreateNewProjectCommand>().Execute());
        [RelayCommand] void OpenProject() => Pick(() => commands.Create<OpenProjectCommand>().Execute());
        [RelayCommand] void ProjectFromPack() => Pick(() => commands.Create<ImportPackAsAsProjectCommand>().Execute());
        [RelayCommand] void OpenPack() => Pick(() => commands.Create<OpenPackCommand>().Execute());
        [RelayCommand] void Skip() => Pick(null);

        void Pick(Action? run)
        {
            _picked = run;
            CloseRequested?.Invoke();
        }

        /// <summary>After the panel has closed, however it closed: keeps the box, then runs the pick.</summary>
        public void Closed()
        {
            if (DontShowAgain)
            {
                settings.CurrentSettings.ShowStartPanel = false;
                settings.Save();
            }
            _picked?.Invoke();
        }
    }
}
