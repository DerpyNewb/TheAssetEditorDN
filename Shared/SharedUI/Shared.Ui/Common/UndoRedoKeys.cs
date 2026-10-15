using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Shared.Ui.Common
{
    /// <summary>
    /// Ctrl+Z undo, Ctrl+Y or Ctrl+Shift+Z redo, for an editor panel while it is shown. Hooked on the window, not the panel: the
    /// panel sits beside a 3D viewport, and that is where focus is while a bone is dragged. A text box, or a text editor with
    /// something to undo, keeps its own.
    /// </summary>
    public static class UndoRedoKeys
    {
        public static void Attach(FrameworkElement view, Action undo, Action redo)
        {
            Window? window = null;
            var handler = new KeyEventHandler((_, e) =>
            {
                if (!view.IsVisible || Keyboard.FocusedElement is TextBoxBase || ApplicationCommands.Undo.CanExecute(null, Keyboard.FocusedElement))
                    return;

                var modifiers = Keyboard.Modifiers;
                if (e.Key == Key.Z && modifiers == ModifierKeys.Control)
                    undo();
                else if (e.Key == Key.Y && modifiers == ModifierKeys.Control || e.Key == Key.Z && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                    redo();
                else
                    return;
                e.Handled = true;
            });

            view.Loaded += (_, _) =>
            {
                window = Window.GetWindow(view);
                window?.AddHandler(Keyboard.PreviewKeyDownEvent, handler, true);
            };
            view.Unloaded += (_, _) =>
            {
                window?.RemoveHandler(Keyboard.PreviewKeyDownEvent, handler);
                window = null;
            };
        }
    }
}
