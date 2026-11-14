using System.Reflection;
using System.Windows.Input;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using KitbasherEditor.ViewModels.MenuBarViews;
using Microsoft.Extensions.DependencyInjection;
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

        Dictionary<Type, MenuAction> Actions() => (Dictionary<Type, MenuAction>)typeof(MenuBarViewModel).GetField("_uiCommands", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Service<MenuBarViewModel>())!;

        // Settings, Hotkeys reads each default off a command it never built; the built command is what the menu runs.
        [Test]
        public void SettingsListsEveryHotkeyTheMenuHas_WithTheDefaultTheBuiltCommandGives()
        {
            var built = Actions().ToDictionary(x => MenuBarViewModel.HotkeyId(x.Key),
                x => ((Editors.KitbasherEditor.Core.MenuBarViews.IKitbasherUiCommand)x.Value.GetType().GetField("_instance", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(x.Value)!).HotKey)
                .Where(x => x.Value != null).ToDictionary();
            var listed = _runner.ServiceProvider.GetRequiredService<HotkeyService>().Definitions.Where(x => x.Group == "Hotkeys.Group.Kitbash").ToDictionary(x => x.Id, x => x.Default);

            Assert.That(built, Has.Count.EqualTo(19));
            Assert.That(listed, Is.EquivalentTo(built));
        }

        [Test]
        public void ARebind_MovesTheCommandToItsNewKey_AltCombinationsToo_AndItsTooltipSaysSo()
        {
            var menu = Service<MenuBarViewModel>();
            var scaleUp = Actions().Single(x => x.Key.Name == "ScaleGizmoUpCommand");
            var size = GizmoSize();

            _runner.ServiceProvider.GetRequiredService<HotkeyService>().Set(new Dictionary<string, Hotkey?> { [MenuBarViewModel.HotkeyId(scaleUp.Key)] = new Hotkey(Key.G, ModifierKeys.Alt) });

            menu.OnKeyReleased(Key.Add, Key.None, ModifierKeys.None);
            Assert.That(GizmoSize(), Is.EqualTo(size), "the old key");
            menu.OnKeyReleased(Key.System, Key.G, ModifierKeys.Alt);
            Assert.That(GizmoSize(), Is.GreaterThan(size), "the new key, which comes as Key.System while Alt is held");
            Assert.That(scaleUp.Value.ToolTipAttribute.Value, Does.EndWith("(Alt+G)"));
        }
    }
}
