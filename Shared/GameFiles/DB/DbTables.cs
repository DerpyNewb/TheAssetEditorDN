using System.IO;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace Shared.GameFormats.DB
{
    /// <summary>A decoded row, with the pack and the table file it came from.</summary>
    public record DbRow(IReadOnlyDictionary<string, object> Cells, int Version, string Pack, string File)
    {
        public string Text(string column) => Cells.TryGetValue(column, out var value) ? value as string ?? "" : "";
        public bool Flag(string column, bool missing = false) => Cells.TryGetValue(column, out var value) ? value is true : missing;
        public int Number(string column, int missing = 0) => Cells.TryGetValue(column, out var value) && value is int number ? number : missing;
    }

    /// <summary>A DB table's rows from every loaded pack, each with its pack and table file, ranked as the game ranks them: the copy of
    /// a key the game keeps comes first. Measured in game 2026-11-18 (docs/MOD_STRUCTURE.md, "Which copy of a DB row wins"): a mod's
    /// row beats CA's, whatever its file is called; among mods, the row in the table file whose name sorts first wins, whatever pack
    /// holds it; and two mods' files at one path are not merged: the mod listed first keeps its whole file and the other's rows are
    /// gone. Within one file the first row wins.</summary>
    public static class DbTables
    {
        /// <param name="problems">With a list, a file that does not decode is skipped and named in it; without one, it throws.</param>
        public static List<DbRow> Read(IPackFileService pfs, string table, List<string>? problems = null)
        {
            var folder = @"db\" + table;
            var packs = pfs.GetAllPackfileContainers();
            // ponytail: the mod list's order is not known here, so a mod's pack name stands in for it, as in the Mod Manager's default list.
            var mods = packs.Where(x => !x.IsCaPackFile).OrderBy(x => x.Name, StringComparer.Ordinal)
                .SelectMany(x => Files(x, folder))
                .DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Name, StringComparer.Ordinal);
            var rows = new List<DbRow>();
            foreach (var (container, name) in mods.Concat(packs.Where(x => x.IsCaPackFile).SelectMany(x => Files(x, folder))))
            {
                var path = folder + @"\" + name;
                if (container.FindFile(path) is not { } file)
                    continue;
                var shown = path.Replace('\\', '/');
                try
                {
                    var (version, read) = DbTableReader.Read(file.DataSource.ReadData(), table, $"{container.Name}: {shown}");
                    rows.AddRange(read.Select(x => new DbRow(x, version, container.Name, shown)));
                }
                catch (InvalidDataException e) when (problems != null)
                {
                    problems.Add(e.Message);
                }
            }
            return rows;
        }

        static IEnumerable<(IPackFileContainer Pack, string Name)> Files(IPackFileContainer pack, string folder) =>
            pack.GetAllFilesByFolder().TryGetValue(folder, out var names) ? names.Select(x => (pack, x)) : [];
    }
}
