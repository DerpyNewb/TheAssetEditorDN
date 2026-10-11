using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.PackFiles;

namespace GameWorld.Core.Vfx
{
    /// <summary>
    /// Draws one effect's particles: a camera-built quad per particle, one draw per emitter, emitters and
    /// particles back to front, through MonoGame's BasicEffect, unlit (lighting is step 2). Textures come from
    /// the editor's VfxTextureCache on first draw; a missing one draws white.
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

        /// <summary>The view's position and axes, moved into the effect's model space.</summary>
        public readonly record struct CameraAxes(Vector3 Position, Vector3 Right, Vector3 Up, Vector3 Forward)
        {
            public static CameraAxes From(CommonShaderParameters parameters, Matrix world)
            {
                var toModel = Matrix.Invert(world);
                var v = parameters.View;
                Vector3 Axis(Vector3 axis) => Vector3.Normalize(Vector3.TransformNormal(axis, toModel));
                return new CameraAxes(Vector3.Transform(parameters.CameraPosition, toModel),
                    Axis(new Vector3(v.M11, v.M21, v.M31)), Axis(new Vector3(v.M12, v.M22, v.M32)), Axis(-new Vector3(v.M13, v.M23, v.M33)));
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

            var camera = CameraAxes.From(parameters, World);
            // ponytail: emitters are sorted back to front within one effect, not against other effects.
            var emitters = _simulator.Emitters.Where(x => x.Particles.Count != 0)
                .Select(x => (x.Settings, States: ParticleSimulator.States(x).ToList()))
                .OrderByDescending(x => Vector3.DistanceSquared(Centre(x.States), camera.Position))
                .ToList();

            var blendState = device.BlendState;
            var samplerState = device.SamplerStates[0];
            device.SamplerStates[0] = SamplerState.LinearClamp;
            foreach (var (settings, states) in emitters)
                DrawEmitter(device, _effect, parameters, World, camera, settings, TextureFor(device, settings.Texture), states);
            device.BlendState = blendState;
            device.SamplerStates[0] = samplerState;
        }

        public static void DrawEmitter(GraphicsDevice device, BasicEffect effect, CommonShaderParameters parameters, Matrix world,
            CameraAxes camera, EmitterSettings settings, Texture2D texture, IReadOnlyList<ParticleState> states)
        {
            var vertices = new VertexPositionColorTexture[states.Count * 6];
            var n = 0;
            foreach (var state in states.OrderByDescending(x => Vector3.DistanceSquared(x.Position, camera.Position)))
                n = AddQuad(vertices, n, settings, camera, state);

            effect.World = world;
            effect.View = parameters.View;
            effect.Projection = parameters.Projection;
            effect.Texture = texture;
            device.BlendState = BlendFor(settings);
            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, states.Count * 2);
            }
        }

        static int AddQuad(VertexPositionColorTexture[] vertices, int n, EmitterSettings s, CameraAxes camera, ParticleState p)
        {
            var (across, up, height) = Axes(s, camera, p);
            var width = p.Size.X;
            var colour = VertexColour(s, p.Colour, p.Alpha);
            var uv = UvRect(s, p.Cell);
            VertexPositionColorTexture Corner(float x, float y) => new(
                p.Position + across * ((x - s.Pivot.X) * width) + up * ((y - s.Pivot.Y) * height),
                colour,
                new Vector2(uv.X + x * uv.Z, uv.Y + (1 - y) * uv.W));

            vertices[n++] = Corner(0, 0);
            vertices[n++] = Corner(1, 0);
            vertices[n++] = Corner(1, 1);
            vertices[n++] = Corner(0, 0);
            vertices[n++] = Corner(1, 1);
            vertices[n++] = Corner(0, 1);
            return n;
        }

        // The quad's two edge directions and its height, per orientation_mode.
        static (Vector3 Across, Vector3 Up, float Height) Axes(EmitterSettings s, CameraAxes camera, ParticleState p)
        {
            var (sin, cos) = MathF.SinCos(p.Angle);
            if (s.Orientation == QuadOrientation.Horizontal)
                return (new Vector3(cos, 0, -sin), new Vector3(sin, 0, cos), p.Size.Y);

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
