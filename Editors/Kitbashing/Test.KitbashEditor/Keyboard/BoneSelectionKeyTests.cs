using GameWorld.Core.Components;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Shared.Core.Commands;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.Keyboard
{
    // F9 is the selection component's bone mode, which the keyframe editor poses with. Kitbash has no bone tools: its toolbar
    // threw on the mode, and every click in it asked for an animation. In Kitbash, F9 leaves the selection where it was.
    public class BoneSelectionKeyTests
    {
        [Test]
        public void F9_LeavesKitbashInObjectMode()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(TestFiles.KarlPackFile, true);
            runner.CommandFactory.Create<OpenEditorCommand>().Execute(runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);
            var mainNode = runner.GetRequiredServiceInCurrentEditorScope<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            var mesh = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(mainNode).First();
            var selection = runner.GetRequiredServiceInCurrentEditorScope<SelectionManager>();
            ((ObjectSelectionState)selection.CreateSelectionSate(GeometrySelectionMode.Object, null!)).ModifySelection([mesh], false);

            var switched = runner.GetRequiredServiceInCurrentEditorScope<SelectionComponent>().SetBoneSelectionMode();

            Assert.That(switched, Is.False);
            Assert.That(selection.GetState().Mode, Is.EqualTo(GeometrySelectionMode.Object));
        }
    }
}
