using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using static GameWorld.Core.Test.Vfx.VfxTestFiles;

namespace GameWorld.Core.Test.Vfx
{
    public class EmitterSettingsTests
    {
        // Cut from wh3_dlc23_lib_warmachines: a premultiplied smoke flipbook and an additive atlas spark.
        const string SmokeChain = """<behaviour_chain id="smoke"><quality value="0"><modifier fragment="ps_texture"><texture_property name="diffuse" type="file" path="vfx\textures\smoke_dense_loop_diffuse.dds"/><property name="pre_multiplied_alpha" type="bool" value="true"/></modifier><modifier fragment="vs_animated_texture"><property name="no_cells_xy" type="float2" value="float2(8.0,8.0)"/><property name="loop" type="bool" value="true"/><property name="no_loops" type="float2" value="float2(0.5,0.5)"/><property name="time_mode" type="list" value="using_particle_lifetime"/></modifier><modifier fragment="ps_lighting"><property name="additive_blend_mul" type="float" value="0.0"/></modifier><modifier fragment="vs_translation"><property name="pivot_x" type="float" value="0.5"/><property name="pivot_y" type="float" value="0.25"/></modifier></quality></behaviour_chain>""";
        const string SparksChain = """<behaviour_chain id="sparks"><quality value="0"><modifier fragment="ps_texture"><texture_property name="diffuse" type="file" path="vfx\textures\sparks.dds"/><property name="pre_multiplied_alpha" type="bool" value="false"/></modifier><modifier fragment="vs_translation_velocity_v2"><property name="velocity_stretch_mul" type="curve">x_min,0,0;0.0,0.5,linear,linear,-0.05,0.0,0.05,0.0,1|x_max,0,0;0.0,1.0,linear,linear,-0.05,0.0,0.05,0.0,1:x,1;0,1</property><property name="orientation_mode" type="list" value="Camera-aligned with stretching factor"/><property name="velocity_stretch_cap" type="float" value="3.0"/></modifier><modifier fragment="vs_cell_texture"><property name="atlas_number_of_cells_x" type="int" value="8"/><property name="atlas_number_of_cells_y" type="int" value="8"/><property name="atlas_cell" type="int" value="51"/><property name="number_of_cells_x" type="int" value="2"/><property name="number_of_cells_y" type="int" value="2"/><property name="randomize_cell" type="bool" value="true"/></modifier><modifier fragment="ps_lighting"><property name="additive_blend_mul" type="float" value="1.0"/></modifier></quality></behaviour_chain>""";

        static List<EmitterSettings> Read(string emitters, string chains, string stacks = "") =>
            new VfxResolver(Pack(("fx.xml", Xml(emitters: emitters, chains: chains, stacks: stacks)))).Resolve("fx", new Random(1)).Emitters.Select(EmitterSettings.From).ToList();

        [Test]
        public void SmokeChain_IsAPremultipliedFlipbook_InLocalSpace()
        {
            var smoke = Read(Emitter("smoke", """<property name="ParticleSimulationSpace" quality="0" value="LOCAL_SPACE_POSITION_ROTATION"/>"""), SmokeChain).Single();
            Assert.Multiple(() =>
            {
                Assert.That(smoke.Draws, Is.True);
                Assert.That(smoke.Space, Is.EqualTo(SimulationSpace.LocalPositionRotation));
                Assert.That(smoke.InfiniteLife, Is.True);
                Assert.That(smoke.Texture, Is.EqualTo(@"vfx\textures\smoke_dense_loop_diffuse.dds"));
                Assert.That((smoke.Premultiplied, smoke.AdditiveMul), Is.EqualTo((true, 0f)));
                Assert.That((smoke.Animated, smoke.CellsX, smoke.CellsY, smoke.FlipbookLoop, smoke.FixedSpeed), Is.EqualTo((true, 8, 8, true, false)));
                Assert.That(smoke.Loops, Is.EqualTo(new Vector2(0.5f, 0.5f)));
                Assert.That(smoke.Pivot, Is.EqualTo(new Vector2(0.5f, 0.25f)));
                Assert.That(smoke.Orientation, Is.EqualTo(QuadOrientation.CameraFacing));
            });
        }

        [Test]
        public void SparksChain_IsAStretchedAdditiveAtlasCell()
        {
            var sparks = Read(Emitter("sparks"), SparksChain).Single();
            Assert.Multiple(() =>
            {
                Assert.That(sparks.Space, Is.EqualTo(SimulationSpace.World), "no ParticleSimulationSpace is world space");
                Assert.That((sparks.Orientation, sparks.StretchCap), Is.EqualTo((QuadOrientation.VelocityAligned, 3f)));
                Assert.That(sparks.Stretch, Is.Not.Null);
                Assert.That((sparks.AtlasX, sparks.AtlasY, sparks.AtlasCell, sparks.CellsX, sparks.CellsY, sparks.RandomCell, sparks.Animated), Is.EqualTo((8, 8, 51, 2, 2, true, false)));
                Assert.That((sparks.Premultiplied, sparks.AdditiveMul), Is.EqualTo((false, 1f)));
            });
        }

        [Test]
        public void DistortionStack_IsNotDrawn_AndSaysWhy()
        {
            var ripple = Read(Emitter("ripple"), SmokeChain.Replace("\"smoke\"", "\"ripple\""),
                """<effect_render_stack id="ripple"><property name="scene_view_type" quality="0" value="DISTORTION"/></effect_render_stack>""").Single();
            Assert.That(ripple.Draws, Is.False);
            Assert.That(ripple.Skipped, Has.One.Matches<string>(x => x.Contains("DISTORTION")));
        }

        [TestCase("Vertically-oriented", QuadOrientation.Vertical)]
        [TestCase("Particle only rotating around local Y (horizontal)", QuadOrientation.LocalHorizontal)]
        [TestCase("Particle only rotating around local Y (vertical)", QuadOrientation.LocalVertical)]
        public void RareOrientationModes_AreRead(string mode, QuadOrientation expected)
        {
            var sparks = Read(Emitter("sparks"), SparksChain.Replace("Camera-aligned with stretching factor", mode)).Single();
            Assert.That((sparks.Orientation, sparks.Skipped.Any(x => x.Contains("orientation_mode"))), Is.EqualTo((expected, false)));
        }

        [Test]
        public void UnknownOrientationMode_DrawsCameraFacing_AndSaysSo()
        {
            var sparks = Read(Emitter("sparks"), SparksChain.Replace("Camera-aligned with stretching factor", "Made up")).Single();
            Assert.That((sparks.Draws, sparks.Orientation), Is.EqualTo((true, QuadOrientation.CameraFacing)));
            Assert.That(sparks.Skipped, Has.One.Matches<string>(x => x.Contains("Made up")));
        }

        [Test]
        public void FlipUv_IsRead()
        {
            var flip = """<modifier fragment="vs_flip_uv"><property name="flip_u" type="float" value="0.5"/><property name="flip_v" type="float" value="1.0"/><property name="uv_orientation" type="list" value="90_degrees_cw"/></modifier>""";
            var sparks = Read(Emitter("sparks"), SparksChain.Replace("</quality>", flip + "</quality>")).Single();
            Assert.That((sparks.FlipU, sparks.FlipV, sparks.UvTurn), Is.EqualTo((0.5f, 1f, -1)));
        }

        [Test]
        public void RenderSizeLimiter_IsRead()
        {
            var limiter = """<modifier fragment="vs_rendersize_limiter"><property name="pixelsize_limit" type="float" value="3.0"/></modifier>""";
            Assert.That(Read(Emitter("sparks"), SparksChain.Replace("</quality>", limiter + "</quality>")).Single().MinPixels, Is.EqualTo(3f));
        }

        [Test]
        public void Emissive_IsRead()
        {
            var emissive = """<modifier fragment="ps_emissive"><property name="emissive_mul" type="float" value="21.0"/></modifier>""";
            Assert.That(Read(Emitter("sparks"), SparksChain.Replace("</quality>", emissive + "</quality>")).Single().EmissiveMul, Is.EqualTo(21f));
            Assert.That(Read(Emitter("sparks"), SparksChain).Single().EmissiveMul, Is.EqualTo(1f), "no ps_emissive: the colour as it is");
        }

        [Test]
        public void CullDistances_AreRead()
        {
            var e = Read(Emitter("sparks", """<property name="CullDistances" quality="0" x="0.00" y="0.00" z="700.00" w="800.00"/><property name="ParticleCullDistances" quality="0" x="2.00" y="5.00" z="400.00" w="450.00"/>"""), SparksChain).Single();
            Assert.That((e.EmitterCull, e.ParticleCull), Is.EqualTo((new Vector4(0, 0, 700, 800), new Vector4(2, 5, 400, 450))));
        }

        [Test]
        public void SkipParticlePositionUpdate_IsRead()
        {
            var head = Read(Emitter("sparks", """<property name="SkipParticlePositionUpdate" quality="0" value="true"/>"""), SparksChain).Single();
            Assert.That((head.SkipPositionUpdate, Read(Emitter("sparks"), SparksChain).Single().SkipPositionUpdate), Is.EqualTo((true, false)));
        }

        [Test]
        public void SpawnSpacing_IsRead()
        {
            var trail = Read(Emitter("sparks", """<property name="SpawnSpacing" quality="0" value="0.50"/>"""), SparksChain).Single();
            Assert.That((trail.SpawnSpacing, Read(Emitter("sparks"), SparksChain).Single().SpawnSpacing), Is.EqualTo((0.5f, 0f)));
        }

        [Test]
        public void TrailMode_IsReadFromTheRenderStack()
        {
            var trail = Read(Emitter("sparks"), SparksChain, """<effect_render_stack id="sparks"><property name="enable_trail_mode" quality="0" value="true"/></effect_render_stack>""").Single();
            Assert.That((trail.Trail, Read(Emitter("sparks"), SparksChain).Single().Trail), Is.EqualTo((true, false)));
        }

        [Test]
        public void EmitterWithoutAChain_IsNotDrawn() => Assert.That(Read(Emitter("lonely"), "").Single().Draws, Is.False);

        [Test]
        public void UnknownFragment_IsNamed_AndTheRestStillDraws()
        {
            var chain = SmokeChain.Replace("<quality value=\"0\">", "<quality value=\"0\"><modifier fragment=\"ps_made_up\"/>");
            var smoke = Read(Emitter("smoke"), chain).Single();
            Assert.That(smoke.Draws, Is.True);
            Assert.That(smoke.Skipped, Has.One.Matches<string>(x => x.Contains("ps_made_up")));
        }

        // 8,908 of 11,722 vanilla continuous_mesh emitters omit SpawnAngle; where present it is an azimuth range
        // (0..360 in 802, -180..180 in 366). Read as 0,0, every emitter without one sprayed into +X alone.
        [Test]
        public void AbsentSpawnAngle_IsEveryAzimuth_AndAPresentOneIsRead()
        {
            Assert.That(Read(Emitter("smoke"), SmokeChain).Single().SpawnAngle, Is.EqualTo(new Vector2(0, 360)));
            var narrow = Read(Emitter("smoke", """<property name="SpawnAngle" quality="0" x="-90.00" y="90.00"/>"""), SmokeChain).Single();
            Assert.That(narrow.SpawnAngle, Is.EqualTo(new Vector2(-90, 90)));
        }

        // SmokeChain with its ps_texture modifier swapped for another.
        static string SmokeWithout(string replacement)
        {
            var start = SmokeChain.IndexOf("<modifier fragment=\"ps_texture\">");
            var end = SmokeChain.IndexOf("</modifier>", start) + "</modifier>".Length;
            return SmokeChain[..start] + replacement + SmokeChain[end..];
        }

        // 231 vanilla chains (wh3_blood_effects among them) draw through a material and carry no ps_texture.
        // Drawn anyway, each particle was a flat quad in the colour curve's tint.
        [Test]
        public void CustomShaderChain_IsNotDrawn_AndNamesItsMaterial()
        {
            var blood = Read(Emitter("smoke"), SmokeWithout("""<modifier fragment="ps_custom_shader"><property name="material" type="file" path="vfx\materials\wh3_blood_spurt_01.xml.material"/></modifier>""")).Single();
            Assert.That(blood.Draws, Is.False);
            Assert.That(blood.Skipped, Has.One.Matches<string>(x => x.Contains("ps_custom_shader") && x.Contains("wh3_blood_spurt_01")));
        }

        // The five vanilla drawn chains with no ps_texture all belong to emitters named "sound": they carry a sound and
        // show nothing, where a white quad each was drawn.
        [Test]
        public void ChainWithoutATexture_IsNotDrawn_AndSaysSo()
        {
            var plain = Read(Emitter("smoke"), SmokeWithout("")).Single();
            Assert.That(plain.Draws, Is.False);
            Assert.That(plain.Skipped, Has.One.Matches<string>(x => x.Contains("ps_texture")));
        }

        // A dynamic light has no ps_texture either, and is not drawn at all: it must not also claim to draw white.
        [Test]
        public void UndrawnChainWithoutATexture_DoesNotClaimToDrawWhite()
        {
            var light = Read(Emitter("smoke"), SmokeWithout(""),
                """<effect_render_stack id="smoke"><property name="scene_view_type" quality="0" value="DEFERRED_DYNAMIC_LIGHT"/></effect_render_stack>""").Single();
            Assert.That(light.Draws, Is.False);
            Assert.That(light.Skipped, Has.None.Matches<string>(x => x.Contains("drawn white")));
        }
    }
}
