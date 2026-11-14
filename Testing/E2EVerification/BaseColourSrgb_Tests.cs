using DirectXTexNet;
using Editors.AnimationMeta.SuperView;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Rendering.Materials.Capabilities.Utility;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.WpfWindow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.Commands;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.E2EVerification
{
    // A base colour with no sRGB flag (a legacy DXT5 header cannot carry one, BC3_UNORM does not) is read by the game as linear, so
    // it draws lighter there, pink or washed out on a dark red (Kharkadon, 2026-10-06). The 3D view decoded every base colour as
    // sRGB, so it drew the faulty file and the good one alike and the fault never showed before the game.
    internal class BaseColourSrgb_Tests
    {
        const string ThrotVmd = @"variantmeshes\variantmeshdefinitions\skv_throt.variantmeshdefinition";

        [Test]
        public void ABaseColourWithNoSrgbFlag_DrawsLighter_AsTheGameReadsIt()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(runner.PackFileService.FindFile(ThrotVmd)!, EditorEnums.SuperView_Editor);
            runner.PackFileService.AddFilesToPack(runner.CreateOutputPack(), [new NewPackFileEntry("test", PackFile.CreateFromBytes("legacy_base_colour.dds",
                TextureEditor_Tests.Dds(DXGI_FORMAT.BC3_UNORM, legacyHeader: true, 128, 48, 32, 255)))]);
            runner.PackFileService.AddFilesToPack(runner.CreateOutputPack(), [new NewPackFileEntry("test", PackFile.CreateFromBytes("srgb_base_colour.dds",
                TextureEditor_Tests.Dds(DXGI_FORMAT.BC3_UNORM_SRGB, legacyHeader: false, 128, 48, 32, 255)))]);

            var baseColours = Every(runner.GetRequiredServiceInCurrentEditorScope<SceneManager>().RootNode).OfType<Rmv2MeshNode>()
                .Select(x => x.Material.TryGetCapability<MetalRoughCapability>()?.BaseColour).OfType<TextureInput>().ToList();
            Assert.That(baseColours, Is.Not.Empty);

            Color[] With(string path)
            {
                foreach (var baseColour in baseColours)
                    (baseColour.TexturePath, baseColour.UseTexture) = (path, true);
                return RenderFrame(runner);
            }

            var srgb = With(@"test\srgb_base_colour.dds");
            var legacy = With(@"test\legacy_base_colour.dds");
            var drawn = srgb.Zip(RenderFrame(runner, hideModel: true)).Count(x => x.First != x.Second);
            var lighter = legacy.Zip(srgb).Count(x => x.First.R + x.First.G + x.First.B > x.Second.R + x.Second.G + x.Second.B + 24);

            Assert.That(drawn, Is.GreaterThan(1000), "the model is not in the frame");
            Assert.That(lighter, Is.GreaterThan(drawn / 2), $"{lighter} of {drawn} model pixels drew lighter");
        }

        static IEnumerable<ISceneNode> Every(ISceneNode node) => node.Children.SelectMany(x => Every(x).Prepend(x));

        static Color[] RenderFrame(AssetEditorTestRunner runner, bool hideModel = false)
        {
            var meshes = Every(runner.GetRequiredServiceInCurrentEditorScope<SceneManager>().RootNode).OfType<Rmv2MeshNode>().ToList();
            meshes.ForEach(x => x.IsVisible = !hideModel);
            var device = runner.ServiceProvider.GetRequiredService<IWpfGame>().GraphicsDevice;
            runner.GetRequiredServiceInCurrentEditorScope<ArcBallCamera>().ViewMatrixOverride = Matrix.CreateLookAt(new Vector3(0, 1.2f, 4), new Vector3(0, 1, 0), Vector3.Up);
            var components = runner.GetRequiredServiceInCurrentEditorScope<IEnumerable<IGameComponent>>().ToList();
            using var target = new RenderTarget2D(device, 256, 192, false, SurfaceFormat.Color, DepthFormat.Depth24);
            var time = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1 / 60.0));
            device.SetRenderTarget(target);
            foreach (var component in components.OfType<IUpdateable>().OrderBy(x => x.UpdateOrder))
                component.Update(time);
            foreach (var component in components.OfType<IDrawable>().OrderBy(x => x.DrawOrder))
                component.Draw(time);
            device.SetRenderTarget(null);
            var pixels = new Color[target.Width * target.Height];
            target.GetData(pixels);
            meshes.ForEach(x => x.IsVisible = true);
            return pixels;
        }
    }
}
