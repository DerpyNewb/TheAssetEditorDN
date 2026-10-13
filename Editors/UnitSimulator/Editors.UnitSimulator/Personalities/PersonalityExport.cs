using System.Globalization;
using System.IO;
using System.Text;
using Shared.GameFormats.DB;

namespace Editors.UnitSimulator.Personalities
{
    /// <summary>The attached personalities as two RPFM TSVs, one per table (spec section 4): key columns first, then the rest, each in
    /// binary order; line 2 RPFM's '#table;version;db/table/name' marker. Never a pack or a DB file.</summary>
    public static class PersonalityExport
    {
        /// <summary>Null when both files are written, else why not.</summary>
        public static string? Write(string folder, string name, IReadOnlyList<PersonalityPart> parts, int personalitiesVersion, int junctionsVersion)
        {
            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(name))
                return "Give the export a folder and a name.";
            var noUnit = parts.Where(x => x.LandUnit.Trim() == "").Select(x => x.Key).ToList();
            if (noUnit.Count != 0)
                return $"No land_unit on {string.Join(", ", noUnit)}: a junction row needs one.";
            var twice = parts.GroupBy(x => (x.Key, Unit: x.LandUnit.Trim())).Where(x => x.Count() > 1).Select(x => $"{x.Key.Key} on {x.Key.Unit}").ToList();
            if (twice.Count != 0)
                return $"One personality twice on one land unit: {string.Join(", ", twice)}. The junction's key is the pair.";
            if (!DbSchema.Versions(PersonalityRows.Personalities).TryGetValue(personalitiesVersion, out var personalityFields))
                return $"{PersonalityRows.Personalities} v{personalitiesVersion} is not in the schema extract.";
            if (!DbSchema.Versions(PersonalityRows.Junctions).TryGetValue(junctionsVersion, out var junctionFields))
                return $"{PersonalityRows.Junctions} v{junctionsVersion} is not in the schema extract.";

            var personalities = Tsv(PersonalityRows.Personalities, personalitiesVersion, name, personalityFields, parts.Select(PersonalityCells));
            var junctions = Tsv(PersonalityRows.Junctions, junctionsVersion, name, junctionFields, parts.Select(JunctionCells));
            try
            {
                foreach (var (table, text) in new[] { (PersonalityRows.Personalities, personalities), (PersonalityRows.Junctions, junctions) })
                {
                    Directory.CreateDirectory(Path.Combine(folder, table));
                    File.WriteAllText(Path.Combine(folder, table, name + ".tsv"), text);
                }
                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return $"Not written: {e.Message}";
            }
        }

        // A part's values by column; a column it does not hold is the source row's, else its type's empty value (Cell).
        static Dictionary<string, object> PersonalityCells(PersonalityPart part) => new(part.Source.Cells)
        {
            ["key"] = part.Key,
            ["man_animations_table"] = part.Table,
            ["variant"] = part.Variant,
        };

        static Dictionary<string, object> JunctionCells(PersonalityPart part) =>
            new(part.SourceJunction?.Cells ?? new Dictionary<string, object>())
            {
                ["battle_personality"] = part.Key,
                ["land_unit"] = part.LandUnit.Trim(),
                ["attach"] = part.Attach,
                ["riders_attachment_point"] = part.Bone,
                ["engine_articulation_index"] = part.EngineArticulationIndex,
                ["jettison_rider"] = part.JettisonRider,
            };

        static string Tsv(string table, int version, string name, List<DbField> fields, IEnumerable<Dictionary<string, object>> rows)
        {
            var columns = fields.Where(x => x.IsKey).Concat(fields.Where(x => !x.IsKey)).ToList();
            var text = new StringBuilder();
            text.Append(string.Join('\t', columns.Select(x => x.Name))).Append('\n');
            text.Append($"#{table};{version};db/{table}/{name}").Append('\t', columns.Count - 1).Append('\n');
            foreach (var row in rows)
                text.Append(string.Join('\t', columns.Select(x => Cell(row, x)))).Append('\n');
            return text.ToString();
        }

        static string Cell(Dictionary<string, object> row, DbField field) => row.TryGetValue(field.Name, out var value)
            ? value switch
            {
                bool flag => flag ? "true" : "false",
                float number => number.ToString(CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
            }
            : field.Type switch
            {
                "Boolean" => "false",
                "I32" or "F32" => "0",
                _ => "",
            };
    }
}
