using Shared.Core.PackFiles;

namespace Shared.GameFormats.DB
{
    /// <summary>A decoded row, with the pack and the table file it came from.</summary>
    public record DbRow(IReadOnlyDictionary<string, object> Cells, int Version, string Pack, string File)
    {
        public string Text(string column) => Cells.TryGetValue(column, out var value) ? value as string ?? "" : "";
        public bool Flag(string column, bool missing = false) => Cells.TryGetValue(column, out var value) ? value is true : missing;
        public int Number(string column, int missing = 0) => Cells.TryGetValue(column, out var value) && value is int number ? number : missing;
    }

    /// <summary>A DB table's rows from every loaded pack, in load order, each with its pack and table file. A key in several files is
    /// kept once per file: which copy the game keeps is not measured (docs/superpowers/specs/2026-10-13-unit-simulator-personalities-design.md, 3.6).</summary>
    public static class DbTables
    {
        public static List<DbRow> Read(IPackFileService pfs, string table)
        {
            var folder = @"db\" + table;
            var rows = new List<DbRow>();
            foreach (var container in pfs.GetAllPackfileContainers())
            {
                if (!container.GetAllFilesByFolder().TryGetValue(folder, out var names))
                    continue;
                foreach (var name in names)
                {
                    var path = folder + @"\" + name;
                    if (container.FindFile(path) is not { } file)
                        continue;
                    var shown = path.Replace('\\', '/');
                    var (version, read) = DbTableReader.Read(file.DataSource.ReadData(), table, $"{container.Name}: {shown}");
                    rows.AddRange(read.Select(x => new DbRow(x, version, container.Name, shown)));
                }
            }
            return rows;
        }
    }
}
