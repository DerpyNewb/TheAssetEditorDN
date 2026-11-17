using Shared.Core.PackFiles.Models;

namespace Shared.Ui.BaseDialogs.PackFileTree
{
    /// <summary>
    /// Takes a file dragged in from Windows and dropped on the Pack Explorer. Implemented outside
    /// Shared.Ui (the importers live in the editors), registered in DI, handed to the tree by
    /// PackFileTreeViewFactory.
    /// </summary>
    public interface IExternalFileDropHandler
    {
        bool CanHandle(string diskPath);
        void Handle(string diskPath, IPackFileContainer container, string packFolder);
    }
}
