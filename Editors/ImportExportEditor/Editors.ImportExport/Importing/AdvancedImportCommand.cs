using Editors.ImportExport.ContextMenu;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;
using Shared.Ui.BaseDialogs.PackFileTree.Utility;

namespace Editors.ImportExport.Importing
{
    public class AdvancedImportCommand(IPackFileService packFileService, IImportFileContextMenuHelper importFileContextMenuHelper) : IContextMenuCommand
    {
        public string GetDisplayName(TreeNode node) => "Import 3D model or image...";
        public bool ShouldAdd(TreeNode node)
        {
            var container = TreeNodeHelper.GetPackFileContainer(node);
            return node.NodeType is NodeType.Directory or NodeType.Root && container is { IsCaPackFile: false };
        }

        public bool IsEnabled(TreeNode node) => true;

        private TreeNode _node = null!;

        public void Configure(TreeNode node)
        {
            _node = node;
        }

        public void Execute()
        {
            var container = TreeNodeHelper.GetPackFileContainer(_node);
            if (container == null)
                return;

            importFileContextMenuHelper.ShowDialog(container, _node);
        }
    }
}
