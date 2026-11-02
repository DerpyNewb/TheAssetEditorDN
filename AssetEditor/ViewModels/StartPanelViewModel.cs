using System;
using AssetEditor.UiCommands;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shared.Core.Events;
using Shared.Core.Settings;

namespace AssetEditor.ViewModels
{
    /// <summary>The opening panel: a project (recommended), a .pack edited in place, or a recent one, as the File menu offers them.
    /// A pick's command runs only after the panel closes, so its own dialog is not left behind it.</summary>
    public partial class StartPanelViewModel(ApplicationSettingsService settings, IUiCommandFactory commands) : ObservableObject
    {
        Action? _picked;

        [ObservableProperty] bool _dontShowAgain;

        public event Action? CloseRequested;

        /// <summary>File > Open Recent's packs and folders, the latest first.</summary>
        public List<RecentPackFileItem> Recent => settings.CurrentSettings.RecentPackFiles.Reverse()
            .Select(info => new RecentPackFileItem(info.Path, info.ContainerType, info.IsReadOnly,
                () => Pick(() => commands.Create<OpenRecentPackCommand>(x => x.Configure(info)).Execute())))
            .ToList();

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
