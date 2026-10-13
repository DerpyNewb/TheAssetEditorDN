using Shared.EmbeddedResources;

namespace Shared.GameFormats.DB
{
    /// <summary>One column of one version of a DB table, in binary order.</summary>
    public record DbField(string Name, string Type, bool IsKey);

    /// <summary>The column layouts of the DB tables the fork decodes, by table and version: the schema extract the workspace's
    /// tools/extract_db_schema.py writes out of RPFM's schema_wh3.ron. A table it lacks has no versions.</summary>
    public static class DbSchema
    {
        static Dictionary<string, Dictionary<int, List<DbField>>>? _tables;

        public static IReadOnlyDictionary<int, List<DbField>> Versions(string table) =>
            (_tables ??= Parse(ResourceLoader.LoadString("Resources.Db.schema_wh3_extract.txt"))).TryGetValue(table, out var versions)
                ? versions
                : new Dictionary<int, List<DbField>>();

        /// <summary>'#' comments; '@table;version' opens a block; then 'name;type;1|0' per field, 1 for a key.</summary>
        public static Dictionary<string, Dictionary<int, List<DbField>>> Parse(string text)
        {
            var tables = new Dictionary<string, Dictionary<int, List<DbField>>>();
            List<DbField>? fields = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;
                var cells = line.TrimStart('@').Split(';');
                if (line.StartsWith('@'))
                {
                    if (!tables.TryGetValue(cells[0], out var versions))
                        tables[cells[0]] = versions = [];
                    versions[int.Parse(cells[1])] = fields = [];
                }
                else
                {
                    fields!.Add(new DbField(cells[0], cells[1], cells[2] == "1"));
                }
            }
            return tables;
        }
    }
}
