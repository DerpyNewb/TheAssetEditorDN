using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.Presentation.View;
using Shared.Core.Commands;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimationMeta
{
    // Ctrl+C, Ctrl+V and Delete copy, paste and delete the selected tags, as their buttons do, but a value box being typed in
    // keeps them for its text. The keys go through WPF's own gesture matching; Ctrl is held in this thread's key state only,
    // which sends no input anywhere.
    public class MetaEditorKeyTests
    {
        const string FilePath = @"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";

        [Test, Apartment(ApartmentState.STA)]
        public void TheTagKeys_ActOnTheSelectedTags_ButAValueBoxKeepsThem()
        {
            AppResources.Load();
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            var editor = runner.CommandFactory.Create<OpenEditorCommand>().Execute<MetaDataEditorViewModel>(runner.PackFileService.FindFile(FilePath)!, Shared.Core.ToolCreation.EditorEnums.Meta_Editor);
            var view = new MainEditorView { DataContext = editor };
            var host = new Window { Content = view, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None, Left = -10000, Width = 900, Height = 900 };
            try
            {
                host.Show();
                var count = editor.Tags.Count;
                var first = editor.Tags[0]._input;
                Select(editor, 0);
                Pump();
                var row = FindAll<ListViewItem>(view).First(x => x.IsSelected);

                Assert.That(Press(host, row, Key.C, ctrl: true), Is.True, "Ctrl+C on the list");
                Assert.That(Press(host, row, Key.V, ctrl: true), Is.True, "Ctrl+V on the list");
                Assert.That(editor.Tags, Has.Count.EqualTo(count + 1), "pasted");
                Assert.That(editor.Tags.Last()._input.GetType(), Is.EqualTo(first.GetType()), "a copy of the first tag");

                Select(editor, editor.Tags.Count - 1);
                Pump();
                row = FindAll<ListViewItem>(view).First(x => x.IsSelected);
                Assert.That(Press(host, row, Key.Delete), Is.True, "Delete on the list");
                Assert.That(editor.Tags, Has.Count.EqualTo(count), "deleted");

                // The copy still holds the first tag; with a tag of another kind selected, keys pressed in its value box must not
                // reach the tags.
                var other = editor.Tags.ToList().FindIndex(x => x._input.GetType() != first.GetType());
                Select(editor, other);
                Pump();
                var box = FindAll<TextBox>(view).First(x => x.IsReadOnly == false && x.IsVisible);
                foreach (var (key, ctrl) in new[] { (Key.C, true), (Key.V, true), (Key.Delete, false) })
                    Assert.That(Press(host, box, key, ctrl), Is.True, $"{key}: the box takes it");
                Assert.That(editor.Tags, Has.Count.EqualTo(count), "no tag pasted or deleted from the box");
                editor.PasteActionCommand.Execute(null);
                Assert.That(editor.Tags.Last()._input.GetType(), Is.EqualTo(first.GetType()), "Ctrl+C in the box did not copy the selected tag");

                editor.LoadFile(null);
                Assert.That(editor.PasteActionCommand.CanExecute(null), Is.False, "no file, nothing to paste into; it used to throw");
            }
            finally
            {
                host.Close();
            }
        }

        static void Select(MetaDataEditorViewModel editor, int index)
        {
            foreach (var tag in editor.Tags)
                tag.IsSelected = false;
            editor.Tags[index].IsSelected = true;
            editor.SelectedTag = editor.Tags[index];
        }

        static bool Press(Window host, UIElement target, Key key, bool ctrl = false)
        {
            var state = new byte[256];
            GetKeyboardState(state);
            var held = (byte[])state.Clone();
            held[VkControl] = held[VkLeftControl] = ctrl ? (byte)0x80 : (byte)0;
            SetKeyboardState(held);
            try
            {
                Assert.That(Keyboard.Modifiers, Is.EqualTo(ctrl ? ModifierKeys.Control : ModifierKeys.None), "the thread's Ctrl state");
                var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(host)!, 0, key) { RoutedEvent = Keyboard.KeyDownEvent };
                target.RaiseEvent(e);
                Pump();
                return e.Handled;
            }
            finally
            {
                SetKeyboardState(state);
            }
        }

        const int VkControl = 0x11, VkLeftControl = 0xA2;
        [DllImport("user32.dll")] static extern bool GetKeyboardState(byte[] state);
        [DllImport("user32.dll")] static extern bool SetKeyboardState(byte[] state);

        static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

        static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    yield return match;
                foreach (var deeper in FindAll<T>(child))
                    yield return deeper;
            }
        }
    }
}
