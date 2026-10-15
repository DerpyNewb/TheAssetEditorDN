using Shared.Core.Events;

namespace Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands
{
    public interface IContextMenuCommand : IAeCommand
    {
        public string GetDisplayName(TreeNode node);
        public bool ShouldAdd(TreeNode node);
        public bool IsEnabled(TreeNode node);
        public void Configure(TreeNode node);
    }

    /// <summary>A command whose entry opens a list to choose from, each choice its own action, rather than running itself.</summary>
    public interface IContextMenuChoices
    {
        System.Collections.Generic.IEnumerable<(string Name, System.Action Choose)> Choices(TreeNode node);
    }
}
