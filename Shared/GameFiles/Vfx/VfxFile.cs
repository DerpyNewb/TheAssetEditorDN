using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Shared.GameFormats.Vfx
{
    /// <summary>One property as written: its attributes (value, x/y/z/w, min/max, type, path) and, for a curve, its text.</summary>
    public record VfxProperty(string Name, IReadOnlyDictionary<string, string> Attributes, string? Text)
    {
        public string? Value => Attributes.GetValueOrDefault("value");
        public bool IsTrue => string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase);

        public float Number(string attribute, float fallback = 0) =>
            Attributes.TryGetValue(attribute, out var text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

        /// <summary>A "float2(8.0,8.0)" value.</summary>
        public Vector2 Float2(Vector2 fallback)
        {
            var parts = Value?.Replace("float2(", "").TrimEnd(')').Split(',');
            return parts is { Length: 2 }
                && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                ? new Vector2(x, y) : fallback;
        }
    }

    public record VfxEmitter(string Id, string Type, IReadOnlyDictionary<string, VfxProperty> Properties);

    public record VfxModifier(string Fragment, IReadOnlyDictionary<string, VfxProperty> Properties, IReadOnlyDictionary<string, string> Textures);

    public record VfxBehaviourChain(string Id, IReadOnlyList<VfxModifier> Modifiers)
    {
        public VfxModifier? Find(string fragment) => Modifiers.FirstOrDefault(x => x.Fragment == fragment);
    }

    /// <summary>What a reference says about one emitter of the file it references, by name.</summary>
    public record VfxEmitterOverride(string Name, bool Enable, Vector4 TintMin, Vector4 TintMax,
        float ParticleLifetimeBiasMin, float ParticleLifetimeBiasMax, float EmitterLifetimeBiasMin, float EmitterLifetimeBiasMax,
        float SpawnRateScaleMin, float SpawnRateScaleMax)
    {
        public static readonly VfxEmitterOverride None = new("", true, Vector4.One, Vector4.One, 0, 0, 0, 0, 1, 1);
    }

    public record VfxReference(string Target, float ScaleMin, float ScaleMax, Vector3 Position, Vector3 RotationDegrees,
        float StartTimeMin, float StartTimeMax, bool InfiniteLife, float SimulationSpeed, IReadOnlyDictionary<string, VfxEmitterOverride> Overrides);

    /// <summary>
    /// One vfx/*.xml, read at one quality tier (0 is Ultra, the default). The file has several root elements (shader_library,
    /// vfx, vfx_references, editor_data), so it is parsed inside a wrapper element. Emitters, behaviour
    /// chains and render stacks share one id per emitter.
    /// </summary>
    public class VfxFile
    {
        public List<VfxEmitter> Emitters { get; } = [];
        public Dictionary<string, VfxBehaviourChain> Chains { get; } = [];
        /// <summary>Render stack id to its quality-0 scene_view_type, for the stacks that set one.</summary>
        public Dictionary<string, string> SceneViewTypes { get; } = [];
        public List<VfxReference> References { get; } = [];

        public static VfxFile Parse(byte[] bytes, int tier = 0)
        {
            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd();
            if (text.StartsWith("<?xml"))
                text = text[(text.IndexOf("?>") + 2)..];
            var root = XElement.Parse("<root>" + text + "</root>");

            var file = new VfxFile();
            var vfx = root.Element("vfx");

            foreach (var emitter in vfx?.Element("emitters")?.Elements("emitter") ?? [])
            {
                var properties = Properties(emitter.Elements().Where(x => x.Name.LocalName is "property" or "ranged_property"), tier);
                file.Emitters.Add(new VfxEmitter((string?)emitter.Attribute("id") ?? "", (string?)emitter.Attribute("type") ?? "", properties));
            }

            foreach (var chain in vfx?.Element("behaviour_chains")?.Elements("behaviour_chain") ?? [])
            {
                var blocks = chain.Elements("quality").ToList();
                // A block has no fallback (docs/sessions/HANDOFF_20260829_VFX_QUALITY_TIERS.md): with no block at the tier the
                // chain draws nothing there. Tier 0 keeps reading the lowest block when there is no "0".
                var block = blocks.FirstOrDefault(x => (string?)x.Attribute("value") == tier.ToString(CultureInfo.InvariantCulture))
                    ?? (tier == 0 ? blocks.OrderBy(x => (string?)x.Attribute("value")).FirstOrDefault() : null);
                if (block == null && tier != 0)
                    continue;
                var modifiers = (block?.Elements("modifier") ?? []).Select(m => new VfxModifier(
                    (string?)m.Attribute("fragment") ?? "",
                    Properties(m.Elements("property")),
                    m.Elements("texture_property").GroupBy(t => (string?)t.Attribute("name") ?? "").ToDictionary(g => g.Key, g => (string?)g.First().Attribute("path") ?? ""))).ToList();
                var id = (string?)chain.Attribute("id") ?? "";
                file.Chains[id] = new VfxBehaviourChain(id, modifiers);
            }

            foreach (var stack in vfx?.Element("effect_render_stacks")?.Elements("effect_render_stack") ?? [])
            {
                if (Properties(stack.Elements("property"), tier).TryGetValue("scene_view_type", out var view) && view.Value != null)
                    file.SceneViewTypes[(string?)stack.Attribute("id") ?? ""] = view.Value;
            }

            foreach (var reference in root.Element("vfx_references")?.Elements("vfx") ?? [])
            {
                var overrides = new Dictionary<string, VfxEmitterOverride>();
                foreach (var o in reference.Elements("emitter"))
                {
                    var name = (string?)o.Attribute("name") ?? "";
                    overrides[name] = new VfxEmitterOverride(name, (string?)o.Attribute("enable") != "false",
                        new Vector4(Num(o, "tint_min_r", 1), Num(o, "tint_min_g", 1), Num(o, "tint_min_b", 1), Num(o, "tint_min_a", 1)),
                        new Vector4(Num(o, "tint_max_r", 1), Num(o, "tint_max_g", 1), Num(o, "tint_max_b", 1), Num(o, "tint_max_a", 1)),
                        Num(o, "particle_lifetime_bias_min", 0), Num(o, "particle_lifetime_bias_max", 0),
                        Num(o, "emitter_lifetime_bias_min", 0), Num(o, "emitter_lifetime_bias_max", 0),
                        Num(o, "spawn_rate_scale_factor_min", 1), Num(o, "spawn_rate_scale_factor_max", 1));
                }

                file.References.Add(new VfxReference((string?)reference.Attribute("vfx_ref") ?? "",
                    Num(reference, "scale_min", 1), Num(reference, "scale_max", 1),
                    new Vector3(Num(reference, "pos_x", 0), Num(reference, "pos_y", 0), Num(reference, "pos_z", 0)),
                    new Vector3(Num(reference, "rot_x", 0), Num(reference, "rot_y", 0), Num(reference, "rot_z", 0)),
                    Num(reference, "start_time_min", 0), Num(reference, "start_time_max", 0),
                    (string?)reference.Attribute("infinite_life") == "true", Num(reference, "simulation_speed_multiplier", 1), overrides));
            }

            return file;
        }

        // Tier 0 (or no quality attribute) is the base; a property written at the asked tier replaces it. The first one
        // written at each wins.
        static Dictionary<string, VfxProperty> Properties(IEnumerable<XElement> elements, int tier = 0)
        {
            var output = new Dictionary<string, VfxProperty>();
            var overrides = new Dictionary<string, VfxProperty>();
            var asked = tier.ToString(CultureInfo.InvariantCulture);
            foreach (var element in elements)
            {
                var name = (string?)element.Attribute("name");
                var quality = (string?)element.Attribute("quality") ?? "0";
                var target = quality == "0" ? output : quality == asked ? overrides : null;
                if (name == null || target == null)
                    continue;
                var attributes = element.Attributes().Where(a => a.Name.LocalName is not ("name" or "quality")).ToDictionary(a => a.Name.LocalName, a => a.Value);
                target.TryAdd(name, new VfxProperty(name, attributes, element.Value.Length == 0 ? null : element.Value));
            }
            foreach (var (name, property) in overrides)
                output[name] = property;
            return output;
        }

        static float Num(XElement element, string attribute, float fallback) =>
            float.TryParse((string?)element.Attribute(attribute), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }
}
