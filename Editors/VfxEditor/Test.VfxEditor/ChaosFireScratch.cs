using System.Globalization;
using System.IO;
using Editors.VfxEditor.Preview;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.WpfWindow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.Commands;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;

namespace Test.VfxEditor
{
    // Scratch, not committed: one effect drawn the app's way, with the glow pass on and off, and the brightest pixels counted.
    public class ChaosFireScratch
    {
        [Test, Explicit, Apartment(ApartmentState.STA)]
        public void Scratch_GlowOnOff()
        {
            var data = Environment.GetEnvironmentVariable("WH3_DATA")!;
            var name = Environment.GetEnvironmentVariable("VFX_PREVIEW") ?? "wh_main_creature_chaos_fire";
            var output = Environment.GetEnvironmentVariable("VFX_PREVIEW_OUT")!;
            var distance = float.Parse(Environment.GetEnvironmentVariable("VFX_DISTANCE") ?? "3", CultureInfo.InvariantCulture);
            Directory.CreateDirectory(output);
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var loader = runner.ServiceProvider.GetRequiredService<IPackFileContainerLoader>();
            foreach (var pack in new[] { "vfx_desc.pack", "vfx.pack", "vfx_models.pack", "models2.pack" })
                runner.PackFileService.AddContainer(loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(data, pack), true));
            runner.CommandFactory.Create<OpenEditorCommand>().Execute($"vfx\\{name}.xml", EditorEnums.Vfx_Editor);
            var preview = runner.GetRequiredServiceInCurrentEditorScope<VfxPreviewComponent>();
            var camera = runner.GetRequiredServiceInCurrentEditorScope<ArcBallCamera>();
            var engine = runner.GetRequiredServiceInCurrentEditorScope<RenderEngineComponent>();
            var components = runner.GetRequiredServiceInCurrentEditorScope<IEnumerable<IGameComponent>>().ToList();
            var device = runner.ServiceProvider.GetRequiredService<IWpfGame>().GraphicsDevice;
            preview.IsPlaying = false;
            using var target = new RenderTarget2D(device, 768, 512, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents);
            var glowField = typeof(RenderEngineComponent).GetField("_drawGlow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            foreach (var line in preview.Lines)
                TestContext.Out.WriteLine("  " + line);
            foreach (var time in new[] { 0.5f, 1f, 2f })
            {
                preview.SetTime(time);
                foreach (var glow in new[] { true, false })
                {
                    glowField.SetValue(engine, glow);
                    camera.ViewMatrixOverride = Matrix.CreateLookAt(new Vector3(0, 1, distance), new Vector3(0, 0.3f, 0), Vector3.Up);
                    device.SetRenderTarget(target);
                    var t = new GameTime(TimeSpan.Zero, TimeSpan.Zero);
                    foreach (var c in components.OfType<IUpdateable>().OrderBy(x => x.UpdateOrder))
                        c.Update(t);
                    foreach (var c in components.OfType<IDrawable>().OrderBy(x => x.DrawOrder))
                        c.Draw(t);
                    device.SetRenderTarget(null);
                    var pixels = new Color[target.Width * target.Height];
                    target.GetData(pixels);
                    var white = pixels.Count(p => p.R > 245 && p.G > 245 && p.B > 235);
                    var lit = pixels.Count(p => p.R > 120);
                    TestContext.Out.WriteLine($"t {time} glow {glow}: near-white {white}, red over 120 {lit}, max {pixels.Max(p => p.R + p.G + p.B)}");
                    using var stream = File.Create(Path.Combine(output, $"{name}_{(glow ? "glow" : "noglow")}_{time.ToString("0.00", CultureInfo.InvariantCulture)}s.png"));
                    target.SaveAsPng(stream, target.Width, target.Height);
                }
            }
        }
    }
}
