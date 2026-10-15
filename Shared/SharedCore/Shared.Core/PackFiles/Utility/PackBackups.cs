using System.Globalization;
using System.Text.RegularExpressions;
using Shared.Core.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Models.Containers;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace Shared.Core.PackFiles.Utility
{
    /// <summary>Copies of a pack kept in the backup folder, never beside the pack: the game loads every .pack in its data folder.
    /// One is taken before each save over a pack, and the autosaves of a .pack edited in place go here too. Named
    /// <c>name.kind.yyyyMMdd-HHmmss-fff.pack</c>; past <see cref="ApplicationSettings.BackupsKept"/> for a pack, the oldest go.</summary>
    public static partial class PackBackups
    {
        public const string BeforeSave = "beforesave";
        public const string Autosave = "autosave";
        const string Stamp = "yyyyMMdd-HHmmss-fff";

        public static string Folder(ApplicationSettings settings) =>
            string.IsNullOrWhiteSpace(settings.BackupFolder) ? DirectoryHelper.BackupDirectory : settings.BackupFolder;

        /// <summary>The pack as it is on disk, before a save replaces it. Null when nothing is there yet.</summary>
        public static string? CopyBeforeSave(string packPath, ApplicationSettings settings)
        {
            if (!File.Exists(packPath))
                return null;

            var target = NewPath(packPath, BeforeSave, settings);
            File.Copy(packPath, target);
            Prune(packPath, settings);
            return target;
        }

        /// <summary>The pack as it is in the editor, read back before it counts. Only a .pack edited in place: a project writes
        /// each file to its folder as it is saved, so nothing of it lives only in memory. Null for anything else.</summary>
        public static string? WriteAutosave(IPackFileContainer container, ApplicationSettings settings, GameInformation game)
        {
            if (container is not PackFileContainer pack || pack.IsReadOnly)
                return null;

            var packPath = pack.SystemFilePath ?? pack.Name;
            var target = NewPath(packPath, Autosave, settings);
            try
            {
                List<WrittenFile> written;
                using (var stream = new FileStream(target, FileMode.CreateNew))
                using (var writer = new BinaryWriter(stream))
                    written = PackFileSerializerWriter.SaveToByteArray(target, pack, writer, game);
                PackFileSerializerWriter.Verify(target, written);
            }
            catch
            {
                File.Delete(target);
                throw;
            }

            Prune(packPath, settings);
            return target;
        }

        /// <summary>This pack's backups, newest first.</summary>
        public static List<string> List(string packPath, ApplicationSettings settings)
        {
            var folder = Folder(settings);
            if (!Directory.Exists(folder))
                return [];

            var name = Path.GetFileNameWithoutExtension(packPath);
            return Directory.GetFiles(folder, name + ".*.pack")
                .Select(x => (Path: x, Match: BackupName().Match(Path.GetFileName(x))))
                .Where(x => x.Match.Success && string.Equals(x.Match.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => DateTime.ParseExact(x.Match.Groups["stamp"].Value, Stamp, CultureInfo.InvariantCulture))
                .Select(x => x.Path)
                .ToList();
        }

        static void Prune(string packPath, ApplicationSettings settings)
        {
            foreach (var old in List(packPath, settings).Skip(Math.Max(1, settings.BackupsKept)))
                File.Delete(old);
        }

        static string NewPath(string packPath, string kind, ApplicationSettings settings)
        {
            var folder = Folder(settings);
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(packPath)}.{kind}.{DateTime.Now.ToString(Stamp, CultureInfo.InvariantCulture)}.pack");
        }

        [GeneratedRegex(@"^(?<name>.+)\.(beforesave|autosave)\.(?<stamp>\d{8}-\d{6}-\d{3})\.pack$", RegexOptions.IgnoreCase)]
        private static partial Regex BackupName();
    }
}
