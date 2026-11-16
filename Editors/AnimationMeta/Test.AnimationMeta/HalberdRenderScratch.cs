using System.IO;
using GameWorld.Core.Components;
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

namespace Test.AnimationMeta
{
    // Scratch, not committed: a project's variant mesh opened in an editor as the app does, one frame saved per camera.
    //   CA_CACHE_DB  the game cache   PROJECT_DIR  the folder project   VMD_PATH  the file   RENDER_OUT  output folder
    //   EDITOR       Kitbash_Editor (default) or SuperView_Editor       RENDER_TAG  file name prefix
    public class HalberdRenderScratch
    {
        class NothingItem : IRenderItem
        {
            public void Draw(GraphicsDevice device, GameWorld.Core.Rendering.CommonShaderParameters parameters, RenderingTechnique renderingTechnique) { }
        }

        [Test, Explicit, Apartment(ApartmentState.STA)]
        public void Scratch_RenderProjectMesh()
        {
            var output = Environment.GetEnvironmentVariable("RENDER_OUT")!;
            var tag = Environment.GetEnvironmentVariable("RENDER_TAG") ?? "head";
            Directory.CreateDirectory(output);
            var runner = new AssetEditorTestRunner();
            var type = typeof(IPackFileContainer).Assembly.GetType("Shared.Core.PackFiles.Models.Containers.CachedPackFileContainer")!;
            var ca = (IPackFileContainer)Activator.CreateInstance(type, "All Game Packs - Warhammer III", Environment.GetEnvironmentVariable("CA_CACHE_DB")!)!;
            type.GetProperty("IsCaPackFile")!.SetValue(ca, true);
            runner.PackFileService.AddContainer(ca);
            var loader = runner.ServiceProvider.GetRequiredService<IPackFileContainerLoader>();
            var project = loader.CreateFromSystemFolder(Environment.GetEnvironmentVariable("PROJECT_DIR")!);
            runner.PackFileService.AddContainer(project, true);

            var editor = Enum.Parse<EditorEnums>(Environment.GetEnvironmentVariable("EDITOR") ?? "Kitbash_Editor");
            // PRE_VMD: a file opened and drawn first, in its own editor, as the app's session opened others before this one.
            if (Environment.GetEnvironmentVariable("PRE_VMD") is { Length: > 0 } preVmd)
            {
                runner.CommandFactory.Create<OpenEditorCommand>().Execute(runner.PackFileService.FindFile(preVmd)!, editor);
                var preComponents = runner.GetRequiredServiceInCurrentEditorScope<IEnumerable<IGameComponent>>().ToList();
                var preDevice = runner.ServiceProvider.GetRequiredService<IWpfGame>().GraphicsDevice;
                using var preTarget = new RenderTarget2D(preDevice, 1024, 768, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents);
                var preTime = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1 / 60.0));
                for (var f = 0; f < 60; f++)
                {
                    preDevice.SetRenderTarget(preTarget);
                    foreach (var component in preComponents.OfType<IUpdateable>().OrderBy(x => x.UpdateOrder))
                        component.Update(preTime);
                    foreach (var component in preComponents.OfType<IDrawable>().OrderBy(x => x.DrawOrder))
                        component.Draw(preTime);
                    preDevice.SetRenderTarget(null);
                }
                using var preStream = File.Create(Path.Combine(output, "pre.png"));
                preTarget.SaveAsPng(preStream, preTarget.Width, preTarget.Height);
            }
            T Current<T>() where T : notnull => runner.ScopeRepository.GetRequiredService<T>(runner.ScopeRepository.GetEditorHandles().Last());
            var file = runner.PackFileService.FindFile(Environment.GetEnvironmentVariable("VMD_PATH")!)!;
            var opened = runner.CommandFactory.Create<OpenEditorCommand>().Execute(file, editor);
            if (opened is Editors.AnimationMeta.SuperView.SuperViewViewModel superView)
            {
                // SV_BIN and SV_SLOT: substrings of the bin and the slot to play; the lists are printed to pick from.
                var selection = superView.SceneObjects[0].FragAndSlotSelection;
                TestContext.Out.WriteLine("bins: " + string.Join(", ", selection.FragmentList.PossibleValues.Select(x => x.FullPath).Where(x => x.Contains("derpy") || x.Contains("chd"))));
                selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath.Contains(Environment.GetEnvironmentVariable("SV_BIN")!));
                TestContext.Out.WriteLine("bin: " + selection.FragmentList.SelectedItem.FullPath);
                TestContext.Out.WriteLine("slots: " + string.Join(", ", selection.FragmentSlotList.PossibleValues.Select(x => x.SlotName).Distinct()));
                selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName.Contains(Environment.GetEnvironmentVariable("SV_SLOT")!));
                TestContext.Out.WriteLine("slot: " + selection.FragmentSlotList.SelectedItem.SlotName);
                TestContext.Out.WriteLine("WEAPONBOOLS " + string.Join(", ", selection.FragmentSlotList.PossibleValues.Select(x => $"{x.SlotName}={Convert.ToString(x.WeaponBools, 2).PadLeft(6, '0')}").Distinct()));
                superView.ShowVfx = Environment.GetEnvironmentVariable("SV_NOVFX") != "1";
                var sk = superView.SceneObjects[0].Data.Skeleton;
                TestContext.Out.WriteLine("BONES " + string.Join(", ", Enumerable.Range(0, Math.Min(sk?.BoneCount ?? 0, 12)).Select(i => $"{i}={sk!.BoneNames[i]}")) + " | " + string.Join(", ", Enumerable.Range(0, sk?.BoneCount ?? 0).Where(i => sk!.BoneNames[i].StartsWith("be_prop")).Select(i => $"{i}={sk!.BoneNames[i]}")));
                TestContext.Out.WriteLine("persist file: " + superView.SceneObjects[0].Data.PersistMetaData?.Name + " meta file: " + superView.SceneObjects[0].Data.MetaData?.Name);
                foreach (var (label, parsed) in new[] { ("persist", superView.PersistentMetaEditor.ParsedFile), ("meta", superView.MetaEditor.ParsedFile) })
                    foreach (var mt in parsed?.Attributes ?? [])
                        TestContext.Out.WriteLine($"TAG {label} {mt.GetType().Name}: " + string.Join(" | ", mt.GetType().GetProperties()
                            .Where(p => p.PropertyType == typeof(string) || p.PropertyType == typeof(float) || p.PropertyType == typeof(int) || p.PropertyType == typeof(Vector3) || p.PropertyType == typeof(Vector4)).Select(p => $"{p.Name}={p.GetValue(mt)}")));
            }

            // RENDER_VARIANT: which model of each slot that has several is shown, 0 (the loader's own choice) by default.
            if (int.TryParse(Environment.GetEnvironmentVariable("RENDER_VARIANT"), out var variant))
            {
                var root = Current<GameWorld.Core.Components.SceneManager>().RootNode;
                static IEnumerable<GameWorld.Core.SceneNodes.ISceneNode> All(GameWorld.Core.SceneNodes.ISceneNode n) => n.Children.SelectMany(x => All(x).Prepend(x));
                foreach (var slot in All(root).Where(x => x.Name.StartsWith("weapon_1")))
                    for (var i = 0; i < slot.Children.Count; i++)
                        slot.Children[i].IsVisible = i == variant;
            }
            {
                var root = Current<GameWorld.Core.Components.SceneManager>().RootNode;
                static IEnumerable<GameWorld.Core.SceneNodes.ISceneNode> Every(GameWorld.Core.SceneNodes.ISceneNode n) => n.Children.SelectMany(x => Every(x).Prepend(x));
                // RENDER_PICK: "slot=index;slot=index", which child of each named slot is shown. The tree is printed to pick from.
                var picks = (Environment.GetEnvironmentVariable("RENDER_PICK") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=')).ToDictionary(p => p[0], p => int.Parse(p[1]));
                foreach (var node in Every(root).Where(n => n.Children.Count > 1))
                {
                    TestContext.Out.WriteLine($"SLOT {node.Name}: " + string.Join(" | ", node.Children.Select((c, i) => $"{i}={c.Name}{(c.IsVisible ? "*" : "")}")));
                    if (picks.TryGetValue(node.Name, out var pick))
                        for (var i = 0; i < node.Children.Count; i++)
                            node.Children[i].IsVisible = i == pick;
                }
                foreach (var mesh in Every(root).OfType<GameWorld.Core.SceneNodes.Rmv2MeshNode>())
                {
                    var material = mesh.Material;
                    var tint = material.TryGetCapability<GameWorld.Core.Rendering.Materials.Capabilities.TintCapability>();
                    var mr = material.TryGetCapability<GameWorld.Core.Rendering.Materials.Capabilities.MetalRoughCapability>();
                    TestContext.Out.WriteLine($"MESH {mesh.Name} {material.Type} tint={(tint == null ? "none" : $"{tint.ApplyCapability}/{tint.UseFactionColours}")} mask={mr?.Mask.UseTexture}:{mr?.Mask.TexturePath}");
                }
            }
            // FACTION=1: garish faction colours, set where the render settings window sets them.
            if (Environment.GetEnvironmentVariable("FACTION") == "1")
            {
                var store = Current<SceneRenderParametersStore>();
                (store.FactionColour0, store.FactionColour1, store.FactionColour2) = (new Vector3(0, 1, 0), new Vector3(1, 0, 1), new Vector3(0, 1, 1));
            }
            // LIGHT_X, LIGHT_Y, LIGHT_I: the render settings' light rotations (degrees) and intensity.
            {
                var store = Current<SceneRenderParametersStore>();
                if (float.TryParse(Environment.GetEnvironmentVariable("LIGHT_X"), out var lx)) store.DirLightRotationDegrees_X = lx;
                if (float.TryParse(Environment.GetEnvironmentVariable("LIGHT_Y"), out var ly)) store.DirLightRotationDegrees_Y = ly;
                if (float.TryParse(Environment.GetEnvironmentVariable("LIGHT_I"), out var li)) store.LightIntensityMult = li;
                TestContext.Out.WriteLine($"LIGHT {store.DirLightRotationDegrees_X} {store.DirLightRotationDegrees_Y} {store.LightIntensityMult} env {store.EnvLightRotationDegrees_Y}");
            }
            var camera = Current<ArcBallCamera>();
            var components = Current<IEnumerable<IGameComponent>>().ToList();
            var device = runner.ServiceProvider.GetRequiredService<IWpfGame>().GraphicsDevice;
            using var target = new RenderTarget2D(device, 1024, 768, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents);
            var time = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1 / 60.0));
            var views = new (string Name, Vector3 Eye, Vector3 At)[]
            {
                ("body", new Vector3(0.6f, 1.2f, 2.4f), new Vector3(0, 0.8f, 0)),
                ("weapon", new Vector3(0.4f, 0.9f, 1.3f), new Vector3(0.2f, 0.7f, 0)),
                ("ground", new Vector3(0, 2.2f, 3.4f), new Vector3(0, 0.2f, 0)),
                ("top", new Vector3(0, 4.5f, 0.01f), new Vector3(0, 0, 0)),
                ("blade", new Vector3(1.9f, 0.3f, 1.6f), new Vector3(1.9f, -0.15f, 0)),
                ("bladeback", new Vector3(1.9f, 0.3f, -1.6f), new Vector3(1.9f, -0.15f, 0)),
                ("bladefar", new Vector3(1.9f, 0.6f, -4.5f), new Vector3(1.9f, -0.15f, 0)),
                ("back", new Vector3(-0.9f, 1.3f, -2.0f), new Vector3(0, 0.9f, 0)),
                ("backclose", new Vector3(-0.5f, 1.2f, -1.1f), new Vector3(0, 1.0f, 0)),
            };
            // RENDER_VIEWS: comma list of view names to draw, all of them by default.
            var only = Environment.GetEnvironmentVariable("RENDER_VIEWS")?.Split(',');
            // LIGHT_SWEEP: "x:y,x:y" light rotations, each drawn with every view; the file name carries them.
            var sweep = (Environment.GetEnvironmentVariable("LIGHT_SWEEP") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split(':')).Select(p => ((float?)float.Parse(p[0]), (float?)float.Parse(p[1]))).DefaultIfEmpty(((float?)null, (float?)null));
            foreach (var (sx, sy) in sweep)
            foreach (var view in views.Where(v => only == null || only.Contains(v.Name)))
            {
                var lightStore = Current<SceneRenderParametersStore>();
                if (sx.HasValue) (lightStore.DirLightRotationDegrees_X, lightStore.DirLightRotationDegrees_Y) = (sx.Value, sy!.Value);
                var lightTag = sx.HasValue ? $"_L{sx}_{sy}" : "";
                for (var f = 0; f < int.Parse(Environment.GetEnvironmentVariable("RENDER_FRAMES") ?? "30"); f++)
                {
                    // Through the arc ball, not an override, so the light turns with the view as it does in the app.
                    var toEye = view.Eye - view.At;
                    camera.ViewMatrixOverride = null;
                    (camera.LookAt, camera.Zoom) = (view.At, toEye.Length());
                    toEye.Normalize();
                    (camera.Yaw, camera.Pitch) = (MathF.Atan2(toEye.X, toEye.Z), -MathF.Asin(toEye.Y));
                    device.SetRenderTarget(target);
                    // SAMPLER_POISON=1: every sampler slot left point-filtered and clamped, as the particle pass's samplers leave them.
                    if (Environment.GetEnvironmentVariable("SAMPLER_POISON") == "1")
                        for (var i = 0; i < 16; i++)
                            device.SamplerStates[i] = SamplerState.PointClamp;
                    foreach (var component in components.OfType<IUpdateable>().OrderBy(x => x.UpdateOrder))
                        component.Update(time);
                    // FAKE_PARTICLE=1: an empty item in the particle bucket, so the frame takes the particle path with nothing drawn.
                    if (Environment.GetEnvironmentVariable("FAKE_PARTICLE") == "1")
                        Current<RenderEngineComponent>().AddRenderItem(RenderBuckedId.Transparent, new NothingItem());
                    if (opened is Editors.AnimationMeta.SuperView.SuperViewViewModel playing)
                        foreach (var item in playing.SceneObjects)
                            item.Data.Update(time);
                    foreach (var component in components.OfType<IDrawable>().OrderBy(x => x.DrawOrder))
                        component.Draw(time);
                    device.SetRenderTarget(null);
                }
                using var stream = File.Create(Path.Combine(output, $"{tag}_{editor}_{view.Name}{lightTag}.png"));
                target.SaveAsPng(stream, target.Width, target.Height);
            }
        }
    }
}
