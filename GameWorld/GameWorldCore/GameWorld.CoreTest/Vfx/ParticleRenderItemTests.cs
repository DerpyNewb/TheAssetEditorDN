using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx
{
    [Apartment(ApartmentState.STA)]
    public class ParticleRenderItemTests
    {
        GraphicsDeviceServiceMock _service;
        GraphicsDevice _device;
        Texture2D _white;
        ContentManager _content;
        // The content manager owns it, and caches it.
        Effect Shader => _content.Load<Effect>("Shaders/Particle");

        // Looking down -Z at the origin from 5 m; a 10 m quad there fills the view.
        static readonly CommonShaderParameters View = new(
            Matrix.CreateLookAt(new Vector3(0, 0, 5), Vector3.Zero, Vector3.Up),
            Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, 0.1f, 100),
            new Vector3(0, 0, 5), Vector3.Zero, 0, 0, 0, 1, [], Vector3.One);

        static readonly ParticleState RedHalfAlpha = new(Vector3.Zero, Vector3.Zero, new Vector2(10, 10), 0, new Vector3(1, 0, 0), 0.5f, 0, 0);

        [OneTimeSetUp]
        public void Setup()
        {
            _service = new GraphicsDeviceServiceMock();
            _device = _service.GraphicsDevice;
            _white = new Texture2D(_device, 1, 1);
            _white.SetData(new[] { Color.White });
            _content = Content(_service);
        }

        // Shaders/Particle comes out of this test's own copy of the content.
        internal static ContentManager Content(IGraphicsDeviceService service)
        {
            var services = new GameServiceContainer();
            services.AddService(service);
            return new ContentManager(services, Path.Combine(TestContext.CurrentContext.TestDirectory, "Content"));
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            _white.Dispose();
            _content.Dispose();
            _service.Release();
        }

        // Draws onto mid grey and returns one pixel from the middle.
        Vector4 Render(Action draw)
        {
            using var target = new RenderTarget2D(_device, 4, 4, false, SurfaceFormat.Color, DepthFormat.None);
            _device.SetRenderTarget(target);
            _device.Clear(new Color(128, 128, 128, 255));
            _device.DepthStencilState = DepthStencilState.None;
            _device.RasterizerState = RasterizerState.CullNone;
            draw();
            _device.SetRenderTarget(null);
            var pixels = new Color[16];
            target.GetData(pixels);
            return pixels[5].ToVector4();
        }

        [TestCase(true, 0f, 0.751f, 0.251f, Description = "alpha: red at half over grey")]
        [TestCase(true, 1f, 1.0f, 0.502f, Description = "additive: red added to grey")]
        [TestCase(true, 0.5f, 0.876f, 0.376f, Description = "half additive")]
        [TestCase(false, 0f, 0.751f, 0.251f, Description = "straight-alpha texture, alpha blend")]
        [TestCase(false, 1f, 1.0f, 0.502f, Description = "straight-alpha texture, additive")]
        public void Blend_OverAKnownBackground(bool premultiplied, float additiveMul, float red, float greenAndBlue)
        {
            var settings = new EmitterSettings { Premultiplied = premultiplied, AdditiveMul = additiveMul };
            var effect = Shader;
            var camera = ParticleRenderItem.CameraAxes.From(View, Matrix.Identity);
            var pixel = Render(() => ParticleRenderItem.DrawEmitter(_device, effect, View, Matrix.Identity, camera, settings, _white, [RedHalfAlpha]));
            Assert.Multiple(() =>
            {
                Assert.That(pixel.X, Is.EqualTo(red).Within(0.02));
                Assert.That(pixel.Y, Is.EqualTo(greenAndBlue).Within(0.02));
                Assert.That(pixel.Z, Is.EqualTo(greenAndBlue).Within(0.02));
            });
        }

        // A colour of 4 lights a quarter-grey texel fully: a vertex colour alone stops at 1 and would leave it a quarter.
        [Test]
        public void HdrColour_IsNotClippedBeforeTheTexture()
        {
            using var quarter = new Texture2D(_device, 1, 1);
            quarter.SetData(new[] { new Color(64, 64, 64, 255) });
            var effect = Shader;
            var camera = ParticleRenderItem.CameraAxes.From(View, Matrix.Identity);
            var bright = RedHalfAlpha with { Colour = new Vector3(4, 0, 0), Alpha = 1 };
            var pixel = Render(() => ParticleRenderItem.DrawEmitter(_device, effect, View, Matrix.Identity, camera, new EmitterSettings(), quarter, [bright]));
            Assert.That(pixel.X, Is.EqualTo(1).Within(0.02));
        }

        // The glow pass adds only what lies over 1, here onto mid grey: 1.25 leaves a quarter, 0.8 nothing.
        [TestCase(1.25f, 0.752f)]
        [TestCase(0.8f, 0.502f)]
        public void GlowPass_DrawsWhatLiesOverOne(float colour, float red)
        {
            var effect = Shader;
            var camera = ParticleRenderItem.CameraAxes.From(View, Matrix.Identity);
            var particle = RedHalfAlpha with { Colour = new Vector3(colour, 0, 0), Alpha = 1 };
            var pixel = Render(() => ParticleRenderItem.DrawEmitter(_device, effect, View, Matrix.Identity, camera, new EmitterSettings { AdditiveMul = 1 }, _white, [particle], glow: true));
            Assert.That(pixel.X, Is.EqualTo(red).Within(0.02));
        }

        // The app's emissive pass, which it blooms: an effect draws there too, a tint of 2 leaving white.
        [Test]
        public void TheEmissivePass_DrawsTheEffectsGlow()
        {
            using var item = new ParticleRenderItem(OneParticle(null, new Vector4(2, 2, 2, 1)), NoTextures(), () => Shader);
            var pixel = Render(() => item.Draw(_device, View, RenderingTechnique.Emissive));
            Assert.That(new[] { pixel.X, pixel.Y, pixel.Z }, Is.All.EqualTo(1).Within(0.02));
        }

        // ps_soft_edge against the preview's ground, world y = 0: a small flat quad 0.5 m up, seen from 10 m straight above,
        // is 0.5 m of view ray from the ground, half of a 1 m soft_factor; below the ground it is hidden. The effect's own
        // y = 0 is not the ground: lifted 1 m by its matrix, a quad 0.5 m below its origin is 0.5 m up.
        [Test]
        public void SoftEdges_FadeIntoTheGround()
        {
            var looking = new ParticleRenderItem.CameraAxes(new Vector3(0, 10, 0), Vector3.Right, Vector3.Forward, Vector3.Down);
            float Alpha(EmitterSettings s, float height, ParticleRenderItem.CameraAxes camera) =>
                ParticleRenderItem.Quads(s, camera, [new(new Vector3(0, height, 0), Vector3.Zero, new Vector2(0.01f), 0, Vector3.One, 1, 0, 0)])[0].Color.A / 255f;
            EmitterSettings Soft(float factor, float strength = 1) => new() { Premultiplied = false, Orientation = QuadOrientation.Horizontal, SoftEdge = factor, SoftStrength = strength };
            Assert.Multiple(() =>
            {
                Assert.That(Alpha(Soft(0), 0.5f, looking), Is.EqualTo(1f), "no ps_soft_edge");
                Assert.That(Alpha(Soft(1), 0.5f, looking), Is.EqualTo(0.5f).Within(0.01));
                Assert.That(Alpha(Soft(1, 2), 0.5f, looking), Is.EqualTo(0.25f).Within(0.01));
                Assert.That(Alpha(Soft(1), -0.5f, looking), Is.Zero, "under the ground");
                var lifted = new ParticleRenderItem.CameraAxes(new Vector3(0, 9, 0), Vector3.Right, Vector3.Forward, Vector3.Down, ToWorld: Matrix.CreateTranslation(0, 1, 0));
                Assert.That(Alpha(Soft(1), -0.5f, lifted), Is.EqualTo(0.5f).Within(0.01), "the ground is the world's");
            });
        }

        // enable_trail_mode: one strip through the particles, oldest to newest, as wide as each, facing the camera, with
        // the texture cell running from the newest (its top, as a velocity-aligned quad's head) to the oldest.
        [Test]
        public void Ribbon_JoinsTheParticles_AsWideAsEach_TextureHeadToTail()
        {
            ParticleState At(float x, float width) => new(new Vector3(x, 0, 0), Vector3.Zero, new Vector2(width, width), 0, Vector3.One, 1, 0, 0);
            var vertices = ParticleRenderItem.Ribbon(new EmitterSettings { Trail = true }, ParticleRenderItem.CameraAxes.From(View, Matrix.Identity), [At(0, 0.2f), At(1, 0.4f), At(2, 0.6f)]);
            Assert.That(vertices, Has.Length.EqualTo(12), "two segments of two triangles");
            var edges = vertices.Select(v => (X: MathF.Round(v.Position.X, 3), HalfWidth: MathF.Round(MathF.Abs(v.Position.Y), 3), V: v.TextureCoordinate.Y)).Distinct().OrderBy(x => x.X).ToList();
            Assert.That(edges, Is.EqualTo(new[] { (0f, 0.1f, 1f), (1f, 0.2f, 0.5f), (2f, 0.3f, 0f) }));
            Assert.That(vertices.All(v => v.Position.Z == 0), Is.True, "faces the camera on +Z");
        }

        // Two particles off the sides of the view: as quads they leave its middle grey, as a trail the strip crosses it.
        [TestCase(false, 0.502f)]
        [TestCase(true, 1f)]
        public void TrailMode_DrawsTheStripBetweenTheParticles(bool trail, float red)
        {
            ParticleState At(float x) => new(new Vector3(x, 0, 0), Vector3.Zero, new Vector2(2, 2), 0, new Vector3(1, 0, 0), 1, 0, 0);
            var effect = Shader;
            var camera = ParticleRenderItem.CameraAxes.From(View, Matrix.Identity);
            var pixel = Render(() => ParticleRenderItem.DrawEmitter(_device, effect, View, Matrix.Identity, camera, new EmitterSettings { Trail = trail }, _white, [At(-4), At(4)]));
            Assert.That(pixel.X, Is.EqualTo(red).Within(0.02));
        }

        // The quad's bottom-left corner: a mirrored particle samples the texture's right edge there, and a quarter turn
        // counter-clockwise brings the texture's top-left corner down to it.
        [Test]
        public void Quads_MirrorAndTurnTheTexture()
        {
            var camera = ParticleRenderItem.CameraAxes.From(View, Matrix.Identity);
            Vector2 BottomLeft(EmitterSettings s, bool flipU) => ParticleRenderItem.Quads(s, camera, [RedHalfAlpha with { FlipU = flipU }])[0].TextureCoordinate;
            Assert.That(BottomLeft(new EmitterSettings(), false), Is.EqualTo(new Vector2(0, 1)));
            Assert.That(BottomLeft(new EmitterSettings(), true), Is.EqualTo(new Vector2(1, 1)));
            Assert.That(BottomLeft(new EmitterSettings { UvTurn = 1 }, false), Is.EqualTo(new Vector2(0, 0)));
        }

        // The axis each rare orientation's quad lies flat across, seen from (10, 10, 0): an upright quad turns about Y to face
        // the camera, and the local ones lie in the emitter's own frame, here turned 90 degrees about X or Y.
        [TestCase(QuadOrientation.Vertical, 0f, 0f, 0)]
        [TestCase(QuadOrientation.LocalHorizontal, 90f, 0f, 2)]
        [TestCase(QuadOrientation.LocalVertical, 0f, 90f, 0)]
        public void RareOrientations_LieFlatAcrossTheirAxis(QuadOrientation mode, float aboutX, float aboutY, int flat)
        {
            var camera = ParticleRenderItem.CameraAxes.From(View, Matrix.Identity) with { Position = new Vector3(10, 10, 0) };
            var frame = Matrix.CreateRotationX(MathHelper.ToRadians(aboutX)) * Matrix.CreateRotationY(MathHelper.ToRadians(aboutY));
            var corners = ParticleRenderItem.Quads(new EmitterSettings { Orientation = mode }, camera, [RedHalfAlpha], frame).Select(v => v.Position).ToList();
            float Axis(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;
            Assert.That(corners.Max(v => Math.Abs(Axis(v, flat))), Is.LessThan(1e-3f));
            foreach (var other in new[] { 0, 1, 2 }.Where(i => i != flat))
                Assert.That(corners.Max(v => Axis(v, other)) - corners.Min(v => Axis(v, other)), Is.GreaterThan(9f), $"spans axis {other}");
        }

        // ParticleCullDistances fades a particle in from x to y metres from the camera and out from z to w; CullDistances does
        // the same to the whole emitter by its own distance. Seen from 3 m, the middle of a 2..4 band is half faded.
        [Test]
        public void CullDistances_FadeByDistanceFromTheCamera()
        {
            var camera = ParticleRenderItem.CameraAxes.From(View, Matrix.Identity) with { Position = new Vector3(0, 0, 3) };
            float Alpha(EmitterSettings s) => ParticleRenderItem.Quads(s, camera, [RedHalfAlpha])[0].Color.A / 255f;
            Assert.Multiple(() =>
            {
                Assert.That(Alpha(new EmitterSettings { Premultiplied = false }), Is.EqualTo(0.5f).Within(0.01));
                Assert.That(Alpha(new EmitterSettings { Premultiplied = false, ParticleCull = new Vector4(2, 4, 100, 200) }), Is.EqualTo(0.25f).Within(0.01));
                Assert.That(Alpha(new EmitterSettings { Premultiplied = false, EmitterCull = new Vector4(0, 0, 1, 5) }), Is.EqualTo(0.25f).Within(0.01));
                Assert.That(Alpha(new EmitterSettings { Premultiplied = false, ParticleCull = new Vector4(0, 0, 1, 2) }), Is.EqualTo(0f), "past the far distance");
            });
        }

        // vs_rendersize_limiter: a 10 cm spark 100 m away covers a tenth of a pixel where a metre at a metre is 100 pixels; a
        // 2 pixel limit grows it to 2 m across, and without one it stays as it is.
        [TestCase(0f, 0.1f)]
        [TestCase(2f, 2f)]
        public void RenderSizeLimiter_KeepsAFarParticleThisManyPixelsAcross(float minPixels, float across)
        {
            var camera = ParticleRenderItem.CameraAxes.From(View, Matrix.Identity) with { Position = new Vector3(0, 0, 100), PixelScale = 100 };
            var spark = RedHalfAlpha with { Size = new Vector2(0.1f, 0.1f) };
            var xs = ParticleRenderItem.Quads(new EmitterSettings { MinPixels = minPixels }, camera, [spark]).Select(v => v.Position.X).ToList();
            Assert.That(xs.Max() - xs.Min(), Is.EqualTo(across).Within(1e-3));
        }

        [Test]
        public void UvRect_AtlasCellThenFlipbookCell()
        {
            var atlas = new EmitterSettings { AtlasX = 8, AtlasY = 8, AtlasCell = 51, CellsX = 2, CellsY = 2 };
            Assert.That(ParticleRenderItem.UvRect(atlas, 3), Is.EqualTo(new Vector4(0.4375f, 0.8125f, 0.0625f, 0.0625f)));
            var strip = new EmitterSettings { CellsX = 8, CellsY = 8 };
            Assert.That(ParticleRenderItem.UvRect(strip, 9), Is.EqualTo(new Vector4(0.125f, 0.125f, 0.125f, 0.125f)));
        }

        // One particle 10 m across at the origin, filling the view.
        static ParticleSimulator OneParticle(string? texture, Vector4? tint = null)
        {
            var settings = new EmitterSettings
            {
                Name = "test", InfiniteLife = true, Rate = VfxCurve.Constant(60), MaxParticles = 1, ParticleLife = VfxCurve.Constant(100),
                InitialScale = VfxCurve.Constant(10, 10, 10), Texture = texture, TintMin = tint ?? Vector4.One, TintMax = tint ?? Vector4.One,
            };
            var simulator = new ParticleSimulator([settings], 1, _ => Matrix.Identity);
            simulator.AdvanceTo(0.5f);
            return simulator;
        }

        static VfxTextureCache NoTextures() => new((_, _) => null);

        [Test]
        public void MissingTexture_DrawsWhite()
        {
            using var item = new ParticleRenderItem(OneParticle(@"vfx\textures\missing.dds"), NoTextures(), () => Shader);
            var pixel = Render(() => item.Draw(_device, View, RenderingTechnique.Normal));
            Assert.That(new[] { pixel.X, pixel.Y, pixel.Z }, Is.All.EqualTo(1).Within(0.02));
        }

        // SuperView disposes an effect twice (CleanUp, then the scene's own dispose), and a frame can land
        // between: a draw after Dispose must build its effect again, not draw with disposed objects.
        [Test]
        public void DrawAfterDispose_DrawsAgain()
        {
            using var item = new ParticleRenderItem(OneParticle(null), NoTextures(), () => Shader);
            Render(() => item.Draw(_device, View, RenderingTechnique.Normal));
            item.Dispose();
            var pixel = Render(() => item.Draw(_device, View, RenderingTechnique.Normal));
            Assert.That(new[] { pixel.X, pixel.Y, pixel.Z }, Is.All.EqualTo(1).Within(0.02));
        }

        // SuperView's scene is disposed node by node when its editor closes, and only IDisposable nodes are
        // reached: a VfxEffectNode that was not one kept its GPU objects for the life of the shared device.
        [Test]
        public void DisposingTheEffectNode_DisposesItsRenderItem()
        {
            var item = new ParticleRenderItem(OneParticle(null), NoTextures(), () => Shader);
            Render(() => item.Draw(_device, View, RenderingTechnique.Normal));
            (new VfxEffectNode("test", item) as IDisposable)?.Dispose();
            Assert.That(item.IsDisposed, Is.True);
        }

        // A SuperView rebuild (every click in its tag list) makes new render items, and each one decoded its
        // textures again: about half a second per effect, measured on the firebox. The editor's cache loads a
        // path once and owns what it loads; a render item disposes only its own objects.
        [Test]
        public void TextureCache_RemembersAPathThatDidNotLoad()
        {
            using var cache = NoTextures();
            using var item = new ParticleRenderItem(OneParticle("broken"), cache, () => Shader);
            Assert.That(cache.Failed("broken"), Is.False, "not tried yet");
            Render(() => item.Draw(_device, View, RenderingTechnique.Normal));
            Assert.That((cache.Failed("broken"), cache.Failed("other")), Is.EqualTo((true, false)));
        }

        // A texture replaced in a pack: the cache lets go of the old one, by either separator, and loads the new one.
        [Test]
        public void TextureCache_ForgetsAChangedPath()
        {
            var loads = 0;
            var first = new Texture2D(_device, 1, 1);
            using var cache = new VfxTextureCache((_, _) => ++loads == 1 ? first : new Texture2D(_device, 1, 1));
            cache.Load(_device, @"vfx\textures\a.dds");
            cache.Forget("vfx/textures/A.dds");
            Assert.That(first.IsDisposed, Is.True);
            Assert.That(cache.Load(_device, @"vfx\textures\a.dds"), Is.Not.SameAs(first));
            Assert.That(loads, Is.EqualTo(2));
        }

        [Test]
        public void TextureCache_LoadsEachPathOnce_AndOwnsWhatItLoads()
        {
            var loads = 0;
            var texture = new Texture2D(_device, 1, 1);
            var cache = new VfxTextureCache((_, _) => { loads++; return texture; });
            for (var i = 0; i < 2; i++)
            {
                using var item = new ParticleRenderItem(OneParticle("t"), cache, () => Shader);
                Render(() => item.Draw(_device, View, RenderingTechnique.Normal));
            }
            Assert.Multiple(() =>
            {
                Assert.That(loads, Is.EqualTo(1), "loaded once for both render items");
                Assert.That(texture.IsDisposed, Is.False, "a render item leaves the cache's texture alone");
            });
            cache.Dispose();
            Assert.That(texture.IsDisposed, Is.True);
        }
    }
}
