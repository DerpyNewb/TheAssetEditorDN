using System.Text;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace GameWorld.Core.Test.Services
{
    // Weapon slot names a bone but only references a shared set whose slot names none (CHD dual axes).
    [Apartment(ApartmentState.STA)]
    public class ComplexMeshLoaderAttachPointTests
    {
        const string Axe = @"variantmeshes\wh_variantmodels\hu17\skv\skv_props\skv_warpstone_1h_01.wsmodel";

        static SceneNode Load(string outer, string inner)
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), false);
            var pack = runner.CreateOutputPack();
            runner.PackFileService.AddFilesToPack(pack, [
                new NewPackFileEntry(@"variantmeshes\variantmeshdefinitions", PackFile.CreateFromBytes("test_outer.variantmeshdefinition", Encoding.UTF8.GetBytes(outer))),
                new NewPackFileEntry(@"variantmeshes\variantmeshdefinitions", PackFile.CreateFromBytes("test_axe_set.variantmeshdefinition", Encoding.UTF8.GetBytes(inner))),
            ]);

            var loader = runner.ServiceProvider.CreateScope().ServiceProvider.GetRequiredService<ComplexMeshLoader>();
            var file = runner.PackFileService.FindFile(@"variantmeshes\variantmeshdefinitions\test_outer.variantmeshdefinition");
            return loader.Load(file!, new AnimationPlayer(), true, false);
        }

        static string Weapon(string attachPoint, string reference) =>
            $"<VARIANT_MESH><SLOT name=\"weapon_2\"{attachPoint}><VARIANT_MESH_REFERENCE definition=\"{reference}\" /></SLOT></VARIANT_MESH>";

        static string AxeSet(string attachPoint) =>
            $"<VARIANT_MESH><SLOT name=\"axe\"{attachPoint}><VARIANT_MESH model=\"{Axe}\" /></SLOT></VARIANT_MESH>";

        static List<string> AttachPoints(SceneNode root) =>
            SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(root).Select(x => x.AttachmentPointName).Distinct().ToList();

        [Test]
        public void AReferencedSetWithNoAttachPoint_TakesTheOuterSlots()
        {
            var root = Load(Weapon(" attach_point=\"be_prop_1\"", @"variantmeshes\variantmeshdefinitions\test_axe_set.variantmeshdefinition"), AxeSet(""));

            Assert.That(AttachPoints(root), Is.EqualTo(new[] { "be_prop_1" }));
        }

        [Test]
        public void AnInnerSlotsOwnAttachPoint_Wins()
        {
            var root = Load(Weapon(" attach_point=\"be_prop_1\"", @"variantmeshes\variantmeshdefinitions\test_axe_set.variantmeshdefinition"), AxeSet(" attach_point=\"be_prop_4\""));

            Assert.That(AttachPoints(root), Is.EqualTo(new[] { "be_prop_4" }));
        }
    }
}
