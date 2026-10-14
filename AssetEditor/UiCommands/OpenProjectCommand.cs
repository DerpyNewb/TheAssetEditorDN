using System.IO;
using System.Windows.Forms;
using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace AssetEditor.UiCommands
{
    public class OpenProjectCommand : IAeCommand
    {
        private const string ProjectSettingsFileName = "aeproject.json";

        private readonly IPackFileService _packFileService;
        private readonly ISystemFolderContainerFactory _systemFolderContainerFactory;
        private readonly IStandardDialogs _standardDialogs;
        private readonly ApplicationSettingsService _applicationSettingsService;
        private readonly LocalizationManager _localizationManager;

        public OpenProjectCommand(
            IPackFileService packFileService,
            ISystemFolderContainerFactory systemFolderContainerFactory,
            IStandardDialogs standardDialogs,
            ApplicationSettingsService applicationSettingsService,
            LocalizationManager localizationManager)
        {
            _packFileService = packFileService;
            _systemFolderContainerFactory = systemFolderContainerFactory;
            _standardDialogs = standardDialogs;
            _applicationSettingsService = applicationSettingsService;
            _localizationManager = localizationManager;
        }

        public void Execute()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = _localizationManager.Get("OpenProjectCommand.SelectProjectFolderDescription"),
                UseDescriptionForTitle = true
            };

            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            if (File.Exists(Path.Combine(dialog.SelectedPath, ProjectSettingsFileName)) == false)
            {
                _standardDialogs.ShowDialogBox(
                    _localizationManager.Get("OpenProjectCommand.ProjectSettingsFileMissing"),
                    _localizationManager.Get("OpenProjectCommand.ErrorTitle"));
                return;
            }

            using var busy = BusyWork.Doing($"Opening the project {Path.GetFileName(dialog.SelectedPath)}");
            var container = _systemFolderContainerFactory.Create(dialog.SelectedPath);
            if (container.PackFileSettings.GameVersion == null)
            {
                container.PackFileSettings.GameVersion = _applicationSettingsService.CurrentSettings.CurrentGame;
                container.SaveSettings();
            }
            _packFileService.AddContainer(container);
            _packFileService.SetEditablePack(container);
        }
    }
}
