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
    }
}
