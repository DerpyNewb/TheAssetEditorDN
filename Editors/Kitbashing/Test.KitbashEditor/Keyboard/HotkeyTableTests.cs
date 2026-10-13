using System.Reflection;
using System.Windows.Input;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using KitbasherEditor.ViewModels.MenuBarViews;
using Shared.Core.Commands;
using Shared.Ui.Common.MenuSystem;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.Keyboard
{
    // Numpad + was bound to three commands: gizmo bigger, gizmo smaller and "split mesh into parts". The two sizes cancelled
    // and the selected mesh was split.
    public class HotkeyTableTests
    {
        AssetEditorTestRunner _runner = null!;

        [SetUp]
        public void OpenKarl()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(TestFiles.KarlPackFile, true);
            _runner.CommandFactory.Create<OpenEditorCommand>().Execute(_runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);
        }

        T Service<T>() where T : notnull => _runner.GetRequiredServiceInCurrentEditorScope<T>();
        float GizmoSize() => ((Gizmo)typeof(GizmoComponent).GetField("_gizmo", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Service<GizmoComponent>())!).ScaleModifier;

        [Test]
        public void NumpadPlusAndMinus_SizeTheGizmo_AndLeaveTheMeshWhole()
        {
            var mainNode = Service<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            var meshes = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(mainNode);
            ((ObjectSelectionState)Service<SelectionManager>().CreateSelectionSate(GeometrySelectionMode.Object, null!)).ModifySelection([meshes.First()], false);
            var menu = Service<MenuBarViewModel>();
            var size = GizmoSize();

            menu.OnKeyReleased(Key.Add, Key.Add, ModifierKeys.None);
            Assert.That(GizmoSize(), Is.GreaterThan(size));
            Assert.That(SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(mainNode), Has.Count.EqualTo(meshes.Count), "numpad + split the mesh");

            for (var i = 0; i < 5; i++)
                menu.OnKeyReleased(Key.Subtract, Key.Subtract, ModifierKeys.None);
            Assert.That(GizmoSize(), Is.LessThan(size).And.GreaterThan(0), "the gizmo shrank to nothing, or turned inside out");
        }

        [Test]
        public void NoTwoCommandsShareAHotkey()
        {
            var actions = (Dictionary<Type, MenuAction>)typeof(MenuBarViewModel).GetField("_uiCommands", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Service<MenuBarViewModel>())!;

            var shared = actions.Where(x => x.Value.Hotkey != null)
                .GroupBy(x => (x.Value.Hotkey.Key, x.Value.Hotkey.ModifierKeys))
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key}: {string.Join(", ", g.Select(x => x.Key.Name))}");

            Assert.That(shared, Is.Empty);
        }
    }
}
