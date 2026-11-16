using System.IO;
using Editors.AnimationMeta.SuperView;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Shared.Core.Commands;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimationMeta
{
    // Scratch, not committed: SuperView's skeleton and grid on three backgrounds, to PNGs.
    public class SkeletonGuideScratch
    {
        [Test, Explicit]
        public void Render()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            var vmd = runner.PackFileService.FindFile(@"variantmeshes\variantmeshdefinitions\skv_throt.variantmeshdefinition")!;
            runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var settings = runner.ServiceProvider.GetRequiredService<ApplicationSettingsService>();
            var outDir = Environment.GetEnvironmentVariable("SCRATCH_OUT") ?? Path.GetTempPath();

            var device = runner.ServiceProvider.GetRequiredService<GameWorld.Core.WpfWindow.IWpfGame>().GraphicsDevice;
            runner.GetRequiredServiceInCurrentEditorScope<GameWorld.Core.Components.Rendering.ArcBallCamera>().ViewMatrixOverride =
                Matrix.CreateLookAt(new Vector3(1.5f, 1.6f, 3.2f), new Vector3(0, 0.9f, 0), Vector3.Up);
            var components = runner.GetRequiredServiceInCurrentEditorScope<IEnumerable<IGameComponent>>().ToList();
            foreach (var background in new[] { BackgroundColour.Black, BackgroundColour.DarkGrey, BackgroundColour.White })
            {
                settings.CurrentSettings.RenderEngineBackgroundColour = background;
                using var target = new Microsoft.Xna.Framework.Graphics.RenderTarget2D(device, 640, 480, false,
                    Microsoft.Xna.Framework.Graphics.SurfaceFormat.Color, Microsoft.Xna.Framework.Graphics.DepthFormat.Depth24);
                var time = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1 / 60.0));
                device.SetRenderTarget(target);
                foreach (var component in components.OfType<IUpdateable>().OrderBy(x => x.UpdateOrder))
                    component.Update(time);
                foreach (var component in components.OfType<IDrawable>().OrderBy(x => x.DrawOrder))
                    component.Draw(time);
                device.SetRenderTarget(null);
                using var file = File.Create(Path.Combine(outDir, $"skeleton_{background}.png"));
                target.SaveAsPng(file, target.Width, target.Height);
            }
        }
    }
}
