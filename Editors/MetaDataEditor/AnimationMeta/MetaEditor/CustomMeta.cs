using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace Editors.AnimationMeta.MetaEditor
{
    /// <summary>Where a meta comes from, and the custom copy offered in its place. A meta read from CA's packs or another mod's and
    /// saved at its own path overrides that file for every bin that names it; a copy at a path of your own changes only the bins
    /// pointed at it.</summary>
    public static class CustomMeta
    {
        public const string Extension = ".anm.meta";

        /// <summary>Where the file at <paramref name="path"/> is read from, the last pack loaded that has it, as FindFile picks: "vanilla"
        /// for a CA pack, "your pack" for the editable pack, else that pack's name; "" when no pack has it. Asked by path because
        /// GetPackFileContainer also matches by file name alone, so CA's file would answer for your override of it.</summary>
        public static string Source(IPackFileService pfs, string path)
        {
            var container = string.IsNullOrWhiteSpace(path) ? null : pfs.GetAllPackfileContainers().LastOrDefault(x => x.FindFile(path) != null);
            return container == null ? "" : container == pfs.GetEditablePack() ? "your pack" : container.IsCaPackFile ? "vanilla" : container.Name;
        }

        /// <summary>The prefix offered for a custom meta's file name: the last one used, else "custom".</summary>
        public static string Prefix(ApplicationSettingsService settings) =>
            string.IsNullOrWhiteSpace(settings.CurrentSettings.CustomMetaPrefix) ? "custom" : settings.CurrentSettings.CustomMetaPrefix;

        /// <summary>The path beside <paramref name="path"/> whose file name starts with the prefix and an underscore, unless it does already.</summary>
        public static string Offered(string path, string prefix)
        {
            path = Normalise(path);
            var folder = path[..(path.LastIndexOf('/') + 1)];
            var name = path[folder.Length..];
            return name.StartsWith(prefix + "_", StringComparison.Ordinal) ? path : folder + prefix + "_" + name;
        }

        /// <summary>The save dialog, starting at <paramref name="offered"/>: the path picked, lower-case with forward slashes and
        /// ending in .anm.meta, or null when cancelled. A file name of the form prefix_name, name being <paramref name="original"/>'s,
        /// makes that prefix the one offered next time.</summary>
        public static string? Ask(IStandardDialogs dialogs, IPackFileService pfs, ApplicationSettingsService settings, string original, string offered)
        {
            var pick = dialogs.DisplaySaveDialog(pfs, [Extension], offered);
            if (pick == null || !pick.Result || string.IsNullOrWhiteSpace(pick.SelectedFilePath))
                return null;
            var path = Normalise(pick.SelectedFilePath);
            if (!path.EndsWith(".meta", StringComparison.Ordinal))
                path += Extension;

            var name = path[(path.LastIndexOf('/') + 1)..];
            var originalName = Normalise(original)[(Normalise(original).LastIndexOf('/') + 1)..];
            if (name.Length > originalName.Length + 1 && name.EndsWith("_" + originalName, StringComparison.Ordinal))
            {
                var prefix = name[..^(originalName.Length + 1)];
                if (prefix != settings.CurrentSettings.CustomMetaPrefix)
                {
                    settings.CurrentSettings.CustomMetaPrefix = prefix;
                    settings.Save();
                }
            }
            return path;
        }

        /// <summary>A pack path as CA's bins name one: trimmed, forward slashes, lower-case.</summary>
        public static string Normalise(string path) => path.Trim().Replace('\\', '/').ToLowerInvariant();
    }
}
