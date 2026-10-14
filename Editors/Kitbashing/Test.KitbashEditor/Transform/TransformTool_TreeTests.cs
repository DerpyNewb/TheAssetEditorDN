using Editors.KitbasherEditor.Events;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using KitbasherEditor.ViewModels.MenuBarViews;
using KitbasherEditor.Views;
using Shared.Core.Commands;
using Shared.Core.Events;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;
using static KitbasherEditor.ViewModels.MenuBarViews.TransformToolViewModel;

namespace Test.KitbashEditor.Transform
{
    // Each step of a scrub in the transform box undoes the last preview, and the undo puts the selection back as it was. The
    // scene tree passed that on as a new node selection, and the mesh panel was thrown away and built again for the same mesh:
    // 400 ms a step with the view laid out, against 14 ms once the tree keeps quiet about a selection that did not change.
    public class TransformTool_TreeTests
    {
        [Test, Apartment(ApartmentState.STA)]
        public void AScrubStep_LeavesTheTreeQuiet_AndADifferentSelectionStillReachesIt()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(TestFiles.KarlPackFile, true);
            runner.CommandFactory.Create<OpenEditorCommand>().Execute(runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);
            T Service<T>() where T : notnull => runner.GetRequiredServiceInCurrentEditorScope<T>();

            var hub = Service<IEventHub>();
            var selection = Service<SelectionManager>();
            _ = new MultiSelectTreeView { SceneManager = Service<SceneManager>(), SelectionManager = selection, EventHub = hub };
            var announced = new List<List<ISceneNode>>();
            hub.Register<SceneNodeSelectedEvent>(announced, x => announced.Add(x.SelectedObjects));

            var main = Service<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            var mesh = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(main).First();
            void Select(params ISelectable[] nodes) =>
                ((ObjectSelectionState)selection.CreateSelectionSate(GeometrySelectionMode.Object, null!)).ModifySelection(nodes.ToList(), false);

            Select(mesh);
            Assert.That(announced, Is.Not.Empty, "selecting the mesh never reached the tree's listeners, so this proves nothing");
            var afterSelecting = announced.Count;

            var tool = Service<TransformToolViewModel>();
            tool.SetMode(TransformMode.Rotate);
            for (var degrees = 1; degrees <= 5; degrees++)
                tool.Vector3.Y.Value = degrees;
            Assert.That(announced, Has.Count.EqualTo(afterSelecting), "a scrub step announced the mesh as newly selected");

            Select();
            Assert.That(announced, Has.Count.EqualTo(afterSelecting + 1));
            Assert.That(announced[^1], Is.Empty);
        }
    }
}
