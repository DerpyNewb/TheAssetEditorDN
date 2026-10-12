using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.SceneNodes;
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

        // Draws onto mid grey, or the background given, and returns one pixel from the middle.
        Vector4 Render(Action draw, Color? background = null)
        {
            // Preserved as the app's are: a distortion copies the target and draws on, and a discarded one comes back cleared.
            using var target = new RenderTarget2D(_device, 4, 4, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            _device.SetRenderTarget(target);
            _device.Clear(background ?? new Color(128, 128, 128, 255));
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

        // The app draws its scene onto nothing and lays it over the background by its alpha, so over nothing a particle's
        // alpha is what it hides of the background: half for one laid over at half, none for light it adds.
        [TestCase(false, 0f, 0.5f, Description = "straight-alpha texture, alpha blend")]
        [TestCase(false, 1f, 0f, Description = "straight-alpha texture, additive")]
        [TestCase(true, 0f, 0.5f, Description = "premultiplied, alpha blend")]
        [TestCase(true, 1f, 0f, Description = "premultiplied, additive")]
        public void OverNothing_AParticleHidesWhatItCovers(bool premultiplied, float additiveMul, float alpha)
        {
            var settings = new EmitterSettings { Premultiplied = premultiplied, AdditiveMul = additiveMul };
            var camera = ParticleRenderItem.CameraAxes.From(View, Matrix.Identity);
            var pixel = Render(() => ParticleRenderItem.DrawEmitter(_device, Shader, View, Matrix.Identity, camera, settings, _white, [RedHalfAlpha]), Color.Transparent);
            Assert.That(pixel.W, Is.EqualTo(alpha).Within(0.02));
        }

        // ps_texture reads a texture's alpha as sRGB, as the hardware reads its colour off a BC7_UNORM_SRGB file: CA's ps_main
        // linearises it before anything else uses it. A white texel at alpha 128 covers 21.6 % of black, not half.
        [TestCase(true)]
        [TestCase(false)]
        public void TextureAlpha_IsReadAsSrgb(bool premultiplied)
        {
            using var texture = new Texture2D(_device, 1, 1);
            texture.SetData(new[] { premultiplied ? new Color(128, 128, 128, 128) : new Color(255, 255, 255, 128) });
            var camera = ParticleRenderItem.CameraAxes.From(View, Matrix.Identity);
            var white = new ParticleState(Vector3.Zero, Vector3.Zero, new Vector2(10, 10), 0, Vector3.One, 1, 0, 0);
            var pixel = Render(() => ParticleRenderItem.DrawEmitter(_device, Shader, View, Matrix.Identity, camera,
                new EmitterSettings { Premultiplied = premultiplied }, texture, [white]), Color.Black);
            Assert.That(pixel.X, Is.EqualTo(0.216).Within(0.01));
        }

        // A light's pool and a heat haze hide nothing over nothing: the pool's unlit corners and a haze with no scene behind
        // it blacked out the background.
        [Test]
        public void OverNothing_ALightOrAHazeHidesNothing()
        {
            var down = new CommonShaderParameters(Matrix.CreateLookAt(new Vector3(0, 5, 0), Vector3.Zero, Vector3.Forward),
                Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, 0.1f, 100), new Vector3(0, 5, 0), Vector3.Zero, 0, 0, 0, 1, [], Vector3.One);
            var light = new ParticleState(new Vector3(0, 20, 0), Vector3.Zero, new Vector2(40, 40), 0, new Vector3(0.4f, 0, 0), 1, 0, 0);
            var pool = Render(() => ParticleRenderItem.DrawEmitter(_device, Shader, down, Matrix.Identity, ParticleRenderItem.CameraAxes.From(down, Matrix.Identity),
                new EmitterSettings { Decal = true, Light = true }, _white, [light]), Color.Transparent);
            var normal = new Texture2D(_device, 1, 1);
            normal.SetData(new[] { new Color(255, 128, 255, 255) });
            using var cache = new VfxTextureCache((_, _) => normal);
            using var item = new ParticleRenderItem(OneParticle("normal", distortion: true), cache, () => Shader);
            var haze = Render(() => item.Draw(_device, View, RenderingTechnique.Normal), Color.Transparent);
            Assert.That(new[] { pool.W, haze.W }, Is.All.EqualTo(0).Within(0.01));
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

        // ps_soft_edge against the solid scene, when the engine has drawn its depth: a white quad 5 m from the camera, well above the
        // ground, with the scene 0.5 m behind it is half faded at a 1 m soft_factor, and with it 2 m behind not at all; the same
        // through the app's orthographic view. A scale_strength of 0 (33 vanilla chains) fades nothing, as on the ground.
        [TestCase(5.5f, 1f, false, 0.5f)]
        [TestCase(7f, 1f, false, 1f)]
        [TestCase(5.5f, 1f, true, 0.5f)]
        [TestCase(4f, 0f, false, 1f)]
        public void SoftEdges_FadeIntoTheSolidScene(float sceneDepth, float strength, bool orthographic, float white)
        {
            using var depth = new Texture2D(_device, 4, 4, false, SurfaceFormat.Single);
            depth.SetData(Enumerable.Repeat(sceneDepth, 16).ToArray());
            var level = Level(depth, orthographic);
            var camera = ParticleRenderItem.CameraAxes.From(level, Matrix.Identity);
            var quad = new ParticleState(new Vector3(0, 10, 0), Vector3.Zero, new Vector2(10, 10), 0, Vector3.One, 1, 0, 0);
            var settings = new EmitterSettings { Premultiplied = false, SoftEdge = 1, SoftStrength = strength };
            var pixel = Render(() => ParticleRenderItem.DrawEmitter(_device, Shader, level, Matrix.Identity, camera, settings, _white, [quad]), Color.Black);
            Assert.That(pixel.X, Is.EqualTo(white).Within(0.02));
        }

        // The glow pass draws onto a target holding only the emissive models' depth, so the scene's depth hides a particle's glow
        // behind a plain model: a glowing quad 5 m away adds nothing behind a model 3 m away, and its glow in front of one at 7 m.
        [TestCase(3f, false)]
        [TestCase(7f, true)]
        public void GlowPass_IsHiddenBehindTheSolidScene(float sceneDepth, bool glows)
        {
            using var depth = new Texture2D(_device, 4, 4, false, SurfaceFormat.Single);
            depth.SetData(Enumerable.Repeat(sceneDepth, 16).ToArray());
            var level = Level(depth, false);
            var camera = ParticleRenderItem.CameraAxes.From(level, Matrix.Identity);
            var quad = new ParticleState(new Vector3(0, 10, 0), Vector3.Zero, new Vector2(10, 10), 0, new Vector3(4, 0, 0), 1, 0, 0);
            var pixel = Render(() => ParticleRenderItem.DrawEmitter(_device, Shader, level, Matrix.Identity, camera, new EmitterSettings { Premultiplied = false }, _white, [quad], glow: true), Color.Black);
            Assert.That(pixel.X > 0.5f, Is.EqualTo(glows), $"red {pixel.X}");
        }

        // Looking level along -Z at a point 10 m up from 5 m away, in perspective or the app's orthographic view, over a scene depth.
        static CommonShaderParameters Level(Texture2D depth, bool orthographic) =>
            new(Matrix.CreateLookAt(new Vector3(0, 10, 5), new Vector3(0, 10, 0), Vector3.Up),
                orthographic ? Matrix.CreateOrthographic(12, 12, 0.1f, 100) : Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, 0.1f, 100),
                new Vector3(0, 10, 5), new Vector3(0, 10, 0), 0, 0, 0, 1, [], Vector3.One) { SceneDepth = depth };

        // cell_blend: half way from a red cell to a blue one draws both at half.
        [TestCase(0f, 1f, 0f)]
        [TestCase(0.5f, 0.5f, 0.5f)]
        public void CellBlend_MixesTheCellWithTheNext(float blend, float red, float blue)
        {
            using var strip = new Texture2D(_device, 2, 1);
            strip.SetData(new[] { new Color(255, 0, 0, 255), new Color(0, 0, 255, 255) });
            var camera = ParticleRenderItem.CameraAxes.From(View, Matrix.Identity);
            var particle = RedHalfAlpha with { Colour = Vector3.One, Alpha = 1, Cell = 0, NextCell = 1, CellBlend = blend };
            var pixel = Render(() =>
            {
                _device.SamplerStates[0] = SamplerState.PointClamp;
                ParticleRenderItem.DrawEmitter(_device, Shader, View, Matrix.Identity, camera, new EmitterSettings { CellsX = 2 }, strip, [particle]);
            });
            Assert.That((pixel.X, pixel.Z), Is.EqualTo((red, blue)).Using<(float, float)>((a, b) => Math.Abs(a.Item1 - b.Item1) < 0.02f && Math.Abs(a.Item2 - b.Item2) < 0.02f));
        }

        // ps_alpha_crush on a texel of alpha 0.75 (225, read as sRGB) over mid grey: a soft crush of 0.5 leaves half of it, a
        // sharp one of 0.8 cuts it away and one of 0.5 leaves it whole.
        [TestCase(false, 0f, 0.875f)]
        [TestCase(false, 0.5f, 0.751f)]
        [TestCase(true, 0.8f, 0.502f)]
        [TestCase(true, 0.5f, 0.875f)]
        public void AlphaCrush_ThinsTheTexture(bool sharp, float crush, float grey)
        {
            using var faint = new Texture2D(_device, 1, 1);
            faint.SetData(new[] { new Color(225, 225, 225, 225) });
            var camera = ParticleRenderItem.CameraAxes.From(View, Matrix.Identity);
            var particle = RedHalfAlpha with { Colour = Vector3.One, Alpha = 1, Crush = crush };
            var pixel = Render(() => ParticleRenderItem.DrawEmitter(_device, Shader, View, Matrix.Identity, camera, new EmitterSettings { CrushSharp = sharp }, faint, [particle]));
            Assert.That(pixel.X, Is.EqualTo(grey).Within(0.02));
        }

        // ps_gradient: a half-grey texel picks the colour half way along the strip, here red rising 0 to 1, no green, full blue.
        [Test]
        public void Gradient_ColoursTheTextureByItsValue()
        {
            using var grey = new Texture2D(_device, 1, 1);
            grey.SetData(new[] { new Color(128, 128, 128, 255) });
            var camera = ParticleRenderItem.CameraAxes.From(View, Matrix.Identity);
            var ramp = VfxCurve.Parse("up,1,0;0.0,0.0,linear,linear,0,0,0,0,0;1.0,1.0,linear,linear,0,0,0,0,0|none,1,0;0.0,0.0,linear,linear,0,0,0,0,0|full,1,0;0.0,1.0,linear,linear,0,0,0,0,0:r,0;0,0|g,0;1,1|b,0;2,2");
            var settings = new EmitterSettings { Gradient = ramp, GradientAlpha = VfxCurve.Constant(1) };
            var particle = RedHalfAlpha with { Colour = Vector3.One, Alpha = 1 };
            var pixel = Render(() => ParticleRenderItem.DrawEmitter(_device, Shader, View, Matrix.Identity, camera, settings, grey, [particle]));
            Assert.That(new[] { pixel.X, pixel.Y, pixel.Z }, Is.EqualTo(new[] { 0.5f, 0f, 1f }).Within(0.03));
        }

        // A decal lies flat on the world's ground under its particle, whatever its own orientation and however the effect is
        // carried: here a camera-facing particle 2 m up in an effect lifted 1 m and tipped 90 degrees about X.
        [Test]
        public void Decal_LiesFlatOnTheGroundUnderItsParticle()
        {
            var effect = Matrix.CreateRotationX(MathHelper.PiOver2) * Matrix.CreateTranslation(3, 1, 0);
            var camera = ParticleRenderItem.CameraAxes.From(View, effect);
            var particle = new ParticleState(new Vector3(0, 2, 0), Vector3.Zero, new Vector2(2, 2), 0, Vector3.One, 1, 0, 0);
            var corners = ParticleRenderItem.Quads(new EmitterSettings { Decal = true }, camera, [particle]).Select(v => Vector3.Transform(v.Position, effect)).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(corners.Select(c => c.Y), Is.All.EqualTo(0.01f).Within(1e-4), "on the ground");
                var middle = corners.Aggregate(Vector3.Zero, (sum, c) => sum + c) / corners.Count;
                Assert.That(new[] { middle.X, middle.Z }, Is.EqualTo(new[] { 3f, 2f }).Within(1e-4), "under the particle");
                Assert.That(corners.Max(c => c.X) - corners.Min(c => c.X), Is.EqualTo(2).Within(1e-3), "as wide as it");
            });
        }

        // A dynamic light lights the ground under it: a pool as wide as the particle, falling off with the distance from the
        // light as (1 - (d/r)^2)^2. Seen from above, a light half its radius up lights the centre 0.5625 of its colour; one a
        // radius up lights nothing.
        [TestCase(10f, 0.502f + 0.4f * 0.5625f)]
        [TestCase(20f, 0.502f)]
        public void Light_LightsTheGroundUnderIt(float height, float red)
        {
            var down = new CommonShaderParameters(Matrix.CreateLookAt(new Vector3(0, 5, 0), Vector3.Zero, Vector3.Forward),
                Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, 0.1f, 100), new Vector3(0, 5, 0), Vector3.Zero, 0, 0, 0, 1, [], Vector3.One);
            var camera = ParticleRenderItem.CameraAxes.From(down, Matrix.Identity);
            var light = new ParticleState(new Vector3(0, height, 0), Vector3.Zero, new Vector2(40, 40), 0, new Vector3(0.4f, 0, 0), 1, 0, 0);
            var pixel = Render(() => ParticleRenderItem.DrawEmitter(_device, Shader, down, Matrix.Identity, camera, new EmitterSettings { Decal = true, Light = true }, _white, [light]));
            Assert.That(new[] { pixel.X, pixel.Y }, Is.EqualTo(new[] { red, 0.502f }).Within(0.01));
        }

        // DISTORTION draws the scene behind it shifted by its normal map. A red-and-blue split seen from 1 m: a full +x normal
        // brings the blue half under the pixel, a flat one leaves it red.
        [TestCase(255, 0f, 1f)]
        [TestCase(128, 1f, 0f)]
        public void Distortion_ShiftsTheSceneBehind(int normalX, float red, float blue)
        {
            var close = new CommonShaderParameters(Matrix.CreateLookAt(new Vector3(0, 0, 1), Vector3.Zero, Vector3.Up),
                Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, 0.1f, 100), new Vector3(0, 0, 1), Vector3.Zero, 0, 0, 0, 1, [], Vector3.One);
            var normal = new Texture2D(_device, 1, 1);
            normal.SetData(new[] { new Color(normalX, 128, 255, 255) });
            using var split = new Texture2D(_device, 4, 1);
            split.SetData(new[] { Color.Red, Color.Red, Color.Blue, Color.Blue });
            using var batch = new SpriteBatch(_device);
            using var cache = new VfxTextureCache((_, _) => normal);
            using var item = new ParticleRenderItem(OneParticle("normal", distortion: true), cache, () => Shader);
            var pixel = Render(() =>
            {
                batch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp);
                batch.Draw(split, new Rectangle(0, 0, 4, 4), Color.White);
                batch.End();
                _device.DepthStencilState = DepthStencilState.None;
                _device.RasterizerState = RasterizerState.CullNone;
                item.Draw(_device, close, RenderingTechnique.Normal);
            });
            Assert.That(new[] { pixel.X, pixel.Z }, Is.EqualTo(new[] { red, blue }).Within(0.05));
        }

        // A rigid particle's model: its pivot (a share of its box) on the particle, scaled by its size, turned X, Y, then Z.
        [Test]
        public void RigidMatrix_PutsThePivotOnTheParticle()
        {
            var box = new BoundingBox(Vector3.Zero, new Vector3(2, 4, 2));
            var m = VfxConventions.RigidMatrix(box, new Vector3(0.5f, 0, 0.5f), 3, new Vector3(0, MathHelper.PiOver2, 0), Matrix.Identity, new Vector3(10, 0, 0));
            Assert.That(Vector3.Distance(Vector3.Transform(new Vector3(1, 0, 1), m), new Vector3(10, 0, 0)), Is.LessThan(1e-4), "the pivot");
            Assert.That(Vector3.Distance(Vector3.Transform(new Vector3(2, 0, 1), m), new Vector3(10, 0, -3)), Is.LessThan(1e-4), "+X a metre off, scaled 3, turned to -Z");
            var framed = VfxConventions.RigidMatrix(box, new Vector3(0.5f, 0, 0.5f), 3, new Vector3(0, MathHelper.PiOver2, 0), Matrix.CreateRotationX(MathHelper.PiOver2), new Vector3(10, 0, 0));
            Assert.That(Vector3.Distance(Vector3.Transform(new Vector3(2, 0, 1), framed), new Vector3(10, 3, 0)), Is.LessThan(1e-4), "then turned by its emitter's frame");
        }

        // The preview's PBR materials stand in for CA's character and rigid shaders; the rest (Tidecall's water, the dissolves,
        // the unlit vortices: most of vanilla's rigid VFX models) are CA's own, which it cannot run.
        [TestCase("shaders/wh2_vfx_cst_tidecall.xml.shader", true)]
        [TestCase("shaders/wh3_vfx_rigid_emissive_dissolve.xml.shader", true)]
        [TestCase("shaders/campaign_vortex_unlit.xml.shader", true)]
        [TestCase("shaders/weighted4_character_alpha.xml.shader", false)]
        [TestCase("shaders/weighted2_character_emissive.xml.shader", false)]
        [TestCase("shaders/rigid_character.xml.shader", false)]
        [TestCase("shaders/rigid_default.xml.shader", false)]
        [TestCase("shaders/weighted2_default.xml.shader", false)]
        [TestCase("SHADERS/System/Weighted_Standard_4.xml.shader", false)]
        [TestCase("", false)]
        public void IsCustomShader_IsAnyButTheCharacterAndRigidOnes(string shader, bool custom) =>
            Assert.That(VfxConventions.IsCustomShader(shader), Is.EqualTo(custom));

        // A rigid particle's AnimFile plays at AnimSpeed from the particle's birth and loops: most of vanilla's 118 are run
        // cycles, idles and tentacles. A clip with no length holds its first frame.
        [TestCase(0.5f, 2f, 4f, 0.25f)]
        [TestCase(5f, 1f, 2f, 0.5f)]
        [TestCase(1f, 1f, 0f, 0f)]
        public void AnimShare_PlaysAtItsSpeedAndLoops(float age, float speed, float length, float share) =>
            Assert.That(VfxConventions.AnimShare(age, speed, length), Is.EqualTo(share).Within(1e-5));

        // A rigid particle takes its emitter's turn, without its place or scale; one RELATIVE_TO_TERRAIN_NORMAL stands on the
        // preview's flat ground, so it keeps only the emitter's heading.
        [Test]
        public void RigidFrame_TurnsWithTheEmitter_AndOnTheGroundKeepsOnlyItsHeading()
        {
            var frame = Matrix.CreateScale(2) * Matrix.CreateRotationX(MathHelper.PiOver4) * Matrix.CreateRotationY(MathHelper.PiOver2) * Matrix.CreateTranslation(5, 6, 7);
            var turned = VfxConventions.RigidFrame(frame, onTerrain: false);
            var heading = VfxConventions.RigidFrame(frame, onTerrain: true);
            Assert.Multiple(() =>
            {
                Assert.That(Vector3.Distance(Vector3.Transform(Vector3.UnitY, turned), Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, frame))), Is.LessThan(1e-4), "turned");
                Assert.That(Vector3.Distance(Vector3.Transform(Vector3.UnitY, heading), Vector3.UnitY), Is.LessThan(1e-4), "upright");
                Assert.That(Vector3.Distance(Vector3.Transform(Vector3.UnitX, heading), -Vector3.UnitZ), Is.LessThan(1e-4), "heading");
            });
        }

        // Each model is loaded once per editor, a miss included; one that loads with no meshes draws nothing.
        [Test]
        public void MeshCache_LoadsEachModelOnce()
        {
            var loads = 0;
            using var cache = new VfxMeshCache(path => { loads++; return path == "missing" ? null : new GroupNode("empty"); });
            cache.Get("vfx/a.wsmodel");
            cache.Get(@"VFX\A.wsmodel");
            cache.Get("missing");
            cache.Get("missing");
            Assert.That((loads, cache.Get("vfx/a.wsmodel").Meshes.Count), Is.EqualTo((2, 0)));
        }

        // A wsmodel is built from a geometry file and material files the cache does not track, so a change to any model file
        // reloads every model; a texture or an effect file reloads none.
        [TestCase(@"variantmeshes\wh_variantmodels\x\x.rigid_model_v2", 2)]
        [TestCase("vfx/models/b.wsmodel", 2)]
        [TestCase("vfx/materials/wave.xml.material", 2)]
        [TestCase("vfx/textures/wave.dds", 1)]
        [TestCase("vfx/fx.xml", 1)]
        public void MeshCache_ForgetsEveryModelWhenAModelFileChanges(string changed, int loads)
        {
            var count = 0;
            using var cache = new VfxMeshCache(path => { count++; return new GroupNode("empty"); });
            cache.Get("vfx/models/a.wsmodel");
            cache.Forget(changed);
            cache.Get("vfx/models/a.wsmodel");
            Assert.That(count, Is.EqualTo(loads));
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

        // A trail draws as its particles would as quads: mirrored by its newest particle's flip, grown to vs_rendersize_limiter's
        // pixels across (2 m for a 10 cm strip 100 m away, where a metre at a metre is 100 pixels), and faded by ps_soft_edge where
        // it runs into the ground (half, 0.5 m up under a camera looking down, with a 1 m soft_factor).
        [Test]
        public void Ribbon_FlipsGrowsAndSoftensLikeAQuad()
        {
            ParticleState At(float x, float y = 0, float width = 0.1f, bool flipU = false) =>
                new(new Vector3(x, y, 0), Vector3.Zero, new Vector2(width), 0, Vector3.One, 1, 0, 0, FlipU: flipU);
            var camera = ParticleRenderItem.CameraAxes.From(View, Matrix.Identity);
            var far = camera with { Position = new Vector3(0, 0, 100), PixelScale = 100 };
            var looking = new ParticleRenderItem.CameraAxes(new Vector3(0, 10, 0), Vector3.Right, Vector3.Forward, Vector3.Down);
            var trail = new EmitterSettings { Trail = true, Premultiplied = false };
            Assert.Multiple(() =>
            {
                Assert.That(ParticleRenderItem.Ribbon(trail, camera, [At(0), At(1)])[0].TextureCoordinate.X, Is.EqualTo(0f), "plain");
                Assert.That(ParticleRenderItem.Ribbon(trail, camera, [At(0), At(1, flipU: true)])[0].TextureCoordinate.X, Is.EqualTo(1f), "mirrored");
                var grown = ParticleRenderItem.Ribbon(new EmitterSettings { Trail = true, MinPixels = 2 }, far, [At(0), At(1)]);
                Assert.That(grown.Max(v => MathF.Abs(v.Position.Y)), Is.EqualTo(1f).Within(1e-2), "grown");
                var soft = ParticleRenderItem.Ribbon(new EmitterSettings { Trail = true, Premultiplied = false, SoftEdge = 1, SoftStrength = 1 }, looking,
                    [At(0, 0.5f, 0.01f), At(1, 0.5f, 0.01f)]);
                Assert.That(soft.Max(v => v.Color.A / 255f), Is.EqualTo(0.5f).Within(0.02), "softened");
            });
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
        static ParticleSimulator OneParticle(string? texture, Vector4? tint = null, bool distortion = false) => Particles(Filling(texture, tint, distortion));

        // An emitter of one particle 10 m across, z metres along the view axis.
        static EmitterSettings Filling(string? texture, Vector4? tint = null, bool distortion = false, float z = 0) => new()
        {
            Name = "test", InfiniteLife = true, Rate = VfxCurve.Constant(60), MaxParticles = 1, ParticleLife = VfxCurve.Constant(100),
            InitialScale = VfxCurve.Constant(10, 10, 10), Texture = texture, TintMin = tint ?? Vector4.One, TintMax = tint ?? Vector4.One, Distortion = distortion,
            ReferenceTransform = Matrix.CreateTranslation(0, 0, z),
        };

        static ParticleSimulator Particles(params EmitterSettings[] settings)
        {
            var simulator = new ParticleSimulator(settings, 1, _ => Matrix.Identity);
            simulator.AdvanceTo(0.5f);
            return simulator;
        }

        // A heat haze shifts what lies behind it when it draws, the effect's own particles included: copied once before any
        // of them, it pasted the bare scene over the mist (tidecall's ripples over its splashes). A flat normal shifts
        // nothing, so the red particle behind stays red.
        [Test]
        public void Distortion_ShiftsTheEffectsOwnParticlesBehindIt()
        {
            var normal = new Texture2D(_device, 1, 1);
            normal.SetData(new[] { new Color(128, 128, 255, 255) });
            using var cache = new VfxTextureCache((_, _) => normal);
            using var item = new ParticleRenderItem(Particles(Filling(null, new Vector4(1, 0, 0, 1), z: -1), Filling("normal", distortion: true)), cache, () => Shader);
            var pixel = Render(() => item.Draw(_device, View, RenderingTechnique.Normal));
            Assert.That(new[] { pixel.X, pixel.Y }, Is.EqualTo(new[] { 1f, 0f }).Within(0.02));
        }

        // The haze's copy of the scene leaves the caller's sampler as it found it: it left point sampling behind.
        [Test]
        public void Distortion_LeavesTheCallersSampler()
        {
            var normal = new Texture2D(_device, 1, 1);
            normal.SetData(new[] { new Color(128, 128, 255, 255) });
            using var cache = new VfxTextureCache((_, _) => normal);
            using var item = new ParticleRenderItem(OneParticle("normal", distortion: true), cache, () => Shader);
            SamplerState? after = null;
            Render(() =>
            {
                _device.SamplerStates[0] = SamplerState.AnisotropicWrap;
                item.Draw(_device, View, RenderingTechnique.Normal);
                after = _device.SamplerStates[0];
            });
            Assert.That(after, Is.SameAs(SamplerState.AnisotropicWrap));
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
