using Shared.Core.PackFiles;
using Shared.GameFormats.DB;

namespace Editors.UnitSimulator.Personalities
{
    /// <summary>The three tables a personality is read from, out of every loaded pack. A personality key in several files is listed
    /// once per file (spec 3.6); a variant, or a land unit's junction row for one personality, is the last loaded copy.</summary>
    // ponytail: the last loaded copy of a variant or a junction pair is used; which copy the game keeps is not measured (spec 8).
    public sealed class PersonalityRows
    {
        public const string Personalities = "battle_personalities_tables";
        public const string Junctions = "land_units_to_battle_personalities_junctions_tables";
        public const string Variants = "variants_tables";

        public List<DbRow> All { get; }
        readonly ILookup<string, DbRow> _byKey;
        readonly List<DbRow> _junctions;
        readonly List<DbRow> _variants;
        List<string>? _variantNames;

        public PersonalityRows(IPackFileService pfs)
        {
            All = DbTables.Read(pfs, Personalities);
            _byKey = All.ToLookup(x => x.Text("key"));
            _junctions = DbTables.Read(pfs, Junctions);
            _variants = DbTables.Read(pfs, Variants);
        }

        public List<DbRow> Copies(string key) => [.. _byKey[key]];

        /// <summary>Every variant key, sorted, for the variant picker.</summary>
        public List<string> VariantNames => _variantNames ??= [.. _variants.Select(x => x.Text("variant_name")).Distinct().Order(StringComparer.Ordinal)];

        /// <summary>The first junction row naming the personality, whose bone a part added on its own starts on (spec 3.4).</summary>
        public DbRow? JunctionOf(string personality) => _junctions.FirstOrDefault(x => x.Text("battle_personality") == personality);

        public List<DbRow> JunctionsOfUnit(string landUnit) =>
            [.. _junctions.Where(x => x.Text("land_unit") == landUnit).GroupBy(x => x.Text("battle_personality")).Select(x => x.Last())];

        public DbRow? Variant(string name) => _variants.LastOrDefault(x => x.Text("variant_name") == name);

        public List<string> TechFoldersOf(string variantFileName) =>
            [.. _variants.Where(x => string.Equals(x.Text("variant_filename"), variantFileName, StringComparison.OrdinalIgnoreCase) && x.Text("tech_folder") != "")
                .Select(x => x.Text("tech_folder")).Distinct(StringComparer.OrdinalIgnoreCase)];

        /// <summary>The version an export writes: the newest among the loaded files of the table, which is CA's own, since a mod cannot
        /// ship one newer than the game reads; with none loaded, the newest in the schema extract.</summary>
        public int ExportVersion(string table)
        {
            var rows = table == Personalities ? All : table == Junctions ? _junctions : _variants;
            return rows.Count != 0 ? rows.Max(x => x.Version) : DbSchema.Versions(table).Keys.Max();
        }
    }
}
