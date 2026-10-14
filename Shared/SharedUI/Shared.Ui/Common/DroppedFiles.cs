using System.IO;
using System.Windows;
using Shared.Core.PackFiles.Models;

namespace Shared.Ui.Common
{
    /// <summary>What a drop carries: the pack tree drags a PackFile (PackFileBrowserView), Windows Explorer drags paths.</summary>
    public static class DroppedFiles
    {
        public static PackFile? PackFile(IDataObject data) => data.GetData(typeof(PackFile)) as PackFile;

        public static IReadOnlyList<string> Packs(IDataObject data) =>
            data.GetData(DataFormats.FileDrop) is string[] paths
                ? paths.Where(x => string.Equals(Path.GetExtension(x), ".pack", StringComparison.OrdinalIgnoreCase)).ToList()
                : [];
    }
}
