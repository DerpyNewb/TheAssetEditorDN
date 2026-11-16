using GameWorld.Core.Components;
using GameWorld.Core.WpfWindow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.Commands;
using Shared.Core.PackFiles.Models;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // Scratch, not committed: the frame that crashed the app - SuperView on ogr_hunter playing the plague ogre's axe attack.
    public class PlagueOgreCrashScratch
    {
        [Test, Explicit, Apartment(ApartmentState.STA)]
        public void Scratch_PlayTheAttack()
        {
            var runner = new AssetEditorTestRunner();
            var type = typeof(IPackFileContainer).Assembly.GetType("Shared.Core.PackFiles.Models.Containers.CachedPackFileContainer")!;
            var ca = (IPackFileContainer)Activator.CreateInstance(type, "All Game Packs - Warhammer III", Environment.GetEnvironmentVariable("CA_CACHE_DB")!)!;
            type.GetProperty("IsCaPackFile")!.SetValue(ca, true);
            runner.PackFileService.AddContainer(ca);

            var file = runner.PackFileService.FindFile(@"variantmeshes\variantmeshdefinitions\ogr_hunter.variantmeshdefinition")!;
            var superView = (Editors.AnimationMeta.SuperView.SuperViewViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute(file, EditorEnums.SuperView_Editor);
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            var anim = Environment.GetEnvironmentVariable("SV_ANIM") ?? "hu13_plague_ogre_2handed_axe_attack_01";
            var found = false;
            foreach (var bin in selection.FragmentList.PossibleValues.ToList())
            {
                selection.FragmentList.SelectedItem = bin;
                var slot = selection.FragmentSlotList.PossibleValues.FirstOrDefault(x => x.AnimationFile?.Contains(anim) == true);
                if (slot == null)
                    continue;
                TestContext.Out.WriteLine($"bin {bin.FullPath} slot {slot.SlotName} anim {slot.AnimationFile} meta {slot.MetaFile}");
                selection.FragmentSlotList.SelectedItem = slot;
                found = true;
                break;
            }
            Assert.That(found, $"no bin of this skeleton plays {anim}");
            superView.ShowVfx = true;

            T Current<T>() where T : notnull => runner.ScopeRepository.GetRequiredService<T>(runner.ScopeRepository.GetEditorHandles().Last());
            var components = Current<IEnumerable<IGameComponent>>().ToList();
            var device = runner.ServiceProvider.GetRequiredService<IWpfGame>().GraphicsDevice;
            using var target = new RenderTarget2D(device, 1024, 768, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents);
            var time = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1 / 60.0));
            for (var f = 0; f < 180; f++)
            {
                try
                {
                    device.SetRenderTarget(target);
                    foreach (var component in components.OfType<IUpdateable>().OrderBy(x => x.UpdateOrder))
                        component.Update(time);
                    foreach (var item in superView.SceneObjects)
                        item.Data.Update(time);
                    foreach (var component in components.OfType<IDrawable>().OrderBy(x => x.DrawOrder))
                        component.Draw(time);
                    device.SetRenderTarget(null);
                }
                catch (Exception e)
                {
                    TestContext.Out.WriteLine($"FRAME {f} THREW: {e}");
                    throw;
                }
            }
            TestContext.Out.WriteLine("180 frames, no exception");
        }
    }
}
