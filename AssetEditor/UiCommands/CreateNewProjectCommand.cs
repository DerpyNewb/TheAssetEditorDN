using System.IO;
using CommonControls.BaseDialogs;
using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace AssetEditor.UiCommands
{
    public class CreateNewProjectCommand : IAeCommand
    {
        private readonly IPackFileService _packFileService;
        private readonly IStandardDialogs _standardDialogs;
        private readonly ISystemFolderContainerFactory _systemFolderContainerFactory;
        private readonly ApplicationSettingsService _applicationSettingsService;

        public CreateNewProjectCommand(
            IPackFileService packFileService,
            IStandardDialogs standardDialogs,
            ISystemFolderContainerFactory systemFolderContainerFactory,
            ApplicationSettingsService applicationSettingsService)
        {
            _packFileService = packFileService;
            _standardDialogs = standardDialogs;
            _systemFolderContainerFactory = systemFolderContainerFactory;
            _applicationSettingsService = applicationSettingsService;
        }

        public void Execute()
        {
            var window = new NewPackFileWindow();
            if (window.ShowDialog() != true)
                return;

            if (string.IsNullOrWhiteSpace(window.SelectedFolderPath))
            {
                _standardDialogs.ShowDialogBox("No folder was selected", "Error");
                return;
            }

            if (string.IsNullOrWhiteSpace(window.SelectedOutputFolderPath))
            {
                _standardDialogs.ShowDialogBox("No output folder was selected", "Error");
                return;
            }

            var projectFolderName = Path.GetFileName(window.SelectedFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(projectFolderName))
                projectFolderName = "project";

            var outputPath = Path.Combine(window.SelectedOutputFolderPath, projectFolderName + ".pack");

            using var busy = BusyWork.Doing($"Opening the project {projectFolderName}");
            var folderPack = _systemFolderContainerFactory.Create(window.SelectedFolderPath);
            folderPack.PackFileSettings.SaveLocationPath = outputPath;
            folderPack.PackFileSettings.EnablePackFileCorruptionDetection = window.EnablePackFileCorruptionDetection;
            if (folderPack.PackFileSettings.GameVersion == null)
                folderPack.PackFileSettings.GameVersion = _applicationSettingsService.CurrentSettings.CurrentGame;
            folderPack.SaveSettings();
            _packFileService.AddContainer(folderPack);
            _packFileService.SetEditablePack(folderPack);
        }
    }
}
