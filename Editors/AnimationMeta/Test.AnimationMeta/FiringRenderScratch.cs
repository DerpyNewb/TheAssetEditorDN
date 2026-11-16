using System.IO;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using Shared.Core.Commands;
using GameWorld.Core.WpfWindow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.PackFiles.Models;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // Scratch, not committed: SuperView on a vanilla variant mesh, the player held at each of RENDER_TIMES, one frame each.
    //   CA_CACHE_DB  the game cache   VMD_PATH (or VMD_FIND, a substring to list)   SV_BIN / SV_SLOT  substrings
    //   RENDER_TIMES  comma list of seconds   RENDER_OUT  output folder   EYE / AT  "x,y,z"
    public class FiringRenderScratch
    {
        static Vector3 V(string s) { var p = s.Split(',').Select(float.Parse).ToArray(); return new Vector3(p[0], p[1], p[2]); }

        [Test, Explicit, Apartment(ApartmentState.STA)]
        public void Scratch_RenderFiring()
        {
            var output = Environment.GetEnvironmentVariable("RENDER_OUT")!;
            Directory.CreateDirectory(output);
            var runner = new AssetEditorTestRunner();
            var type = typeof(IPackFileContainer).Assembly.GetType("Shared.Core.PackFiles.Models.Containers.CachedPackFileContainer")!;
            var ca = (IPackFileContainer)Activator.CreateInstance(type, "All Game Packs - Warhammer III", Environment.GetEnvironmentVariable("CA_CACHE_DB")!)!;
            type.GetProperty("IsCaPackFile")!.SetValue(ca, true);
            runner.PackFileService.AddContainer(ca);
            runner.CreateOutputPack();

            if (Environment.GetEnvironmentVariable("VMD_FIND") is { Length: > 0 } find)
            {
                foreach (var (name, _) in runner.PackFileService.FindAllWithExtention(".variantmeshdefinition", ca).Where(x => x.FileName.Contains(find)))
                    TestContext.Out.WriteLine("VMD " + runner.PackFileService.GetFullPath(runner.PackFileService.FindAllWithExtention(".variantmeshdefinition", ca).First(x => x.FileName == name).Pack, ca));
                return;
            }

            T Current<T>() where T : notnull => runner.ScopeRepository.GetRequiredService<T>(runner.ScopeRepository.GetEditorHandles().Last());
            var file = runner.PackFileService.FindFile(Environment.GetEnvironmentVariable("VMD_PATH")!)!;
            var superView = (Editors.AnimationMeta.SuperView.SuperViewViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute(file, EditorEnums.SuperView_Editor)!;
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            var bin = Environment.GetEnvironmentVariable("SV_BIN")!;
            TestContext.Out.WriteLine("bins: " + string.Join(", ", selection.FragmentList.PossibleValues.Select(x => x.FullPath).Where(x => x.Contains(bin))));
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath.Contains(bin));
            TestContext.Out.WriteLine("slots: " + string.Join(", ", selection.FragmentSlotList.PossibleValues.Select(x => x.SlotName).Distinct()));
            selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName.Contains(Environment.GetEnvironmentVariable("SV_SLOT")!));
            TestContext.Out.WriteLine("slot: " + selection.FragmentSlotList.SelectedItem.SlotName + " meta: " + superView.SceneObjects[0].Data.MetaData?.Name);
            foreach (var mt in superView.MetaEditor.ParsedFile?.Attributes ?? [])
                if (mt is Shared.GameFormats.AnimationMeta.Parsing.DecodedMetaEntryBase d)
                    TestContext.Out.WriteLine($"TAG {mt.Name} {d.StartTime:0.000}-{d.EndTime:0.000}");

            // ADD_SPLASH: a cone and a corridor splash attack from 0.2 to 0.6 s, the cone selected, so a frame inside the window
            // shows both yellow and one outside shows the guide colour and the selection's red.
            if (Environment.GetEnvironmentVariable("ADD_SPLASH") is { Length: > 0 })
            {
                var meta = superView.MetaEditor;
                var cone = new Shared.GameFormats.AnimationMeta.Definitions.SplashAttack_v10 { Name = "SPLASH_ATTACK", Version = 10, StartTime = 0.2f, EndTime = 0.6f, StartPosition = new Vector3(0, 1, 0.5f), EndPosition = new Vector3(0, 1, 2.5f), AoeShape = 0, AngleForCone = 60 };
                var corridor = new Shared.GameFormats.AnimationMeta.Definitions.SplashAttack_v10 { Name = "SPLASH_ATTACK", Version = 10, StartTime = 0.2f, EndTime = 0.6f, StartPosition = new Vector3(0, 0.5f, -0.5f), EndPosition = new Vector3(0, 0.5f, -3f), AoeShape = 1, WidthForCorridor = 0.8f };
                meta.ParsedFile!.Attributes.Add(cone);
                meta.ParsedFile!.Attributes.Add(corridor);
                meta.UpdateView();
                meta.SelectedTag = meta.Tags.First(x => x._input == cone);
            }

            var player = superView.SceneObjects[0].Data.Player;
            player.Pause();
            var camera = Current<ArcBallCamera>();
            var components = Current<IEnumerable<IGameComponent>>().ToList();
            var device = runner.ServiceProvider.GetRequiredService<IWpfGame>().GraphicsDevice;
            using var target = new RenderTarget2D(device, 1024, 768, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents);
            var time = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1 / 60.0));
            var (eye, at) = (V(Environment.GetEnvironmentVariable("EYE") ?? "1.5,1.2,3"), V(Environment.GetEnvironmentVariable("AT") ?? "0,0.8,0"));
            foreach (var seconds in Environment.GetEnvironmentVariable("RENDER_TIMES")!.Split(',').Select(float.Parse))
            {
                for (var f = 0; f < 20; f++)
                {
                    var toEye = eye - at;
                    camera.ViewMatrixOverride = null;
                    (camera.LookAt, camera.Zoom) = (at, toEye.Length());
                    toEye.Normalize();
                    (camera.Yaw, camera.Pitch) = (MathF.Atan2(toEye.X, toEye.Z), -MathF.Asin(toEye.Y));
                    device.SetRenderTarget(target);
                    player.SetTime((long)(seconds * 1_000_000));
                    foreach (var component in components.OfType<IUpdateable>().OrderBy(x => x.UpdateOrder))
                        component.Update(time);
                    player.SetTime((long)(seconds * 1_000_000));
                    player.Refresh();
                    foreach (var item in superView.SceneObjects)
                        item.Data.Update(time);
                    foreach (var component in components.OfType<IDrawable>().OrderBy(x => x.DrawOrder))
                        component.Draw(time);
                    device.SetRenderTarget(null);
                }
                TestContext.Out.WriteLine($"RENDERED t={seconds} player={player.GetTimeUs() / 1e6f}");
                using var stream = File.Create(Path.Combine(output, $"firing_{seconds:0.00}.png"));
                target.SaveAsPng(stream, target.Width, target.Height);
            }
        }
    }
}
