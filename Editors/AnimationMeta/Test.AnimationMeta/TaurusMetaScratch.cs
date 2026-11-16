using System.IO;
using System.Text;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // Scratch, not committed: every meta naming a Bale Taurus effect, and its EFFECT tags.
    public class TaurusMetaScratch
    {
        // FX_NAME built at scale 1 on an unmoving origin from the game cache at CA_CACHE_DB: what each emitter's particles measure.
        [Test, Explicit]
        public void Scratch_EffectSizes()
        {
            var runner = new AssetEditorTestRunner();
            var type = typeof(IPackFileContainer).Assembly.GetType("Shared.Core.PackFiles.Models.Containers.CachedPackFileContainer")!;
            var ca = (IPackFileContainer)Activator.CreateInstance(type, "ca", Environment.GetEnvironmentVariable("CA_CACHE_DB")!)!;
            type.GetProperty("IsCaPackFile")!.SetValue(ca, true);
            runner.PackFileService.AddContainer(ca);
            var resolved = new GameWorld.Core.Vfx.VfxResolver(runner.PackFileService).Resolve(Environment.GetEnvironmentVariable("FX_NAME")!, new Random(1));
            TestContext.Out.WriteLine($"found {resolved.Found}, emitters {resolved.Emitters.Count}: " + string.Join("; ", resolved.Problems));
            var simulator = new GameWorld.Core.Vfx.ParticleSimulator(resolved.Emitters.Select(GameWorld.Core.Vfx.EmitterSettings.From).ToList(), 1, _ => Microsoft.Xna.Framework.Matrix.Identity, float.PositiveInfinity, m => TestContext.Out.WriteLine(m));
            foreach (var time in new[] { 0.1f, 0.3f, 0.6f, 1.0f })
            {
                simulator.AdvanceTo(time);
                foreach (var e in simulator.AllEmitters)
                {
                    var states = GameWorld.Core.Vfx.ParticleSimulator.States(e).ToList();
                    if (states.Count == 0) { TestContext.Out.WriteLine($"t {time} {e.Settings.Name}: none"); continue; }
                    TestContext.Out.WriteLine($"t {time} {e.Settings.Name}: n {states.Count} size max {states.Max(x => MathF.Max(x.Size.X, x.Size.Y)):0.000} mean {states.Average(x => (x.Size.X + x.Size.Y) / 2):0.000} " +
                        $"reach {states.Max(x => x.Position.Length()):0.00} speed {states.Max(x => x.Velocity.Length()):0.00} stretch {states.Max(x => x.Stretch):0.00} world scale {new Microsoft.Xna.Framework.Vector3(e.World.M11, e.World.M12, e.World.M13).Length():0.00}");
                }
            }
        }

        record NoSkeleton : GameWorld.Core.SceneNodes.ISkeletonProvider
        {
            public GameWorld.Core.Animation.GameSkeleton Skeleton => null!;
        }

        // META_PATHS (';'-separated) read through the game cache at CA_CACHE_DB: every EFFECT tag with its fields.
        [Test, Explicit]
        public void Scratch_EffectTagsOf()
        {
            var runner = new AssetEditorTestRunner();
            var type = typeof(IPackFileContainer).Assembly.GetType("Shared.Core.PackFiles.Models.Containers.CachedPackFileContainer")!;
            var ca = (IPackFileContainer)Activator.CreateInstance(type, "ca", Environment.GetEnvironmentVariable("CA_CACHE_DB")!)!;
            var parser = new MetaDataFileParser(new MetaDataDatabase());
            foreach (var path in Environment.GetEnvironmentVariable("META_PATHS")!.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var file = ca.FindFile(path);
                if (file == null) { TestContext.Out.WriteLine($"missing {path}"); continue; }
                var parsed = parser.ParseFile(file.DataSource.ReadData());
                TestContext.Out.WriteLine(path);
                foreach (var e in parsed.Attributes.OfType<IEffectMeta>())
                {
                    var a = (ParsedMetadataAttribute)e;
                    var scale = e.GetType().GetProperty("Scale")?.GetValue(e);
                    TestContext.Out.WriteLine($"   {a.Name}_{a.Version} {e.VfxName} node {e.NodeIndex} pos {e.Position} orient {e.Orientation} scale {scale} tracking {e.Tracking} t {e.EffectStartTime}..{e.EffectEndTime}");
                }
            }
        }

        [Test, Explicit]
        public void Scratch_TaurusEffects()
        {
            var data = Environment.GetEnvironmentVariable("WH3_DATA")!;
            var runner = new AssetEditorTestRunner();
            var loader = (IPackFileContainerLoader)runner.ServiceProvider.GetService(typeof(IPackFileContainerLoader))!;
            var parser = new MetaDataFileParser(new MetaDataDatabase());
            var needle = Encoding.ASCII.GetBytes("bale_taurus");
            foreach (var packName in new[] { "anim.pack", "anim2.pack", "anim_3.pack" })
            {
                var container = loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(data, packName), true);
                foreach (var (path, file) in container.GetAllFiles().Where(x => x.Key.EndsWith(".meta")))
                {
                    var bytes = file.DataSource.ReadData();
                    if (bytes.AsSpan().IndexOf(needle) < 0)
                        continue;
                    var parsed = parser.ParseFile(bytes);
                    TestContext.Out.WriteLine($"{packName} {path}");
                    foreach (var e in parsed.Attributes.OfType<IEffectMeta>())
                    {
                        var a = (ParsedMetadataAttribute)e;
                        var scale = e.GetType().GetProperty("Scale")?.GetValue(e);
                        TestContext.Out.WriteLine($"   {a.Name}_{a.Version} {e.VfxName} node {e.NodeIndex} pos {e.Position} scale {scale} tracking {e.Tracking} t {e.EffectStartTime}..{e.EffectEndTime}");
                    }
                }
            }
        }
    }
}
