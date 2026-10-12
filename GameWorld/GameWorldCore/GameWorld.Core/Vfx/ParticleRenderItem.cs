using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.RenderItems;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.PackFiles;
using System.Runtime.CompilerServices;

namespace GameWorld.Core.Vfx
{
    /// <summary>
    /// Draws one effect's particles: a camera-built quad per particle, or one strip for a trail-mode emitter, one draw
    /// per emitter, emitters and particles back to front, through Shaders/Particle.fx, unlit (lighting is step 2). The
    /// app's emissive pass gets what lies over 1 of each colour, and blooms it.
    /// Textures come from the editor's VfxTextureCache on first draw; a missing one draws white.
    /// </summary>
    public class ParticleRenderItem : IRenderItem, IDisposable
    {
        private readonly ParticleSimulator _simulator;
        private readonly VfxTextureCache _textureCache;
        private readonly Func<Effect> _shader;
        private readonly VfxMeshCache? _meshes;
        private Texture2D? _white;
        private RenderTarget2D? _scene;
        private SpriteBatch? _batch;

        public Matrix World { get; set; } = Matrix.Identity;
        public bool IsDisposed { get; private set; }

        /// <summary>shader: Shaders/Particle, asked for at draw time (ResourceLibrary's ShaderTypes.Particle in the app).
        /// meshes: continuous_rigid's models; without it, rigid emitters draw nothing.</summary>
        public ParticleRenderItem(ParticleSimulator simulator, VfxTextureCache textures, Func<Effect> shader, VfxMeshCache? meshes = null)
        {
            _shader = shader;
            _meshes = meshes;
            _simulator = simulator;
            _textureCache = textures;
        }

        /// <summary>The view's position and axes, moved into the effect's model space. PixelScale: the pixels one metre covers
        /// one metre from the camera, 0 when the view's height is not known. ToWorld: the effect's matrix, null for none.</summary>
        public readonly record struct CameraAxes(Vector3 Position, Vector3 Right, Vector3 Up, Vector3 Forward, float PixelScale = 0, Matrix? ToWorld = null)
        {
            public static CameraAxes From(CommonShaderParameters parameters, Matrix world, int viewportHeight = 0)
            {
                var toModel = Matrix.Invert(world);
                var v = parameters.View;
                Vector3 Axis(Vector3 axis) => Vector3.Normalize(Vector3.TransformNormal(axis, toModel));
                return new CameraAxes(Vector3.Transform(parameters.CameraPosition, toModel),
                    Axis(new Vector3(v.M11, v.M21, v.M31)), Axis(new Vector3(v.M12, v.M22, v.M32)), Axis(-new Vector3(v.M13, v.M23, v.M33)),
                    viewportHeight * parameters.Projection.M22 / 2, world);
            }
        }

        public void Draw(GraphicsDevice device, CommonShaderParameters parameters, RenderingTechnique renderingTechnique)
        {
            if (renderingTechnique is not (RenderingTechnique.Normal or RenderingTechnique.Emissive))
                return;

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

            // Rigid models first: they are opaque, and write the depth the particles are drawn against.
            foreach (var (settings, states, frame) in emitters.Where(x => x.Settings.Rigid.Count > 0))
                DrawRigid(device, parameters, renderingTechnique, settings, states, frame);

            var glow = renderingTechnique == RenderingTechnique.Emissive;
            var blendState = device.BlendState;
            var samplerState = device.SamplerStates[0];
            device.SamplerStates[0] = SamplerState.LinearClamp;
            foreach (var (settings, states, frame) in emitters.Where(x => x.Settings.Rigid.Count == 0))
            {
                // A haze bends what is drawn behind it by now, this effect's own particles included.
                var scene = !glow && settings.Distortion ? CopyScene(device) : null;
                DrawEmitter(device, _shader(), parameters, World, camera, settings, TextureFor(device, settings.Texture), states, frame, glow, scene);
            }
            device.BlendState = blendState;
            device.SamplerStates[0] = samplerState;
        }

        public static void DrawEmitter(GraphicsDevice device, Effect effect, CommonShaderParameters parameters, Matrix world,
            CameraAxes camera, EmitterSettings settings, Texture2D texture, IReadOnlyList<ParticleState> states, Matrix? frame = null, bool glow = false,
            Texture2D? scene = null)
        {
            // A distortion bends what is drawn already, and adds no light to bloom.
            if (settings.Distortion && (glow || scene == null))
                return;
            (states, var scale) = Hdr(settings, states, glow);
            var vertices = settings.Trail ? Ribbon(settings, camera, states, frame) : Quads(settings, camera, states, frame);
            if (vertices.Length == 0)
                return;

            effect.Parameters["WorldViewProjection"].SetValue(world * parameters.View * parameters.Projection);
            effect.Parameters["Scale"].SetValue(scale);
            effect.Parameters["Texture"].SetValue(texture);
            effect.Parameters["Premultiplied"].SetValue(settings.Premultiplied ? 1f : 0f);
            effect.Parameters["CrushSharp"].SetValue(settings.CrushSharp ? 1f : 0f);
            effect.Parameters["UseGradient"].SetValue(settings.Gradient != null ? 1f : 0f);
            effect.Parameters["GradientMapsAlpha"].SetValue(settings.GradientMapsAlpha ? 1f : 0f);
            effect.Parameters["GradientRamp"].SetValue(Ramp(settings));
            effect.CurrentTechnique = effect.Techniques[settings.Distortion ? "Distortion" : settings.Light ? "Light" : "ParticleDrawing"];
            if (settings.Distortion)
            {
                effect.Parameters["Scene"].SetValue(scene);
                effect.Parameters["InverseSceneSize"].SetValue(new Vector2(1f / scene!.Width, 1f / scene.Height));
                effect.Parameters["Reach"].SetValue(new Vector2(parameters.Projection.M11, parameters.Projection.M22) * (VfxConventions.DistortionMetres / 2));
            }
            device.BlendState = BlendFor(device, settings);
            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, vertices.Length / 3);
            }
        }

        // A vertex colour stops at 1 and the shader's Scale does not: an HDR emitter's colours are divided by the
        // brightest of them and the draw multiplied back, so a colour of 4 lights a quarter-grey texel fully. Past 255 even
        // the faintest texel a byte holds saturates. glow: only what lies over 1, which the app's emissive pass blooms.
        static (IReadOnlyList<ParticleState>, float) Hdr(EmitterSettings s, IReadOnlyList<ParticleState> states, bool glow)
        {
            var brightness = VfxConventions.Blend(s.AdditiveMul).Brightness;
            if (glow)
                states = states.Select(p => p with { Colour = VfxConventions.Glow(p.Colour * brightness) / brightness }).ToList();
            var top = states.Select(p => Math.Max(p.Colour.X, Math.Max(p.Colour.Y, p.Colour.Z)) * brightness).DefaultIfEmpty(1).Max();
            var scale = Math.Clamp(top, 1, 255);
            return scale > 1 ? (states.Select(p => p with { Colour = p.Colour / scale }).ToList(), scale) : (states, 1);
        }

        // ps_gradient's strip at the 16 points the shader interpolates between: rgb off curve_colour, alpha off curve_alpha.
        static Vector4[] Ramp(EmitterSettings s) => Enumerable.Range(0, 16).Select(i => i / 15f).Select(t => s.Gradient == null ? Vector4.One
            : new Vector4(s.Gradient.Evaluate(0, t, 0, 1), s.Gradient.Evaluate(1, t, 0, 1), s.Gradient.Evaluate(2, t, 0, 1), s.GradientAlpha?.Evaluate(0, t, 0, 1) ?? 1)).ToArray();

        public static VfxVertex[] Quads(EmitterSettings s, CameraAxes camera, IReadOnlyList<ParticleState> states, Matrix? frame = null)
        {
            var vertices = new VfxVertex[states.Count * 6];
            var n = 0;
            var fade = EmitterFade(s, camera, frame);
            foreach (var state in states.OrderByDescending(x => Vector3.DistanceSquared(x.Position, camera.Position)))
                n = AddQuad(vertices, n, s, camera, state, frame ?? Matrix.Identity, fade);
            return vertices;
        }

        /// <summary>enable_trail_mode: the particles, oldest first, joined into one camera-facing strip as wide as each. The
        /// newest particle's texture cell runs along it, its top (a velocity-aligned quad's head) at the newest.</summary>
        public static VfxVertex[] Ribbon(EmitterSettings s, CameraAxes camera, IReadOnlyList<ParticleState> states, Matrix? frame = null)
        {
            if (states.Count < 2)
                return [];
            var uv = UvRect(s, states[^1].Cell);
            var fade = EmitterFade(s, camera, frame);
            var last = states.Count - 1;
            var edges = new (Vector3 Left, Vector3 Right, Color Colour, float T, float Crush)[states.Count];
            for (var i = 0; i <= last; i++)
            {
                var p = states[i];
                var along = states[Math.Min(i + 1, last)].Position - states[Math.Max(i - 1, 0)].Position;
                var side = Vector3.Cross(along, camera.Position - p.Position);
                side = side.LengthSquared() > 1e-12f ? Vector3.Normalize(side) * (p.Size.X / 2) : Vector3.Zero;
                edges[i] = (p.Position - side, p.Position + side, VertexColour(s, p.Colour, Faded(s, camera, p, fade)), 1 - i / (float)last, p.Crush);
            }

            var (next, blend) = (UvRect(s, states[^1].NextCell), states[^1].CellBlend);
            VfxVertex Vertex(Vector3 position, Color colour, float across, float t, float crush) =>
                new(position, colour, new Vector2(uv.X + across * uv.Z, uv.Y + t * uv.W), new Vector4(next.X + across * next.Z, next.Y + t * next.W, blend, crush));
            var vertices = new VfxVertex[last * 6];
            var n = 0;
            for (var i = 0; i < last; i++)
            {
                var (a, b) = (edges[i], edges[i + 1]);
                vertices[n++] = Vertex(a.Left, a.Colour, 0, a.T, a.Crush);
                vertices[n++] = Vertex(a.Right, a.Colour, 1, a.T, a.Crush);
                vertices[n++] = Vertex(b.Right, b.Colour, 1, b.T, b.Crush);
                vertices[n++] = Vertex(a.Left, a.Colour, 0, a.T, a.Crush);
                vertices[n++] = Vertex(b.Right, b.Colour, 1, b.T, b.Crush);
                vertices[n++] = Vertex(b.Left, b.Colour, 0, b.T, b.Crush);
            }
            return vertices;
        }

        // CullDistances: the whole emitter's fade, by the distance of its origin from the camera.
        static float EmitterFade(EmitterSettings s, CameraAxes camera, Matrix? frame) =>
            VfxConventions.CullFade(Vector3.Distance(camera.Position, (frame ?? Matrix.Identity).Translation), s.EmitterCull);

        // A particle's alpha, faded with the emitter's and by its own distance from the camera (ParticleCullDistances).
        static float Faded(EmitterSettings s, CameraAxes camera, ParticleState p, float emitterFade) =>
            p.Alpha * emitterFade * VfxConventions.CullFade(Vector3.Distance(camera.Position, p.Position), s.ParticleCull);

        static int AddQuad(VfxVertex[] vertices, int n, EmitterSettings s, CameraAxes camera, ParticleState p, Matrix frame, float fade)
        {
            var toWorld = camera.ToWorld ?? Matrix.Identity;
            var lift = 0f;
            if (s.Decal)
            {
                var world = Vector3.Transform(p.Position, toWorld);
                lift = world.Y;
                p = p with { Position = Vector3.Transform(VfxConventions.Decal(world), Matrix.Invert(toWorld)) };
            }
            var (across, up, height) = Axes(s, camera, p, frame, toWorld);
            var width = p.Size.X;
            var grow = VfxConventions.MinPixelGrowth(Math.Min(width, height), Vector3.Distance(camera.Position, p.Position), camera.PixelScale, s.MinPixels);
            (width, height) = (width * grow, height * grow);
            var alpha = Faded(s, camera, p, fade);
            var colour = VertexColour(s, p.Colour, alpha);
            var (uv, next) = (UvRect(s, p.Cell), UvRect(s, p.NextCell));
            var eye = Vector3.Transform(camera.Position, toWorld);
            VfxVertex Corner(float x, float y)
            {
                var (u, v) = VfxConventions.QuadUv(x, y, p.FlipU, p.FlipV, s.UvTurn);
                var position = p.Position + across * ((x - s.Pivot.X) * width) + up * ((y - s.Pivot.Y) * height);
                // A light's pool spans its diameter, the particle's size; Extra.x is its height over the ground in radii.
                if (s.Light)
                    return new(position, colour, new Vector2(u, v), new Vector4(lift / (p.Size.X / 2), 0, 0, 0));
                var soft = s.SoftEdge > 0 && !s.Decal ? VertexColour(s, p.Colour, alpha * VfxConventions.SoftEdge(Vector3.Transform(position, toWorld), eye, s.SoftEdge, s.SoftStrength)) : colour;
                return new(position, soft, new Vector2(uv.X + u * uv.Z, uv.Y + v * uv.W), new Vector4(next.X + u * next.Z, next.Y + v * next.W, p.CellBlend, p.Crush));
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
        // toWorld: the effect's matrix, whose world a decal lies flat in.
        static (Vector3 Across, Vector3 Up, float Height) Axes(EmitterSettings s, CameraAxes camera, ParticleState p, Matrix frame, Matrix toWorld)
        {
            var (sin, cos) = MathF.SinCos(p.Angle);
            if (s.Decal)
            {
                var toModel = Matrix.Invert(toWorld);
                Vector3 Flat(Vector3 axis) => Vector3.Normalize(Vector3.TransformNormal(axis, toModel));
                return (Flat(new Vector3(cos, 0, -sin)), Flat(new Vector3(sin, 0, cos)), p.Size.Y);
            }
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

        /// <summary>A particle vertex for Shaders/Particle. Extra: the next flipbook cell's texture coordinate (xy), how far to
        /// blend into it (z), and the particle's alpha crush level (w).</summary>
        public struct VfxVertex(Vector3 position, Color color, Vector2 textureCoordinate, Vector4 extra) : IVertexType
        {
            public Vector3 Position = position;
            public Color Color = color;
            public Vector2 TextureCoordinate = textureCoordinate;
            public Vector4 Extra = extra;

            public static readonly VertexDeclaration Declaration = new(
                new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                new VertexElement(12, VertexElementFormat.Color, VertexElementUsage.Color, 0),
                new VertexElement(16, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
                new VertexElement(24, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 1));

            readonly VertexDeclaration IVertexType.VertexDeclaration => Declaration;
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

        // The app draws its scene onto nothing and lays it over the background by its alpha, so a particle's alpha is what it
        // hides: nothing for light it adds, its own alpha for one laid over. MonoGame's Additive and NonPremultiplied write that
        // alpha times itself, which blacked the background out under a light pool's unlit corners.
        // ponytail: a straight-alpha chain with a mixed additive_blend_mul is rounded to alpha or additive;
        // premultiply its texture on load if one turns up that needs the mix.
        public static BlendState BlendFor(GraphicsDevice device, EmitterSettings s) =>
            s.Distortion || (s.Premultiplied && !s.Light) ? BlendState.AlphaBlend : Straight(device)[s.Light || s.AdditiveMul >= 0.5f ? 1 : 0];

        // Laid over, added, and depth only. One set per device: MonoGame binds a state to the first device it is set on.
        static readonly ConditionalWeakTable<GraphicsDevice, BlendState[]> s_straight = new();
        static BlendState[] Straight(GraphicsDevice device) => s_straight.GetValue(device, _ =>
        [
            new BlendState { ColorSourceBlend = Blend.SourceAlpha, ColorDestinationBlend = Blend.InverseSourceAlpha, AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.InverseSourceAlpha },
            new BlendState { ColorSourceBlend = Blend.SourceAlpha, ColorDestinationBlend = Blend.One, AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One },
            new BlendState { ColorWriteChannels = ColorWriteChannels.None },
        ]);

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

        // continuous_rigid: each particle draws its model's LOD 0 through the model's own materials, opaque. The chain's colour
        // and alpha are not applied to it.
        void DrawRigid(GraphicsDevice device, CommonShaderParameters parameters, RenderingTechnique technique, EmitterSettings s, IReadOnlyList<ParticleState> states, Matrix frame)
        {
            if (_meshes == null)
                return;
            // The engine draws effects reading depth without writing it; a model writes its own, or its far side draws over its near one.
            var (blendState, depthState) = (device.BlendState, device.DepthStencilState);
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.Default;
            var turn = VfxConventions.RigidFrame(frame, s.RigidOnTerrain);
            foreach (var p in states)
            {
                var entry = s.Rigid[Math.Clamp(p.Model, 0, s.Rigid.Count - 1)];
                var model = _meshes.Get(entry.ModelFile);
                _meshes.Pose(model, entry.AnimFile, p.Age, entry.AnimSpeed);
                var matrix = VfxConventions.RigidMatrix(model.Box, entry.Pivot, p.Size.X, new Vector3(p.TurnXy, p.Angle), turn, p.Position) * World;
                var tinted = parameters with { ModelColour = new Vector4(p.Colour, p.Alpha) };
                // A see-through model shows its nearest faces once: its depth first, then its colour laid over where it is nearest.
                (BlendState, DepthStencilState)[] passes = p.Alpha < 1
                    ? [(Straight(device)[2], DepthStencilState.Default), (Straight(device)[0], DepthStencilState.DepthRead)]
                    : [(BlendState.Opaque, DepthStencilState.Default)];
                foreach (var (blend, depth) in passes)
                {
                    (device.BlendState, device.DepthStencilState) = (blend, depth);
                    foreach (var (geometry, material, local) in model.Meshes)
                        new GeometryRenderItem(geometry, material, local * matrix).Draw(device, tinted, technique);
                }
            }
            device.BlendState = blendState;
            device.DepthStencilState = depthState;
        }

        // DISTORTION samples what is drawn already, so the bound target is copied first, once a frame. Null on the back buffer.
        Texture2D? CopyScene(GraphicsDevice device)
        {
            if (device.GetRenderTargets() is not [{ RenderTarget: RenderTarget2D target }, ..])
                return null;
            if (_scene == null || _scene.Width != target.Width || _scene.Height != target.Height)
            {
                _scene?.Dispose();
                _scene = new RenderTarget2D(device, target.Width, target.Height, false, SurfaceFormat.Color, DepthFormat.None);
            }
            _batch ??= new SpriteBatch(device);
            var (viewport, depth, raster, blend, sampler) = (device.Viewport, device.DepthStencilState, device.RasterizerState, device.BlendState, device.SamplerStates[0]);
            device.SetRenderTarget(_scene);
            _batch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            _batch.Draw(target, Vector2.Zero, Color.White);
            _batch.End();
            device.SetRenderTarget(target);
            (device.Viewport, device.DepthStencilState, device.RasterizerState, device.BlendState, device.SamplerStates[0]) = (viewport, depth, raster, blend, sampler);
            return _scene;
        }

        Texture2D TextureFor(GraphicsDevice device, string? path) =>
            string.IsNullOrEmpty(path) ? _white! : _textureCache.Load(device, path) ?? _white!;

        static Vector3 Centre(List<ParticleState> states) => states.Aggregate(Vector3.Zero, (sum, x) => sum + x.Position) / states.Count;

        // The textures belong to the VfxTextureCache, which outlives this item, and the shader to whoever supplied it.
        public void Dispose()
        {
            // Nulled, so a frame drawn after a dispose builds them again rather than drawing with disposed ones.
            _white?.Dispose();
            _white = null;
            _scene?.Dispose();
            _scene = null;
            _batch?.Dispose();
            _batch = null;
            IsDisposed = true;
        }
    }
}
