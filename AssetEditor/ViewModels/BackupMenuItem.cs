using System.Collections.Generic;
using System.Windows.Input;

namespace AssetEditor.ViewModels
{
    /// <summary>A pack in File > Restore Backup, with its backups under it, or one of those backups, which restores it.</summary>
    public record BackupMenuItem(string Header, ICommand? Command, List<BackupMenuItem> MenuItems);
}
