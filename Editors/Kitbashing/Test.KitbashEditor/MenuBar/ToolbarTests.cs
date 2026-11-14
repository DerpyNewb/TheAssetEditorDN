using System.Reflection;
using Editors.KitbasherEditor.ChildEditors.ReplaceTexturePaths;
using Editors.KitbasherEditor.ChildEditors.SnapTool;
using Editors.KitbasherEditor.Services;
using Editors.KitbasherEditor.UiCommands;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using KitbasherEditor.ViewModels.MenuBarViews;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Commands;
using Shared.Core.Events;
using Shared.EmbeddedResources;
using Shared.Ui.Common.MenuSystem;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.MenuBar
{
    // The Kitbash tools added on 2026-11-14 were reachable only from the Tools menu; each now has a button on the bar.
    public class ToolbarTests
    {
        AssetEditorTestRunner _runner = null!;
        MainEditableNode _karl = null!;

        [SetUp]
        public void OpenKarl()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(TestFiles.KarlPackFile, true);
            _runner.CommandFactory.Create<OpenEditorCommand>().Execute(_runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);
            _karl = Service<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
        }

        T Service<T>() where T : notnull => _runner.GetRequiredServiceInCurrentEditorScope<T>();

        static readonly (Type Command, ButtonVisibilityRule Rule)[] s_barTools =
        [
            (typeof(MirrorMeshCommand), ButtonVisibilityRule.ObjectMode),
            (typeof(RecalculateNormalsCommand), ButtonVisibilityRule.ObjectMode),
            (typeof(FlipFacesCommand), ButtonVisibilityRule.ObjectMode),
            (typeof(BakePivotCommand), ButtonVisibilityRule.ObjectMode),
            (typeof(OpenReplaceTexturePathsCommand), ButtonVisibilityRule.Always),
            (typeof(OpenSnapToolCommand), ButtonVisibilityRule.Always),
            (typeof(CheckModelCommand), ButtonVisibilityRule.Always),
        ];

        [Test]
        public void EachNewTool_HasAButtonOnTheBar()
        {
            var menu = Service<MenuBarViewModel>();
            var actions = (Dictionary<Type, MenuAction>)typeof(MenuBarViewModel).GetField("_uiCommands", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(menu)!;

            foreach (var (command, rule) in s_barTools)
            {
                var button = menu.CustomButtons.SingleOrDefault(x => x.Action == actions[command]);
                Assert.That(button, Is.Not.Null, $"{command.Name} has no button on the bar");
                Assert.That(button!.ShowRule, Is.EqualTo(rule), command.Name);
            }
        }

        // An icon that is not embedded throws in IconLibrary.Load, which runs at startup.
        [Test, Apartment(ApartmentState.STA)]
        public void TheBarsNewIcons_Load()
        {
            IconLibrary.Load();

            foreach (var icon in new[] { IconLibrary.MirrorIcon, IconLibrary.RecalculateNormalsIcon, IconLibrary.FlipFacesIcon, IconLibrary.BakePivotIcon,
                IconLibrary.ReplaceTexturePathsIcon, IconLibrary.SnapIcon, IconLibrary.CheckModelIcon, IconLibrary.PickBoneIcon })
                Assert.That(icon?.PixelWidth, Is.GreaterThan(0));
        }

        // Pick mode sits on the sidebar under the selection modes, in a group of its own so it leaves the selection mode alone. The
        // grouped buttons are radio buttons, which a click only ever turns on, so this one follows the picker instead.
        [Test]
        public void PickBoneMode_IsASidebarToggle_ThatShowsWhetherItIsOn()
        {
            var menu = Service<MenuBarViewModel>();
            var picker = Service<BonePicker>();
            var actions = (Dictionary<Type, MenuAction>)typeof(MenuBarViewModel).GetField("_uiCommands", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(menu)!;
            var buttons = menu.SidebarButtons.ToList();
            var button = (MenuBarGroupButton)buttons.Single(x => x.Action == actions[typeof(PickBoneModeCommand)]);
            Assert.That(buttons.IndexOf(button), Is.EqualTo(buttons.FindIndex(x => x.Action == actions[typeof(VertexSelectionModeCommand)]) + 1));
            Assert.That(button.GroupName, Is.Not.EqualTo(((MenuBarGroupButton)buttons.Single(x => x.Action == actions[typeof(VertexSelectionModeCommand)])).GroupName));

            button.Action!.Command.Execute(null);
            Assert.That((picker.IsActive, button.IsChecked.Value), Is.EqualTo((true, true)));

            button.Action.Command.Execute(null);
            Assert.That((picker.IsActive, button.IsChecked.Value), Is.EqualTo((false, false)));
        }

        [Test]
        public void BakePivot_BakesEverySelectedMesh_AsOneUndoStep()
        {
            var meshes = _karl.GetMeshesInLod(0, true).Take(2).ToList();
            var pivot = new Vector3(0.1f, -0.2f, 0.3f);
            var before = meshes.Select(x => x.Geometry.VertexArray[0].Position).ToList();
            foreach (var mesh in meshes)
                mesh.PivotPoint = pivot;
            ((ObjectSelectionState)Service<SelectionManager>().CreateSelectionSate(GeometrySelectionMode.Object, null!)).ModifySelection(meshes, false);

            Service<IUiCommandFactory>().Create<BakePivotCommand>().Execute();

            for (var i = 0; i < meshes.Count; i++)
            {
                Assert.That(meshes[i].PivotPoint, Is.EqualTo(Vector3.Zero));
                var moved = meshes[i].Geometry.VertexArray[0].Position;
                Assert.That(new Vector3(moved.X, moved.Y, moved.Z), Is.EqualTo(new Vector3(before[i].X, before[i].Y, before[i].Z) + pivot));
            }

            Service<CommandManager>().Undo();
            Assert.That(meshes.Select(x => x.PivotPoint), Is.All.EqualTo(pivot), "one undo did not restore every mesh");
            Assert.That(meshes.Select(x => x.Geometry.VertexArray[0].Position), Is.EqualTo(before));
        }
    }
}
