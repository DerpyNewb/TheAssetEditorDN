using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx
{
    public enum SimulationSpace { World, LocalPositionRotation, LocalPositionOnly }

    public enum QuadOrientation { CameraFacing, VelocityAligned, Horizontal }

    /// <summary>
    /// One emitter, read into what ParticleSimulator and ParticleRenderItem use. Its setters let a test
    /// build one directly; From reads one out of a resolved emitter, and lists in Skipped what step 1
    /// does not simulate, for the log.
    /// </summary>
    public class EmitterSettings
    {
        public string Name { get; set; } = "";
        public bool Draws { get; set; } = true;
        public List<string> Skipped { get; } = [];

        // Placement and timing
        public Matrix ReferenceTransform { get; set; } = Matrix.Identity;
        public float StartOffset { get; set; }
        public (float Min, float Max) StartTime { get; set; }
        public (float Min, float Max) LifeTime { get; set; } = (1, 1);
        public (float Min, float Max) EmitterLifeBias { get; set; }
        public bool InfiniteLife { get; set; }
        public float SpeedMultiplier { get; set; } = 1;
        public SimulationSpace Space { get; set; }
        public VfxCurve? EmitterPosition { get; set; }
        public VfxCurve? EmitterOrientation { get; set; }
        public bool LoopPosition { get; set; }
        public bool LoopOrientation { get; set; }

        // Spawning
        public VfxCurve? Rate { get; set; }
        public bool LoopRate { get; set; }
        public (float Min, float Max) RateScale { get; set; } = (1, 1);
        public (float Min, float Max)? BurstInterval { get; set; }
        public (float Min, float Max) BurstLength { get; set; }
        public int MaxParticles { get; set; } = ParticleSimulator.ParticleCap;
        public VfxCurve? SpawnBox { get; set; }
        public VfxCurve? SpawnRing { get; set; }
        public bool LoopArea { get; set; }
        public Vector2 SpawnAngle { get; set; }
        public Vector2 SpawnArc { get; set; }
        public (float Min, float Max) Spread { get; set; } = (90, 90);

        // A particle's start
        public VfxCurve? InitialSpeed { get; set; }
        public VfxCurve? InitialScale { get; set; }
        public VfxCurve? InitialAlpha { get; set; }
        public VfxCurve? ParticleLife { get; set; }
        public bool LoopSpeed { get; set; }
        public bool LoopInitial { get; set; }
        public bool LoopLife { get; set; }
        public (float Min, float Max) LifeBias { get; set; }
        public Vector4 TintMin { get; set; } = Vector4.One;
        public Vector4 TintMax { get; set; } = Vector4.One;
        public (float Min, float Max) Angle { get; set; }
        public (float Min, float Max) Spin { get; set; }
        public float SpinSign { get; set; } = 1;

        // Over a particle's life
        public VfxCurve? Scale { get; set; }
        public VfxCurve? Deceleration { get; set; }
        public VfxCurve? SpinDeceleration { get; set; }
        public VfxCurve?[] Forces { get; set; } = [];
        public VfxCurve? Alpha { get; set; }
        public VfxCurve? Colour { get; set; }
        public VfxCurve? Stretch { get; set; }

        // Look
        public string? Texture { get; set; }
        public bool Premultiplied { get; set; } = true;
        public float AdditiveMul { get; set; }
        public Vector2 Pivot { get; set; } = new(0.5f, 0.5f);
        public QuadOrientation Orientation { get; set; }
        public float StretchCap { get; set; }
        public int CellsX { get; set; } = 1;
        public int CellsY { get; set; } = 1;
        public bool Animated { get; set; }
        public bool FlipbookLoop { get; set; } = true;
        public Vector2 Loops { get; set; } = Vector2.One;
        public bool FixedSpeed { get; set; }
        public int Cell { get; set; }
        public bool RandomCell { get; set; }
        public int AtlasX { get; set; } = 1;
        public int AtlasY { get; set; } = 1;
        public int AtlasCell { get; set; }

        // The 18 fragments vanilla uses (spec section 3). Step 1 reads eight of them; the rest are listed in docs/VFX.md.
        static readonly HashSet<string> s_knownFragments =
        [
            "vs_vertex_alpha", "vs_colour", "ps_texture", "ps_soft_edge", "ps_lighting", "vs_translation", "vs_cell_texture",
            "vs_translation_velocity_v2", "vs_animated_texture", "vs_flip_uv", "ps_normal", "vs_rendersize_limiter", "ps_alpha_crush",
            "ps_gradient", "ps_emissive", "ps_custom_shader", "ps_metal_roughness", "vs_translation_screen_space_wrap",
        ];

        static readonly (string Property, string What)[] s_notSimulated =
        [
            ("AllowEmitFromMesh", "emit from mesh"),
            ("EnableCollisions", "collisions"),
            ("ParticlesFollowTerrain", "terrain follow"),
            ("ParticlesSpawnOnTerrain", "terrain spawn"),
        ];

        public static EmitterSettings From(ResolvedEmitter resolved)
        {
            var properties = resolved.Emitter.Properties;
            var o = resolved.Override;
            // The file is in the name, so a log line says where a broken property lives (spec section 9).
            var s = new EmitterSettings { Name = resolved.File + "/" + resolved.Emitter.Id };

            VfxProperty? P(string name) => properties.GetValueOrDefault(name);
            bool Flag(string name) => P(name)?.IsTrue ?? false;
            float Number(string name, float fallback) => P(name)?.Number("value", fallback) ?? fallback;
            (float, float) Range(string name, (float, float) fallback) => P(name) is { } p ? (p.Number("min"), p.Number("max")) : fallback;
            Vector2 Xy(string name) => P(name) is { } p ? new Vector2(p.Number("x"), p.Number("y")) : Vector2.Zero;
            float Z(string name) => P(name)?.Number("z") ?? 0;

            s.ReferenceTransform = resolved.ReferenceTransform;
            s.StartOffset = resolved.StartOffset;
            s.SpeedMultiplier = resolved.SpeedMultiplier * Number("SimulationSpeedMultiplier", 1);
            s.StartTime = Range("StartTime", (0, 0));
            s.LifeTime = Range("LifeTime", (1, 1));
            s.EmitterLifeBias = (o.EmitterLifetimeBiasMin, o.EmitterLifetimeBiasMax);
            s.InfiniteLife = resolved.InfiniteLife || Flag("InfiniteLife");
            s.Space = P("ParticleSimulationSpace")?.Value switch
            {
                "LOCAL_SPACE_POSITION_ROTATION" => SimulationSpace.LocalPositionRotation,
                "LOCAL_SPACE_POSITION_ONLY" => SimulationSpace.LocalPositionOnly,
                _ => SimulationSpace.World,
            };
            s.EmitterPosition = ParseCurve(s, P("EmitterPosition"));
            s.EmitterOrientation = ParseCurve(s, P("EmitterOrientation"));
            s.LoopPosition = Flag("EmitterPositionLoopAnim");
            s.LoopOrientation = Flag("EmitterOrientationLoopAnim");

            s.Rate = ParseCurve(s, P("EmissionRateCurve"));
            s.LoopRate = Flag("EmissionLoopAnim");
            s.RateScale = (o.SpawnRateScaleMin, o.SpawnRateScaleMax);
            if (P("BurstInterval") != null)
            {
                s.BurstInterval = Range("BurstInterval", (0, 0));
                s.BurstLength = Range("BurstIntervalLength", (0, 0));
            }
            var maximum = (int)Number("MaximumParticles", ParticleSimulator.ParticleCap);
            s.MaxParticles = maximum > 0 ? Math.Min(maximum, ParticleSimulator.ParticleCap) : ParticleSimulator.ParticleCap;
            s.SpawnBox = ParseCurve(s, P("SpawnAreaBox"));
            s.SpawnRing = ParseCurve(s, P("SpawnAreaCurve"));
            s.LoopArea = Flag("SpawnAreaLoopAnim");
            s.SpawnAngle = P("SpawnAngle") == null ? VfxConventions.EveryAzimuth : Xy("SpawnAngle");
            s.SpawnArc = Xy("SpawnArc");
            s.Spread = Range("SpawnSpread", (90, 90));

            s.InitialSpeed = ParseCurve(s, P("ParticleInitialSpeed"));
            s.InitialScale = ParseCurve(s, P("ParticleInitialScale"));
            s.InitialAlpha = ParseCurve(s, P("ParticleInitialAlpha"));
            s.ParticleLife = ParseCurve(s, P("ParticleLifeTime"));
            s.LoopSpeed = Flag("ParticleInitialSpeedLoopAnim");
            s.LoopInitial = Flag("ParticleInitialParamLoopAnim");
            s.LoopLife = Flag("ParticleLifeTimeLoopAnim");
            s.LifeBias = (o.ParticleLifetimeBiasMin, o.ParticleLifetimeBiasMax);
            s.TintMin = o.TintMin;
            s.TintMax = o.TintMax;
            s.Angle = (Z("ParticleOrientationMin"), Z("ParticleOrientationMax"));
            s.Spin = (Z("ParticleSpin_Min"), Z("ParticleSpin_Max"));
            s.SpinSign = VfxConventions.SpinSign(P("ParticleSpinDirectionZ")?.Value);

            s.Scale = ParseCurve(s, P("ParticleScale"));
            s.Deceleration = ParseCurve(s, P("ParticleDeceleration"));
            s.SpinDeceleration = ParseCurve(s, P("ParticleSpinDeceleration"));
            s.Forces = [ParseCurve(s, P("ForceA")), ParseCurve(s, P("ForceB")), ParseCurve(s, P("ForceC"))];

            if (resolved.Emitter.Type != "continuous_mesh")
                NotDrawn(s, $"emitter type {resolved.Emitter.Type} is not drawn in step 1");
            if (resolved.SceneViewType is not (null or "FULLRES_DEFERRED_ALPHA" or "HALFRES_DEFERRED_ALPHA"))
                NotDrawn(s, $"scene_view_type {resolved.SceneViewType} is not drawn in step 1");
            foreach (var (property, what) in s_notSimulated)
            {
                if (Flag(property))
                    s.Skipped.Add($"{what} is not simulated in step 1");
            }
            if (P("VectorFieldStrId")?.Value is { Length: > 0 })
                s.Skipped.Add("the vector field is not simulated in step 1");
            if (Number("EffectedByWindFactor", 0) != 0)
                s.Skipped.Add("wind is not simulated in step 1");
            if (Number("SpawnInheritedSpeed", 0) != 0)
                s.Skipped.Add("inherited speed is not simulated in step 1");
            if (Range("SpreadArc", (0, 0)) != (0, 0))
                s.Skipped.Add("SpreadArc is not read in step 1");

            ReadChain(s, resolved.Chain);
            return s;
        }

        static void ReadChain(EmitterSettings s, VfxBehaviourChain? chain)
        {
            if (chain == null)
            {
                NotDrawn(s, "no behaviour chain, so nothing to draw");
                return;
            }
            foreach (var modifier in chain.Modifiers.Where(x => !s_knownFragments.Contains(x.Fragment)))
                s.Skipped.Add($"fragment {modifier.Fragment} is not one of the 18 vanilla uses, so it is ignored");

            float Number(VfxModifier? m, string name, float fallback) => m?.Properties.GetValueOrDefault(name)?.Number("value", fallback) ?? fallback;
            bool Flag(VfxModifier? m, string name) => m?.Properties.GetValueOrDefault(name)?.IsTrue ?? false;

            var texture = chain.Find("ps_texture");
            s.Texture = texture?.Textures.GetValueOrDefault("diffuse") is { Length: > 0 } path ? path : null;
            if (texture == null && chain.Find("ps_custom_shader") is { } custom)
                NotDrawn(s, $"fragment ps_custom_shader draws through {custom.Properties.GetValueOrDefault("material")?.Attributes.GetValueOrDefault("path")}, which step 1 does not read");
            else if (texture == null && s.Draws)
                s.Skipped.Add("the chain has no ps_texture, so it is drawn white");
            s.Premultiplied = texture?.Properties.GetValueOrDefault("pre_multiplied_alpha")?.IsTrue ?? true;
            s.AdditiveMul = Number(chain.Find("ps_lighting"), "additive_blend_mul", 0);
            s.Alpha = ParseCurve(s, chain.Find("vs_vertex_alpha")?.Properties.GetValueOrDefault("curve_alpha"));
            s.Colour = ParseCurve(s, chain.Find("vs_colour")?.Properties.GetValueOrDefault("curve_colour"));

            var velocity = chain.Find("vs_translation_velocity_v2");
            var translation = velocity ?? chain.Find("vs_translation");
            s.Pivot = new Vector2(Number(translation, "pivot_x", 0.5f), Number(translation, "pivot_y", 0.5f));
            s.Stretch = ParseCurve(s, velocity?.Properties.GetValueOrDefault("velocity_stretch_mul"));
            s.StretchCap = Number(velocity, "velocity_stretch_cap", 0);
            var mode = velocity?.Properties.GetValueOrDefault("orientation_mode")?.Value;
            (s.Orientation, var known) = mode switch
            {
                null => (QuadOrientation.CameraFacing, true),
                "Camera-aligned with stretching factor" or "Camera-facing velocity-aligned" => (QuadOrientation.VelocityAligned, true),
                "Horizontally-oriented" => (QuadOrientation.Horizontal, true),
                _ => (QuadOrientation.CameraFacing, false),
            };
            if (!known)
                s.Skipped.Add($"orientation_mode \"{mode}\" is drawn camera-facing in step 1");

            var animated = chain.Find("vs_animated_texture");
            var cells = chain.Find("vs_cell_texture");
            var atlas = animated ?? cells;
            s.AtlasX = Math.Max(1, (int)Number(atlas, "atlas_number_of_cells_x", 1));
            s.AtlasY = Math.Max(1, (int)Number(atlas, "atlas_number_of_cells_y", 1));
            s.AtlasCell = (int)Number(atlas, "atlas_cell", 0);
            if (animated != null)
            {
                var count = animated.Properties.GetValueOrDefault("no_cells_xy")?.Float2(Vector2.One) ?? Vector2.One;
                s.CellsX = Math.Max(1, (int)count.X);
                s.CellsY = Math.Max(1, (int)count.Y);
                s.Animated = true;
                s.FlipbookLoop = Flag(animated, "loop");
                s.Loops = animated.Properties.GetValueOrDefault("no_loops")?.Float2(Vector2.One) ?? Vector2.One;
                s.FixedSpeed = animated.Properties.GetValueOrDefault("time_mode")?.Value == "fixed_animation_speed";
            }
            else if (cells != null)
            {
                s.CellsX = Math.Max(1, (int)Number(cells, "number_of_cells_x", 1));
                s.CellsY = Math.Max(1, (int)Number(cells, "number_of_cells_y", 1));
                s.Cell = (int)Number(cells, "cell", 0);
                s.RandomCell = Flag(cells, "randomize_cell");
            }
        }

        static VfxCurve? ParseCurve(EmitterSettings s, VfxProperty? property)
        {
            if (property == null)
                return null;
            var curve = VfxCurve.Parse(property.Text);
            if (curve == null)
                s.Skipped.Add($"{property.Name} did not parse, so its default is used");
            return curve;
        }

        static void NotDrawn(EmitterSettings s, string why)
        {
            s.Draws = false;
            s.Skipped.Add(why);
        }
    }
}
