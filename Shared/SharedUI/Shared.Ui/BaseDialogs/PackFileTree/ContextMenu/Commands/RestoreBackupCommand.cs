using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.PackFileTree.Utility;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    /// <summary>
    /// A .pack put back to one of its backups (Settings > Saving): the autosaves and the copies taken before each save, newest
    /// first. From the pack's own menu when it is open, or from File > Restore Backup when it is not, or is gone. An open pack is
    /// closed, the backup copied over it, and the pack opened again; what it replaced is backed up.
    /// </summary>
    public class RestoreBackupCommand(IPackFileService packFileService, IPackFileContainerLoader packFileContainerLoader, IStandardDialogs standardDialogs,
        ApplicationSettingsService settingsService, IScopedLogger scopedLogger) : IContextMenuCommand, IContextMenuChoices
    {
        private readonly ILogger _logger = scopedLogger.ForContext<RestoreBackupCommand>();

        public string GetDisplayName(TreeNode node) => "Restore Backup";
        public bool ShouldAdd(TreeNode node) => node.NodeType == NodeType.Root && Pack(node) is { } pack && Backups(pack.SystemFilePath!).Count != 0;
        public bool IsEnabled(TreeNode node) => true;
        public void Configure(TreeNode node) { }

        // The entry opens the list; each choice restores.
        public void Execute() { }

        public IEnumerable<(string Name, Action Choose)> Choices(TreeNode node) =>
            Pack(node) is { SystemFilePath: { } path } ? Choices(path) : [];

        /// <summary>A pack's backups, newest first, each labelled with when it was taken, its kind and its size.</summary>
        public IEnumerable<(string Name, Action Choose)> Choices(string packPath) =>
            Backups(packPath).Select(backup => (Label(backup), (Action)(() => Restore(packPath, backup))));

        public static string Label(string backup)
        {
            var (taken, kind) = PackBackups.Describe(backup);
            return $"{taken:yyyy-MM-dd HH:mm:ss}   {(kind == PackBackups.Autosave ? "Autosave" : "Before save")}   {new FileInfo(backup).Length / 1048576.0:0.0} MB";
        }

        // A .pack opened to edit in place; a project's path is its folder, and a game pack is read-only.
        static IPackFileContainer? Pack(TreeNode node) =>
            TreeNodeHelper.GetPackFileContainer(node) is { IsReadOnly: false, SystemFilePath: { } path } pack && File.Exists(path) ? pack : null;

        public List<string> Backups(string packPath) => PackBackups.List(packPath, settingsService.CurrentSettings);

        public void Restore(string packPath, string backup)
        {
            var (taken, _) = PackBackups.Describe(backup);
            var question = $"Put {Path.GetFileName(packPath)} back to how it was at {taken:yyyy-MM-dd HH:mm:ss}?\n\n" +
                "Unsaved changes to it in the editor are lost. The .pack as it is on disk now is backed up first, so this can be taken back the same way.";
            if (standardDialogs.ShowYesNoBox(question, "Restore Backup") != ShowMessageBoxResult.OK)
                return;

            var open = packFileService.GetAllPackfileContainers().FirstOrDefault(x => string.Equals(x.SystemFilePath, packPath, StringComparison.OrdinalIgnoreCase));
            var wasEditable = open != null && packFileService.GetEditablePack() == open;
            _logger.Here().Information($"Restoring '{packPath}' from '{backup}'");
            if (open != null)
                packFileService.UnloadPackContainer(open);
            try
            {
                PackBackups.Restore(backup, packPath, settingsService.CurrentSettings);
            }
            catch (Exception e)
            {
                _logger.Here().Error($"Restoring '{packPath}' failed: {e}");
                standardDialogs.ShowDialogBox($"The backup could not be restored, and the .pack is unchanged:\n{e.Message}");
            }

            if (open == null)
                return;
            var reopened = packFileContainerLoader.CreateFromPackFile(PackFileContainerType.Normal, packPath, false);
            packFileService.AddContainer(reopened, true);
            if (wasEditable)
                packFileService.SetEditablePack(reopened);
        }
    }
}
