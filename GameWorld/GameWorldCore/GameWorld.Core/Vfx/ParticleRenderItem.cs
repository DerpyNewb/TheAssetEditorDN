using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.PackFiles;

namespace GameWorld.Core.Vfx
{
    /// <summary>
    /// Draws one effect's particles: a camera-built quad per particle, or one strip for a trail-mode emitter, one draw
    /// per emitter, emitters and particles back to front, through MonoGame's BasicEffect, unlit (lighting is step 2).
    /// Textures come from the editor's VfxTextureCache on first draw; a missing one draws white.
    /// </summary>
    public class ParticleRenderItem : IRenderItem, IDisposable
    {
        private readonly ParticleSimulator _simulator;
        private readonly VfxTextureCache _textureCache;
        private BasicEffect? _effect;
        private Texture2D? _white;

        public Matrix World { get; set; } = Matrix.Identity;
        public bool IsDisposed { get; private set; }

        public ParticleRenderItem(ParticleSimulator simulator, VfxTextureCache textures)
        {
            _simulator = simulator;
            _textureCache = textures;
        }

        /// <summary>The view's position and axes, moved into the effect's model space. PixelScale: the pixels one metre covers
        /// one metre from the camera, 0 when the view's height is not known.</summary>
        public readonly record struct CameraAxes(Vector3 Position, Vector3 Right, Vector3 Up, Vector3 Forward, float PixelScale = 0)
        {
            public static CameraAxes From(CommonShaderParameters parameters, Matrix world, int viewportHeight = 0)
            {
                var toModel = Matrix.Invert(world);
                var v = parameters.View;
                Vector3 Axis(Vector3 axis) => Vector3.Normalize(Vector3.TransformNormal(axis, toModel));
                return new CameraAxes(Vector3.Transform(parameters.CameraPosition, toModel),
                    Axis(new Vector3(v.M11, v.M21, v.M31)), Axis(new Vector3(v.M12, v.M22, v.M32)), Axis(-new Vector3(v.M13, v.M23, v.M33)),
                    viewportHeight * parameters.Projection.M22 / 2);
            }
        }

        public void Draw(GraphicsDevice device, CommonShaderParameters parameters, RenderingTechnique renderingTechnique)
        {
            if (renderingTechnique != RenderingTechnique.Normal)
                return;

            _effect ??= new BasicEffect(device) { TextureEnabled = true, VertexColorEnabled = true, LightingEnabled = false };
            if (_white == null)
            {
                _white = new Texture2D(device, 1, 1);
                _white.SetData(new[] { Color.White });
            }

            var camera = CameraAxes.From(parameters, World, device.Viewport.Height);
            // ponytail: emitters are sorted back to front within one effect, not against other effects.
            var emitters = _simulator.Emitters.Where(x => x.Particles.Count != 0)
                .Select(x => (x.Settings, States: ParticleSimulator.States(x).ToList(), Frame: x.World))
                .OrderByDescending(x => Vector3.DistanceSquared(Centre(x.States), camera.Position))
                .ToList();

            var blendState = device.BlendState;
            var samplerState = device.SamplerStates[0];
            device.SamplerStates[0] = SamplerState.LinearClamp;
            foreach (var (settings, states, frame) in emitters)
                DrawEmitter(device, _effect, parameters, World, camera, settings, TextureFor(device, settings.Texture), states, frame);
            device.BlendState = blendState;
            device.SamplerStates[0] = samplerState;
        }

        public static void DrawEmitter(GraphicsDevice device, BasicEffect effect, CommonShaderParameters parameters, Matrix world,
            CameraAxes camera, EmitterSettings settings, Texture2D texture, IReadOnlyList<ParticleState> states, Matrix? frame = null)
        {
            var vertices = settings.Trail ? Ribbon(settings, camera, states, frame) : Quads(settings, camera, states, frame);
            if (vertices.Length == 0)
                return;

            effect.World = world;
            effect.View = parameters.View;
            effect.Projection = parameters.Projection;
            effect.Texture = texture;
            device.BlendState = BlendFor(settings);
            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, vertices.Length / 3);
            }
        }

        public static VertexPositionColorTexture[] Quads(EmitterSettings s, CameraAxes camera, IReadOnlyList<ParticleState> states, Matrix? frame = null)
        {
            var vertices = new VertexPositionColorTexture[states.Count * 6];
            var n = 0;
            var fade = EmitterFade(s, camera, frame);
            foreach (var state in states.OrderByDescending(x => Vector3.DistanceSquared(x.Position, camera.Position)))
                n = AddQuad(vertices, n, s, camera, state, frame ?? Matrix.Identity, fade);
            return vertices;
        }

        /// <summary>enable_trail_mode: the particles, oldest first, joined into one camera-facing strip as wide as each. The
        /// newest particle's texture cell runs along it, its top (a velocity-aligned quad's head) at the newest.</summary>
        public static VertexPositionColorTexture[] Ribbon(EmitterSettings s, CameraAxes camera, IReadOnlyList<ParticleState> states, Matrix? frame = null)
        {
            if (states.Count < 2)
                return [];
            var uv = UvRect(s, states[^1].Cell);
            var fade = EmitterFade(s, camera, frame);
            var last = states.Count - 1;
            var edges = new (Vector3 Left, Vector3 Right, Color Colour, float V)[states.Count];
            for (var i = 0; i <= last; i++)
            {
                var p = states[i];
                var along = states[Math.Min(i + 1, last)].Position - states[Math.Max(i - 1, 0)].Position;
                var side = Vector3.Cross(along, camera.Position - p.Position);
                side = side.LengthSquared() > 1e-12f ? Vector3.Normalize(side) * (p.Size.X / 2) : Vector3.Zero;
                edges[i] = (p.Position - side, p.Position + side, VertexColour(s, p.Colour, Faded(s, camera, p, fade)), uv.Y + (1 - i / (float)last) * uv.W);
            }

            var vertices = new VertexPositionColorTexture[last * 6];
            var n = 0;
            for (var i = 0; i < last; i++)
            {
                var (a, b) = (edges[i], edges[i + 1]);
                vertices[n++] = new(a.Left, a.Colour, new Vector2(uv.X, a.V));
                vertices[n++] = new(a.Right, a.Colour, new Vector2(uv.X + uv.Z, a.V));
                vertices[n++] = new(b.Right, b.Colour, new Vector2(uv.X + uv.Z, b.V));
                vertices[n++] = new(a.Left, a.Colour, new Vector2(uv.X, a.V));
                vertices[n++] = new(b.Right, b.Colour, new Vector2(uv.X + uv.Z, b.V));
                vertices[n++] = new(b.Left, b.Colour, new Vector2(uv.X, b.V));
            }
            return vertices;
        }

        // CullDistances: the whole emitter's fade, by the distance of its origin from the camera.
        static float EmitterFade(EmitterSettings s, CameraAxes camera, Matrix? frame) =>
            VfxConventions.CullFade(Vector3.Distance(camera.Position, (frame ?? Matrix.Identity).Translation), s.EmitterCull);

        // A particle's alpha, faded with the emitter's and by its own distance from the camera (ParticleCullDistances).
        static float Faded(EmitterSettings s, CameraAxes camera, ParticleState p, float emitterFade) =>
            p.Alpha * emitterFade * VfxConventions.CullFade(Vector3.Distance(camera.Position, p.Position), s.ParticleCull);

        static int AddQuad(VertexPositionColorTexture[] vertices, int n, EmitterSettings s, CameraAxes camera, ParticleState p, Matrix frame, float fade)
        {
            var (across, up, height) = Axes(s, camera, p, frame);
            var width = p.Size.X;
            var grow = VfxConventions.MinPixelGrowth(Math.Min(width, height), Vector3.Distance(camera.Position, p.Position), camera.PixelScale, s.MinPixels);
            (width, height) = (width * grow, height * grow);
            var colour = VertexColour(s, p.Colour, Faded(s, camera, p, fade));
            var uv = UvRect(s, p.Cell);
            VertexPositionColorTexture Corner(float x, float y)
            {
                var (u, v) = VfxConventions.QuadUv(x, y, p.FlipU, p.FlipV, s.UvTurn);
                return new(p.Position + across * ((x - s.Pivot.X) * width) + up * ((y - s.Pivot.Y) * height), colour, new Vector2(uv.X + u * uv.Z, uv.Y + v * uv.W));
            }

            vertices[n++] = Corner(0, 0);
            vertices[n++] = Corner(1, 0);
            vertices[n++] = Corner(1, 1);
            vertices[n++] = Corner(0, 0);
            vertices[n++] = Corner(1, 1);
            vertices[n++] = Corner(0, 1);
            return n;
        }

        // The quad's two edge directions and its height, per orientation_mode. frame: the emitter's matrix, whose axes the
        // two local-Y modes use. The three rare modes (29 vanilla chains) are readings of their names, not measurements.
        static (Vector3 Across, Vector3 Up, float Height) Axes(EmitterSettings s, CameraAxes camera, ParticleState p, Matrix frame)
        {
            var (sin, cos) = MathF.SinCos(p.Angle);
            if (s.Orientation == QuadOrientation.Horizontal)
                return (new Vector3(cos, 0, -sin), new Vector3(sin, 0, cos), p.Size.Y);

            // Flat in the emitter's XZ plane, or upright on its Y, turned by the particle's angle about that Y.
            if (s.Orientation is QuadOrientation.LocalHorizontal or QuadOrientation.LocalVertical)
            {
                var (x, z) = (Vector3.Normalize(frame.Right), Vector3.Normalize(frame.Backward));
                var across = x * cos - z * sin;
                return (across, s.Orientation == QuadOrientation.LocalHorizontal ? x * sin + z * cos : Vector3.Normalize(frame.Up), p.Size.Y);
            }

            // Upright, turning about the world's Y to face the camera.
            if (s.Orientation == QuadOrientation.Vertical)
            {
                var side = Vector3.Cross(Vector3.Up, camera.Position - p.Position);
                if (side.LengthSquared() > 1e-8f)
                    return (Vector3.Normalize(side), Vector3.Up, p.Size.Y);
            }

            if (s.Orientation == QuadOrientation.VelocityAligned)
            {
                var along = p.Velocity - camera.Forward * Vector3.Dot(p.Velocity, camera.Forward);
                if (along.LengthSquared() > 1e-8f)
                {
                    along.Normalize();
                    return (Vector3.Normalize(Vector3.Cross(along, camera.Forward)), along, p.Size.Y * VfxConventions.Stretch(p.Velocity.Length(), p.Stretch, s.StretchCap));
                }
            }

            return (camera.Right * cos + camera.Up * sin, camera.Up * cos - camera.Right * sin, p.Size.Y);
        }

        /// <summary>The texture rectangle (x, y, width, height) of a cell, inside the atlas cell when the texture is an atlas.</summary>
        public static Vector4 UvRect(EmitterSettings s, int cell)
        {
            var (ax, ay) = (Math.Max(1, s.AtlasX), Math.Max(1, s.AtlasY));
            var (cx, cy) = (Math.Max(1, s.CellsX), Math.Max(1, s.CellsY));
            var atlas = Math.Clamp(s.AtlasCell, 0, ax * ay - 1);
            cell = Math.Clamp(cell, 0, cx * cy - 1);
            var (w, h) = (1f / ax / cx, 1f / ay / cy);
            return new Vector4((atlas % ax) / (float)ax + (cell % cx) * w, (atlas / ax) / (float)ay + (cell / cx) * h, w, h);
        }

        /// <summary>
        /// Premultiplied chains: (rgb x a, a x alphaWeight) under one premultiplied blend, so alpha and additive
        /// particles mix in one sorted draw. The 825 straight-alpha chains draw their plain colour under a
        /// straight-alpha or additive blend instead.
        /// </summary>
        public static Color VertexColour(EmitterSettings s, Vector3 rgb, float alpha)
        {
            var (alphaWeight, brightness) = VfxConventions.Blend(s.AdditiveMul);
            alpha = Math.Clamp(alpha, 0, 1);
            return s.Premultiplied
                ? new Color(new Vector4(rgb * alpha * brightness, alpha * alphaWeight))
                : new Color(new Vector4(rgb * brightness, alpha));
        }

        // ponytail: a straight-alpha chain with a mixed additive_blend_mul is rounded to alpha or additive;
        // premultiply its texture on load if one turns up that needs the mix.
        public static BlendState BlendFor(EmitterSettings s) =>
            s.Premultiplied ? BlendState.AlphaBlend : s.AdditiveMul >= 0.5f ? BlendState.Additive : BlendState.NonPremultiplied;

        /// <summary>Loads a chain's texture from the loaded packs. Missing or undecodable: null (drawn white) and one log line.</summary>
        public static Func<GraphicsDevice, string, Texture2D?> PackTextureLoader(IPackFileService packFileService, Action<string> log) => (device, path) =>
        {
            try
            {
                var texture = ImageLoader.LoadTextureAsTexture2d(path, packFileService, device, out _, false);
                if (texture == null)
                    log($"vfx texture {path} is not in any loaded pack, so it is drawn white");
                return texture;
            }
            catch (Exception e)
            {
                log($"vfx texture {path} did not load ({e.Message}), so it is drawn white");
                return null;
            }
        };

        Texture2D TextureFor(GraphicsDevice device, string? path) =>
            string.IsNullOrEmpty(path) ? _white! : _textureCache.Load(device, path) ?? _white!;

        static Vector3 Centre(List<ParticleState> states) => states.Aggregate(Vector3.Zero, (sum, x) => sum + x.Position) / states.Count;

        // The textures belong to the VfxTextureCache, which outlives this item.
        public void Dispose()
        {
            // Nulled, so a frame drawn after a dispose builds them again rather than drawing with disposed ones.
            _white?.Dispose();
            _white = null;
            _effect?.Dispose();
            _effect = null;
            IsDisposed = true;
        }
    }
}
