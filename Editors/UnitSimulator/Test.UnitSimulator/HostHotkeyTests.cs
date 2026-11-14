using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Test.UnitSimulator
{
    // The animation editors' play/pause hotkey works anywhere in their window while the editor shows, as Kitbash's hotkeys do,
    // except in a text box. The key is raised as the tunnelling PreviewKeyDown a real one is.
    public class HostHotkeyTests
    {
        [Test, Apartment(ApartmentState.STA)]
        public void Space_IsTakenAnywhereInTheWindow_ButNotInATextBox_OrWhileTheEditorIsHidden()
        {
            using var shown = HostLayoutTests.Show(HostLayoutTests.OpenPlain());
            var window = Window.GetWindow(shown.View)!;
            var box = new TextBox();
            var button = new Button();
            window.Content = null;
            window.Content = new DockPanel { Children = { box, button, shown.View } };
            shown.Lay();

            bool Taken(UIElement target)
            {
                var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Space) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                target.RaiseEvent(e);
                return e.Handled;
            }

            Assert.That(Taken(shown.View), Is.True, "in the editor, after it was moved into another parent");
            Assert.That(Taken(button), Is.True, "on a button, which would otherwise click as well");
            Assert.That(Taken(box), Is.False, "in a text box");
            shown.View.Visibility = Visibility.Collapsed;
            Assert.That(Taken(button), Is.False, "with the editor's tab not shown");
        }

        // Left and Right step a frame from the editor or a button, held too, but a control that moves with the arrows keeps them.
        [Test, Apartment(ApartmentState.STA)]
        public void TheStepKeys_AreTakenAndRepeat_ButControlsThatMoveWithArrowsKeepThem()
        {
            using var shown = HostLayoutTests.Show(HostLayoutTests.OpenPlain());
            var window = Window.GetWindow(shown.View)!;
            var list = new ListBox { Items = { "a", "b" } };
            var combo = new ComboBox { Items = { "a", "b" } };
            var tree = new TreeView { Items = { new TreeViewItem { Header = "a" } } };
            var grid = new DataGrid { ItemsSource = new[] { new { Name = "a" } } };
            var slider = new Slider();
            var tabs = new TabControl { Items = { new TabItem { Header = "a" }, new TabItem { Header = "b" } } };
            var button = new Button();
            var box = new TextBox();
            window.Content = null;
            window.Content = new DockPanel { Children = { list, combo, tree, grid, slider, tabs, button, box, shown.View } };
            shown.Lay();

            bool Taken(UIElement target, Key key, bool held = false)
            {
                var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                typeof(KeyEventArgs).GetMethod("SetRepeat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(e, [held]);
                target.RaiseEvent(e);
                return e.Handled;
            }

            Assert.That(Taken(shown.View, Key.Right), Is.True, "in the editor");
            Assert.That(Taken(shown.View, Key.Left, held: true), Is.True, "held, it steps on");
            Assert.That(Taken(button, Key.Right), Is.True, "on a button");
            Assert.That(Taken(shown.View, Key.Space, held: true), Is.False, "a held Space does not toggle again");

            var cell = FindAll<DataGridCell>(grid).First();
            foreach (var (control, name) in new (UIElement, string)[] { (list, "list"), ((ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0), "list row"),
                (combo, "combo box"), (tree, "tree"), ((TreeViewItem)tree.Items[0], "tree row"), (cell, "grid cell"), (slider, "slider"),
                ((TabItem)tabs.Items[0], "tab"), (box, "text box") })
            {
                Assert.That(Taken(control, Key.Right), Is.False, name);
                Assert.That(Taken(control, Key.Left), Is.False, name);
            }
            Assert.That(Taken(list, Key.Space), Is.True, "Space still plays from a list, as it did");
        }

        static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    yield return match;
                foreach (var deeper in FindAll<T>(child))
                    yield return deeper;
            }
        }
    }
}
