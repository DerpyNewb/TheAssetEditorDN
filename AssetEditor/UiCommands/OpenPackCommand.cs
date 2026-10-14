using System.IO;
using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;

namespace AssetEditor.UiCommands
{
    /// <summary>v0.45's File > Open: the .pack itself becomes the editable pack and Save writes back into it
    /// (PackFileContainer.SaveToDisk writes beside it and reads it back first). A damaged pack opens read-only.</summary>
    public class OpenPackCommand(IPackFileService packFileService, IPackFileContainerLoader packFileContainerLoader, IStandardDialogs standardDialogs) : IAeCommand
    {
        public void Execute()
        {
            var dialog = standardDialogs.ShowSystemOpenFileDialog(filter: "Pack files (*.pack)|*.pack");
            if (dialog.Result && dialog.FilePaths.Count != 0)
                Open(dialog.FilePaths[0]);
        }

        public void Open(string path)
        {
            // The pack tree is rebuilt after the load, still under this name.
            using var busy = BusyWork.Doing($"Opening {Path.GetFileName(path)}");
            var container = packFileContainerLoader.CreateFromPackFile(PackFileContainerType.Normal, path, false);
            packFileService.AddContainer(container, true);
        }
    }
}
