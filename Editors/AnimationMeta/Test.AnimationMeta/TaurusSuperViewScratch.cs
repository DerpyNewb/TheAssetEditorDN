using System.IO;
using Editors.AnimationMeta.SuperView;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Vfx;
using GameWorld.Core.WpfWindow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.Commands;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // Scratch, not committed: the vanilla Bale Taurus in SuperView, its light particles counted and drawn.
    public class TaurusSuperViewScratch
    {
        [Test, Explicit, Apartment(ApartmentState.STA)]
        public void Scratch_SuperViewTaurus()
        {
            var data = Environment.GetEnvironmentVariable("WH3_DATA")!;
            var output = Environment.GetEnvironmentVariable("VFX_PREVIEW_OUT")!;
            var slotWanted = Environment.GetEnvironmentVariable("TAURUS_SLOT") ?? "STAND";
            Directory.CreateDirectory(output);
            var runner = new AssetEditorTestRunner();
            var caDb = Environment.GetEnvironmentVariable("CA_CACHE_DB");
            if (caDb != null)
            {
                // As the app runs: every game file in the one SQLite cache.
                var type = typeof(IPackFileContainer).Assembly.GetType("Shared.Core.PackFiles.Models.Containers.CachedPackFileContainer")!;
                var ca = (IPackFileContainer)Activator.CreateInstance(type, "All Game Packs - Warhammer III", caDb)!;
                type.GetProperty("IsCaPackFile")!.SetValue(ca, true);
                runner.PackFileService.AddContainer(ca);
            }
            else
            {
            runner.CreateCaContainer();
            var loader = runner.ServiceProvider.GetRequiredService<IPackFileContainerLoader>();
            foreach (var pack in Directory.GetFiles(data, "*.pack").Select(Path.GetFileName)
                .Where(x => x!.StartsWith("anim") || x.StartsWith("variants") || x.StartsWith("vfx") || x == "models2.pack" || x == "data.pack" || x.StartsWith("database")))
                runner.PackFileService.AddContainer(loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(data, pack!), true));
            }
            var vmds = runner.PackFileService.GetAllPackfileContainers().SelectMany(c => c.SearchFiles("taurus", [".variantmeshdefinition"])).Select(x => x.Path).Distinct().ToList();
            TestContext.Out.WriteLine("vmds: " + string.Join(", ", vmds));
            var vmdPath = vmds.FirstOrDefault(x => x.Contains("bale")) ?? vmds.First();
            var vmd = runner.PackFileService.FindFile(vmdPath)!;
            var superView = runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            var bins = selection.FragmentList.PossibleValues.Where(x => x.FullPath.Contains("taurus") || x.FullPath.Contains("ox1")).ToList();
            TestContext.Out.WriteLine("bins: " + string.Join(", ", bins.Select(x => x.FullPath)));
            selection.FragmentList.SelectedItem = bins.First(x => x.FullPath.EndsWith(Environment.GetEnvironmentVariable("TAURUS_BIN") ?? "ox1_dlc23_taurus_cages.bin"));
            TestContext.Out.WriteLine("bin: " + selection.FragmentList.SelectedItem.FullPath);
            TestContext.Out.WriteLine("slots: " + string.Join(", ", selection.FragmentSlotList.PossibleValues.Select(x => x.SlotName).Distinct().Take(200)));
            selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName.Contains(slotWanted));
            TestContext.Out.WriteLine("slot: " + selection.FragmentSlotList.SelectedItem.SlotName);

            var scene = runner.GetRequiredServiceInCurrentEditorScope<SceneManager>();
            var camera = runner.GetRequiredServiceInCurrentEditorScope<ArcBallCamera>();
            var components = runner.GetRequiredServiceInCurrentEditorScope<IEnumerable<IGameComponent>>().ToList();
            var device = runner.ServiceProvider.GetRequiredService<IWpfGame>().GraphicsDevice;
            using var target = new RenderTarget2D(device, 1024, 768, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents);
            var time = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1 / 60.0));
            for (var f = 0; f < 180; f++)
            {
                camera.ViewMatrixOverride = Matrix.CreateLookAt(new Vector3(7, 6, 7), new Vector3(0, 1, 0), Vector3.Up);
                device.SetRenderTarget(target);
                foreach (var component in components.OfType<IUpdateable>().OrderBy(x => x.UpdateOrder))
                    component.Update(time);
                foreach (var item in superView.SceneObjects)
                    item.Data.Update(time);
                foreach (var component in components.OfType<IDrawable>().OrderBy(x => x.DrawOrder))
                    component.Draw(time);
                device.SetRenderTarget(null);
            }
            var pixel = new Color[1];
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (var f = 0; f < 120; f++)
            {
                device.SetRenderTarget(target);
                foreach (var component in components.OfType<IUpdateable>().OrderBy(x => x.UpdateOrder))
                    component.Update(time);
                foreach (var item in superView.SceneObjects)
                    item.Data.Update(time);
                foreach (var component in components.OfType<IDrawable>().OrderBy(x => x.DrawOrder))
                    component.Draw(time);
                device.SetRenderTarget(null);
                target.GetData(0, new Rectangle(0, 0, 1, 1), pixel, 0, 1);
            }
            TestContext.Out.WriteLine($"frame {clock.Elapsed.TotalMilliseconds / 120:0.00} ms with the GPU waited on, at {target.Width}x{target.Height}");
            var hub = runner.GetRequiredServiceInCurrentEditorScope<Shared.Core.Events.IEventHub>();
            hub.Publish(new Editors.AnimationMeta.Presentation.MetaDataAttributeChangedEvent());
            var gate = Environment.GetEnvironmentVariable("PROFILE_GATE");
            var rebuilds = 10;
            if (gate != null)
            {
                File.WriteAllText(gate + ".pid", Environment.ProcessId.ToString());
                for (var w = 0; w < 600 && !File.Exists(gate + ".go"); w++)
                    Thread.Sleep(100);
                rebuilds = 100;
            }
            var rebuild = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < rebuilds; i++)
                hub.Publish(new Editors.AnimationMeta.Presentation.MetaDataAttributeChangedEvent());
            TestContext.Out.WriteLine($"rebuild {rebuild.Elapsed.TotalMilliseconds / rebuilds:0.0} ms per meta change");
            // A scrub: one effect's position moves a step before every rebuild.
            var scrubbed = (superView.MetaEditor.ParsedFile?.GetItemsOfType<Shared.GameFormats.AnimationMeta.Definitions.IEffectMeta>() ?? [])
                .Concat(superView.PersistentMetaEditor.ParsedFile?.GetItemsOfType<Shared.GameFormats.AnimationMeta.Definitions.IEffectMeta>() ?? []).First();
            rebuild.Restart();
            for (var i = 0; i < rebuilds; i++)
            {
                scrubbed.Position += new Vector3(0.001f, 0, 0);
                hub.Publish(new Editors.AnimationMeta.Presentation.MetaDataAttributeChangedEvent());
            }
            TestContext.Out.WriteLine($"scrub {rebuild.Elapsed.TotalMilliseconds / rebuilds:0.0} ms per step on {scrubbed.VfxName}");
            var nodes = Descendants(scene.RootNode).OfType<VfxEffectNode>().ToList();
            var lights = nodes.SelectMany(x => x.RenderItem.Lights()).ToList();
            TestContext.Out.WriteLine($"effect nodes {nodes.Count}: " + string.Join(", ", nodes.GroupBy(x => x.Name).Select(g => $"{g.Key} x{g.Count()}")));
            TestContext.Out.WriteLine($"lights {lights.Count}");
            var sim = (ParticleSimulator)typeof(ParticleRenderItem).GetField("_simulator", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(nodes[0].RenderItem)!;
            TestContext.Out.WriteLine($"first node {nodes[0].Name}: emitters {sim.AllEmitters.Count()}, light emitters {sim.AllEmitters.Count(e => e.Settings.Light)}, particles {sim.AllEmitters.Sum(e => e.Particles.Count)}, " +
                string.Join("; ", sim.AllEmitters.Select(e => $"{e.Settings.Name} light={e.Settings.Light} n={e.Particles.Count}")));
            foreach (var l in lights)
                TestContext.Out.WriteLine($"   at {l.Position.X:0.00},{l.Position.Y:0.00},{l.Position.Z:0.00} reach {l.Radius:0.00} colour {l.Colour.X:0.000},{l.Colour.Y:0.000},{l.Colour.Z:0.000}");
            using var stream = File.Create(Path.Combine(output, $"superview_{slotWanted}.png"));
            target.SaveAsPng(stream, target.Width, target.Height);
        }

        static IEnumerable<ISceneNode> Descendants(ISceneNode node) => node.Children.SelectMany(x => Descendants(x).Prepend(x));
    }
}
