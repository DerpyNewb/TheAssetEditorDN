using System.IO;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.PackFileTree.Utility;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    public class CopyToEditablePackCommand(IPackFileService packFileService, IStandardDialogs standardDialogs, IScopedLogger scopedLogger) : IContextMenuCommand
    {
        private readonly ILogger _logger = scopedLogger.ForContext<CopyToEditablePackCommand>();

        public string GetDisplayName(TreeNode node) => "Copy to editable pack";
        public bool ShouldAdd(TreeNode node)
        {
            var container = TreeNodeHelper.GetPackFileContainer(node);
            var editablePack = packFileService.GetEditablePack();
            return editablePack != null && container != null && editablePack != container;
        }
        public bool IsEnabled(TreeNode node) => true;

        private TreeNode _node = null!;

        public void Configure(TreeNode node)
        {
            _node = node;
        }

        public void Execute()
        {
            var editablePack = packFileService.GetEditablePack();
            if (editablePack == null)
            {
                _logger.Here().Warning($"Copy to editable pack requested for '{CommandLoggingHelper.DescribeNode(_node)}' but no editable pack is selected");
                standardDialogs.ShowDialogBox("No editable pack selected!");
                return;
            }

            var container = TreeNodeHelper.GetPackFileContainer(_node);
            if (container == null)
            {
                _logger.Here().Warning($"Copy to editable pack blocked because no container was resolved for '{CommandLoggingHelper.DescribeNode(_node)}'");
                standardDialogs.ShowDialogBox("Unable to resolve selected packfile");
                return;
            }

            using (standardDialogs.ShowWaitCursor())
            {
                var paths = _node.GetAllChildFileNodes().Select(x => x.GetFullPath()).ToList();
                // A model's textures come from the .wsmodel beside it (WsModelMaterialProvider finds it by the model's path); the
                // paths stored in the model can be stale. So a copied model brings that .wsmodel, unless the editable pack has one.
                foreach (var model in paths.Where(x => x.EndsWith(".rigid_model_v2", StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    var wsModel = Path.ChangeExtension(model, ".wsmodel");
                    if (!paths.Contains(wsModel, StringComparer.OrdinalIgnoreCase) && packFileService.FindFile(wsModel, container) != null && packFileService.FindFile(wsModel, editablePack) == null)
                        paths.Add(wsModel);
                }

                _logger.Here().Information($"Copying {paths.Count} file(s) from '{CommandLoggingHelper.DescribeNode(_node)}' to editable pack '{CommandLoggingHelper.DescribePack(editablePack)}'");
                foreach (var path in paths)
                    packFileService.CopyFileFromOtherPackFile(container, path, editablePack);

                _logger.Here().Information($"Copied {paths.Count} file(s) from '{CommandLoggingHelper.DescribeNode(_node)}' to editable pack '{CommandLoggingHelper.DescribePack(editablePack)}'");
            }
        }
    }
}
