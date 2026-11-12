using System.Globalization;
using Editors.VfxEditor.Document;

namespace Editors.VfxEditor.VariantMesh
{
    /// <summary>A problem on a grid row. A slot's problems go on its first row, an entry's on its own.</summary>
    public sealed record VmdFinding(int Row, bool IsError, string Message);

    /// <summary>The checks of spec section 5, cheap enough to run after every edit. None blocks an edit or a save.</summary>
    public static class VmdChecks
    {
        /// <summary>The META_DATA keys CA uses in 3,483 files (2026-11-13), each but the first two followed by ":value".</summary>
        public static readonly string[] TagKeys = ["equipment", "is_stump", "audio_armour_type", "audio_entity_type", "audio_melee_weapon_type",
            "audio_shield_type", "audio_switch", "audio_actor_id", "audio_material", "metadata_id", "audio_group_movement_foley"];

        static readonly string[] s_modelExtensions = [".wsmodel", ".rigid_model_v2"];

        public static List<VmdFinding> Run(VfxDocument document, IReadOnlyList<VmdRowRef> rows, Func<string, bool> exists, string? ownPath)
        {
            var findings = new List<VmdFinding>();
            for (var row = 0; row < rows.Count; row++)
            {
                void Warn(string message) => findings.Add(new VmdFinding(row, false, message));
                var slot = document.At(rows[row].Slot);

                if (rows[row].IsFirst)
                {
                    if (string.IsNullOrWhiteSpace(slot.Attr("name")))
                        findings.Add(new VmdFinding(row, true, "This slot has no name."));
                    if (slot.Attr("probability") is { } probability && !float.TryParse(probability, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                        findings.Add(new VmdFinding(row, true, $"Probability \"{probability}\" is not a number. Use a dot for decimals."));
                    var name = slot.Attr("name") ?? "";
                    if (name.Length != 0 && slot.Parent!.ChildrenNamed("SLOT").Count(s => string.Equals(s.Attr("name"), name, StringComparison.OrdinalIgnoreCase)) > 1)
                        Warn($"Another slot at this level is also named \"{name}\".");
                }

                if (VmdLayout.EntryOf(document, rows[row]) is not { } entry)
                    continue;
                if (entry.Name == "VARIANT_MESH_REFERENCE")
                {
                    var definition = entry.Attr("definition") ?? "";
                    if (!definition.EndsWith(".variantmeshdefinition", StringComparison.OrdinalIgnoreCase))
                        Warn($"A reference names a .variantmeshdefinition, and \"{definition}\" is not one.");
                    else if (ownPath != null && Same(definition, ownPath))
                        Warn("This file references itself.");
                    if (definition.Length != 0 && !exists(definition))
                        Warn($"Not found in the loaded packs: {definition}");
                    continue;
                }

                var model = entry.Attr("model") ?? "";
                if (model.Length == 0)
                {
                    if (!entry.ChildrenNamed("SLOT").Any())
                        Warn("This mesh has no model and no slots under it, so it shows nothing.");
                }
                else
                {
                    if (!s_modelExtensions.Any(x => model.EndsWith(x, StringComparison.OrdinalIgnoreCase)))
                        Warn($"A model is a .wsmodel or a .rigid_model_v2, and \"{model}\" is neither.");
                    if (!exists(model))
                        Warn($"Not found in the loaded packs: {model}");
                }
                if (entry.Attr("imposter_model") is { Length: > 0 } imposter && !exists(imposter))
                    Warn($"Imposter not found in the loaded packs: {imposter}");
                foreach (var tag in VmdEdits.Tags(document, entry))
                {
                    var key = tag.Split(':')[0].Trim();
                    if (!TagKeys.Contains(key))
                        Warn($"Tag key \"{key}\" is not one CA uses ({string.Join(", ", TagKeys)}).");
                }
            }
            return findings;
        }

        static bool Same(string a, string b) => Normal(a) == Normal(b);

        static string Normal(string path) => path.Replace('/', '\\').ToLowerInvariant();
    }
}
