using System.IO;
using Editors.AnimationMeta.SuperView;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.Commands;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimationMeta
{
    // A .variantmeshdefinition offers SuperView in the tool selector, beside the XML and Kitbash editors.
    public class SuperViewOpenFileTests
    {
        const string ThrotVmd = @"variantmeshes\variantmeshdefinitions\skv_throt.variantmeshdefinition";

        AssetEditorTestRunner _runner = null!;

        [SetUp]
        public void Setup()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
        }

        [Test]
        public void VariantMesh_OpensInSuperView_WithItsSkeletonAndAnimations()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);

            Assert.That(superView.CurrentFile, Is.SameAs(vmd));
            var asset = superView.SceneObjects[0];
            Assert.That(asset.Data.MeshName.Value, Is.EqualTo(vmd.Name));
            Assert.That(asset.Data.Skeleton, Is.Not.Null, "the mesh's skeleton did not load");

            // Picking an animation set and slot loads that animation's meta, as when SuperView is opened empty.
            var selection = asset.FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == @"animations/database/battle/bin/hu17_dlc16_throt.bin");
            selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "ATTACK_5");
            Assert.That(superView.MetaEditor.ParsedFile, Is.Not.Null);
            Assert.That(superView.MetaEditor.Tags, Is.Not.Empty);
        }

        // The meta props always play; the main model has to as well, or it stands in its bind pose
        // under them until the "Animation" toggle is found.
        [Test]
        public void VariantMesh_MainModelPlaysThePickedSlot()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == @"animations/database/battle/bin/hu17_dlc16_throt.bin");
            selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "ATTACK_5");

            var data = superView.SceneObjects[0].Data;
            var meshes = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(data.ModelNode);
            Assert.That(meshes, Is.Not.Empty);
            Assert.That(meshes.All(x => x.AnimationPlayer == data.Player), "a mesh is not driven by the model's player");

            // One step of what AnimationsContainerComponent does every frame.
            Matrix[] Pose(double ms)
            {
                data.Player.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(ms)));
                return Enumerable.Range(0, data.Skeleton.BoneCount).Select(data.Skeleton.GetAnimatedWorldTranform).ToArray();
            }
            var start = Pose(0);
            var later = Pose(400);

            Assert.That(data.Player.IsEnabled, Is.True, "the main model's player is off");
            Assert.That(later, Is.Not.EqualTo(start), "no bone moved in 400ms of the slot's animation");
        }

        [Test]
        public void VariantMesh_AlreadyOpenInKitbash_StillOpensInSuperView()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var open = _runner.CommandFactory.Create<OpenEditorCommand>();
            open.Execute(vmd, EditorEnums.Kitbash_Editor);

            Assert.That(open.Execute(vmd, EditorEnums.SuperView_Editor), Is.InstanceOf<SuperViewViewModel>());
        }

        // A vfx file added or saved in any pack (the VFX editor's save, a replaced texture) rebuilds the scene's effects, so
        // SuperView shows it without the tab being reopened; any other file leaves the scene alone.
        [TestCase(@"vfx\test_smoke.xml", true)]
        [TestCase(@"text\notes.txt", false)]
        public void AChangedVfxFile_RebuildsTheScene(string path, bool rebuilds)
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var data = superView.SceneObjects[0].Data;
            var pack = _runner.CreateOutputPack();

            var before = data.MetaDataItems;
            _runner.PackFileService.AddFilesToPack(pack, [new NewPackFileEntry(Path.GetDirectoryName(path)!, PackFile.CreateFromBytes(Path.GetFileName(path), [1]))]);
            Assert.That(data.MetaDataItems, rebuilds ? Is.Not.SameAs(before) : Is.SameAs(before), "on add");

            before = data.MetaDataItems;
            _runner.PackFileService.SaveFile(_runner.PackFileService.FindFile(path, pack)!, [2]);
            Assert.That(data.MetaDataItems, rebuilds ? Is.Not.SameAs(before) : Is.SameAs(before), "on save");
        }
    }
}
