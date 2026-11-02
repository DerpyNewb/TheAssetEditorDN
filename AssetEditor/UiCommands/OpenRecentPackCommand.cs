using System.IO;
using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;

namespace AssetEditor.UiCommands
{
    /// <summary>A pack or folder from File > Open Recent or the start panel's Recent list, opened as it was opened before.</summary>
    public class OpenRecentPackCommand(IPackFileService packFileService, IPackFileContainerLoader packFileContainerLoader) : IAeCommand
    {
        RecentPackFileInfo? _recent;

        public void Configure(RecentPackFileInfo recent) => _recent = recent;

        public void Execute()
        {
            var info = _recent!;
            using var busy = BusyWork.Doing($"Opening {Path.GetFileName(info.Path)}");
            var container = info.ContainerType == PackFileContainerType.SystemFolder
                ? packFileContainerLoader.CreateFromSystemFolder(info.Path)
                : packFileContainerLoader.CreateFromPackFile(info.ContainerType, info.Path, info.IsReadOnly);

            if (container == null)
            {
                System.Windows.MessageBox.Show($"Unable to load packfiles {info.Path}");
                return;
            }

            packFileService.AddContainer(container, true);
        }
    }
}
