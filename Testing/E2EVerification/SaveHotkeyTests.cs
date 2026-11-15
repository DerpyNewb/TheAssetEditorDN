using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using AssetEditor.ViewModels;
using AssetEditor.Views;
using Editors.AnimationMeta.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.Commands;
using Shared.Core.ToolCreation;
using Shared.Ui.Common.MenuSystem;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.E2EVerification
{
    // Ctrl+S saves the open tab when it is an editor that saves, after a text box being typed in has passed its value on.
    internal class SaveHotkeyTests
    {
        const string MetaPath = @"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";

        [Test]
        public void CtrlS_SavesTheOpenEditor_AndARebindMovesIt()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var output = runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true)!;
            // The meta is Throt's, not the editable pack's, so the save asks before overriding it; this answers Override.
            runner.Dialogs.Setup(x => x.ShowChoiceBox(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>())).Returns(1);
            var editor = runner.CommandFactory.Create<OpenEditorCommand>().Execute<MetaDataEditorViewModel>(runner.PackFileService.FindFile(MetaPath)!, EditorEnums.Meta_Editor);
            var main = runner.ServiceProvider.GetRequiredService<MainViewModel>();
            editor.Tags[0].IsSelected = true;
            editor.DeleteActionCommand.Execute(null);
            Assert.That(editor.HasUnsavedChanges, Is.True);

            var committed = 0;
            Assert.That(main.OnKeyPressed(Key.S, ModifierKeys.None, () => committed++), Is.False, "S alone");
            Assert.That(output.GetFileCount(), Is.Zero);
            Assert.That(main.OnKeyPressed(Key.S, ModifierKeys.Control, () => committed++), Is.True);
            Assert.That(committed, Is.EqualTo(1), "typed text is passed on first");
            Assert.That(runner.PackFileService.FindFile(MetaPath, output), Is.Not.Null, "saved into the editable pack");
            Assert.That(editor.HasUnsavedChanges, Is.False);

            runner.ServiceProvider.GetRequiredService<HotkeyService>().Set(new Dictionary<string, Hotkey?> { [MainViewModel.SaveHotkey] = new Hotkey(Key.S, ModifierKeys.Control | ModifierKeys.Shift) });
            Assert.That(main.OnKeyPressed(Key.S, ModifierKeys.Control), Is.False, "Ctrl+S is free again");
            Assert.That(main.OnKeyPressed(Key.S, ModifierKeys.Control | ModifierKeys.Shift), Is.True);
        }

        // An editor with nothing to save, or no editor open, leaves the key to whatever has focus.
        [Test]
        public void CtrlS_DoesNothing_WithoutAnEditorThatSaves()
        {
            var main = new AssetEditorTestRunner().ServiceProvider.GetRequiredService<MainViewModel>();
            var viewer = new Mock<IEditorInterface>().Object;
            var manager = new Mock<IEditorManager>();
            manager.Setup(x => x.GetAllEditors()).Returns([viewer]);
            main.EditorManager = manager.Object;
            var committed = 0;

            manager.Setup(x => x.GetCurrentEditor()).Returns(0);
            Assert.That(main.OnKeyPressed(Key.S, ModifierKeys.Control, () => committed++), Is.False, "a viewer");
            manager.Setup(x => x.GetCurrentEditor()).Returns(-1);
            Assert.That(main.OnKeyPressed(Key.S, ModifierKeys.Control, () => committed++), Is.False, "no tab");
            Assert.That(committed, Is.Zero);
        }

        // A box's value reaches its source when the box loses focus; a save by key has to send it on itself.
        [Test, Apartment(ApartmentState.STA)]
        public void CommitTypedText_PassesABoxsValueOn()
        {
            var source = new Holder { Text = "old" };
            var box = new TextBox { DataContext = source };
            box.SetBinding(TextBox.TextProperty, new Binding(nameof(Holder.Text)));
            box.Text = "new";
            Assert.That(source.Text, Is.EqualTo("old"), "not passed on while typing");

            MainWindow.CommitTypedText(box);
            Assert.That(source.Text, Is.EqualTo("new"));
            Assert.That(() => MainWindow.CommitTypedText(new Button()), Throws.Nothing);
        }

        class Holder
        {
            public string Text { get; set; } = "";
        }
    }
}
