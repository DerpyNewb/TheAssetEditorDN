using System.Globalization;

namespace Editors.VfxEditor.Document
{
    /// <summary>Adding and removing (spec section 6) as splices; a linked edit reaches every tier, matching modifiers by
    /// fragment and occurrence. Refusals throw ArgumentException (a bad id) or InvalidOperationException (not at this tier).</summary>
    public static class VfxEdits
    {
        static readonly string[] s_emitterSections = ["emitters", "behaviour_chains", "effect_render_stacks"];

        static SpanElement Required(VfxDocument document, string section) =>
            document.Section(section) ?? throw new InvalidOperationException($"the file has no <{section}>");

        // Properties of an emitter or render stack

        /// <summary>A tier-0 base, which the other tiers inherit, after the container's last child.</summary>
        public static Splice AddProperty(VfxDocument document, SpanElement container, string propertyXml) => document.InsertInto(container, propertyXml);

        /// <summary>Linked: every tier of it. This tier only: the tier's own element; at tier 0 the tiers that inherited the
        /// base first get an override holding it. A tier that inherits cannot drop the base's value alone.</summary>
        public static List<Splice> RemoveProperty(VfxDocument document, SpanElement container, string name, int tier, bool thisTierOnly)
        {
            var tiers = TierEdits.Tiers(container, name);
            if (!thisTierOnly)
                return tiers.Values.Select(document.Remove).ToList();
            if (!tiers.TryGetValue(tier, out var own))
                throw new InvalidOperationException($"{name} at this tier is Ultra's value, so it cannot be removed at this tier alone");
            var splices = new List<Splice>();
            if (tier == 0)
                splices.AddRange(new[] { 1, 2 }.Where(t => !tiers.ContainsKey(t)).Select(t => TierEdits.InsertTierCopy(document, tiers, own, t, null, null)));
            splices.Add(document.Remove(own));
            return splices;
        }

        // Properties of a modifier

        public static List<Splice> AddModifierProperty(VfxDocument document, SpanElement chain, int tier, string fragment, int occurrence, string propertyXml, bool thisTierOnly)
        {
            var name = VfxDocument.FromText(propertyXml).Root.Children[0].Attr("name") ?? "";
            return TierEdits.LinkedModifiers(chain, tier, fragment, occurrence, thisTierOnly)
                .Where(m => TierEdits.PropertyIn(m, name) == null)
                .Select(m => document.InsertInto(m, propertyXml)).ToList();
        }

        public static List<Splice> RemoveModifierProperty(VfxDocument document, SpanElement chain, int tier, string fragment, int occurrence, string name, bool thisTierOnly) =>
            TierEdits.LinkedModifiers(chain, tier, fragment, occurrence, thisTierOnly)
                .Select(m => TierEdits.PropertyIn(m, name)).OfType<SpanElement>()
                .Select(document.Remove).ToList();

        // Modifiers

        /// <summary>Into every block the chain has, or the tier's own; each property on its own line.</summary>
        public static List<Splice> AddModifier(VfxDocument document, SpanElement chain, int tier, string fragment, IReadOnlyList<string> properties, bool thisTierOnly)
        {
            var blocks = thisTierOnly
                ? new List<SpanElement> { TierEdits.Block(chain, tier) ?? throw new InvalidOperationException("this tier has no block; copy Ultra's block to it first") }
                : chain.ChildrenNamed("quality").ToList();
            var start = $"<modifier fragment=\"{XmlText.EscapeAttribute(fragment, '"')}\">";
            return blocks.Select(b => document.InsertInto(b, VfxDocument.Wrap(start, properties, "</modifier>", document.ChildBreak(b)))).ToList();
        }

        public static List<Splice> RemoveModifier(VfxDocument document, SpanElement chain, int tier, string fragment, int occurrence, bool thisTierOnly) =>
            TierEdits.LinkedModifiers(chain, tier, fragment, occurrence, thisTierOnly).Select(document.Remove).ToList();

        /// <summary>A tier with no block draws nothing (HANDOFF_20260829); this gives it a copy of another tier's block.</summary>
        public static Splice CopyBlock(VfxDocument document, SpanElement chain, int fromTier, int toTier)
        {
            if (TierEdits.Block(chain, toTier) != null)
                throw new InvalidOperationException($"tier {toTier} already has a block");
            var source = TierEdits.Block(chain, fromTier) ?? throw new InvalidOperationException($"tier {fromTier} has no block to copy");
            var copy = VfxDocument.Rewrite(document.RawOf(source), (d, e) => [d.SetAttribute(e, "value", toTier.ToString(CultureInfo.InvariantCulture))]);
            return document.InsertAfter(chain.ChildrenNamed("quality").Last(), copy);
        }

        // Emitters

        /// <summary>An emitter, its behaviour chain and its render stack share one id; all three are copied under the new one,
        /// each after its original.</summary>
        public static List<Splice> DuplicateEmitter(VfxDocument document, string id, string newId)
        {
            if (string.IsNullOrWhiteSpace(newId) || newId != newId.Trim())
                throw new ArgumentException("an id cannot be empty or start or end with a space");
            if (document.ById("emitters", id) == null)
                throw new ArgumentException($"there is no emitter {id}");
            if (s_emitterSections.Any(s => document.ById(s, newId) != null))
                throw new ArgumentException($"{newId} is already an id in this file");
            return s_emitterSections.Select(s => document.ById(s, id)).OfType<SpanElement>()
                .Select(r => document.InsertAfter(r, VfxDocument.Rewrite(document.RawOf(r), (d, e) => [d.SetAttribute(e, "id", newId)])))
                .ToList();
        }

        public static List<Splice> RemoveEmitter(VfxDocument document, string id) =>
            s_emitterSections.Select(s => document.ById(s, id)).OfType<SpanElement>().Select(document.Remove).ToList();

        // References

        /// <summary>CA's attributes for a reference, in CA's order, with the most common value of each across vfx_desc.pack
        /// (measured 2026-10-11).</summary>
        public static readonly (string Name, string Value)[] ReferenceDefaults =
        [
            ("inst_name", "xref"), ("vfx_ref", ""), ("notes", ""), ("scale_min", "1.00"), ("scale_max", "1.00"),
            ("pos_x", "0.00"), ("pos_y", "0.00"), ("pos_z", "0.00"), ("rot_x", "0.00"), ("rot_y", "0.00"), ("rot_z", "0.00"),
            ("start_time_min", "0.00"), ("start_time_max", "0.00"), ("infinite_life", "true"), ("simulation_speed_multiplier", "1.00"),
        ];

        /// <summary>An override row in CA's attribute order and neutral values: no tint, no bias, the spawn rate as written.</summary>
        public static string OverrideRow(string emitterName, bool enable = true) =>
            $"<emitter name=\"{XmlText.EscapeAttribute(emitterName, '"')}\" enable=\"{(enable ? "true" : "false")}\" " +
            "tint_min_r=\"1.00\" tint_min_g=\"1.00\" tint_min_b=\"1.00\" tint_min_a=\"1.00\" tint_max_r=\"1.00\" tint_max_g=\"1.00\" tint_max_b=\"1.00\" tint_max_a=\"1.00\" " +
            "particle_lifetime_bias_min=\"0.00\" particle_lifetime_bias_max=\"0.00\" emitter_lifetime_bias_min=\"0.00\" emitter_lifetime_bias_max=\"0.00\" " +
            "spawn_rate_scale_factor_min=\"1.00\" spawn_rate_scale_factor_max=\"1.00\" sound_record=\"\"/>";

        /// <summary>A reference to `target` with a row, switched on, for each of its emitters (planning ruling: CA's most
        /// common row is switched off, but on shows the target at once).</summary>
        public static Splice AddReference(VfxDocument document, string target, IReadOnlyList<string> targetEmitters)
        {
            var references = Required(document, "vfx_references");
            var attributes = ReferenceDefaults.Select(a => a.Name == "vfx_ref" ? (a.Name, target) : a);
            var start = "<vfx " + string.Join(' ', attributes.Select(a => $"{a.Name}=\"{XmlText.EscapeAttribute(a.Item2, '"')}\"")) + ">";
            var rows = targetEmitters.Select(e => OverrideRow(e)).ToList();
            return document.InsertInto(references, VfxDocument.Wrap(start, rows, "</vfx>", document.ChildBreak(references)));
        }

        public static Splice AddOverride(VfxDocument document, SpanElement reference, string emitterName) => document.InsertInto(reference, OverrideRow(emitterName));

        public static List<Splice> SetAllOverrides(VfxDocument document, SpanElement reference, bool enable) =>
            reference.ChildrenNamed("emitter").Select(r => document.SetAttribute(r, "enable", enable ? "true" : "false")).ToList();
    }
}
