using Shared.Core.PackFiles.Models;
using Shared.Core.ToolCreation;

namespace Editors.Ipc
{
    public interface IExternalFileOpenExecutor
    {
        /// <summary>Returns null when the file opened, or why it did not.</summary>
        Task<string?> OpenAsync(PackFile file, bool bringToFront, bool openInExistingKitbashTab, EditorEnums? openWith, CancellationToken cancellationToken);
    }
}
