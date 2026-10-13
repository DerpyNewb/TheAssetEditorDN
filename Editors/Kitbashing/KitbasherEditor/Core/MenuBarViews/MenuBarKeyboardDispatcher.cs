using System.Windows.Controls;
using System.Windows.Input;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor.Core.MenuBarViews
{
    /// <summary>
    /// Encapsulates the keyboard event dispatch logic for MenuBarView.
    /// Extracted to enable unit testing of the visibility and source-filtering guards.
    /// </summary>
    public class MenuBarKeyboardDispatcher
    {
        private readonly Func<bool> _isVisible;
        private readonly Func<IKeyboardHandler?> _getHandler;

        // Hotkeys fire on release. A combination counts by the modifiers held when its key went down, so Ctrl+Z still undoes when
        // Ctrl is let go first; a release whose press was not seen counts what is held now.
        private readonly Dictionary<Key, ModifierKeys> _modifiersAtPress = new();

        public MenuBarKeyboardDispatcher(Func<bool> isVisible, Func<IKeyboardHandler?> getHandler)
        {
            _isVisible = isVisible;
            _getHandler = getHandler;
        }

        // Plain keys typed into a text box are the box's, so after typing a value every plain-key hotkey looked dead until a left
        // click in the viewport, which also changed the selection. Enter or Escape in a one-line box hands the keyboard back.
        public static bool HandsKeyboardBack(Key key, object? source) => key is Key.Enter or Key.Escape && source is TextBox { AcceptsReturn: false };

        /// <summary>
        /// Returns true if the event was dispatched to the handler.
        /// </summary>
        public bool HandleKeyUp(Key key, Key systemKey, ModifierKeys modifiers, bool isSourceTextBox)
        {
            if (_modifiersAtPress.Remove(key, out var atPress))
                modifiers = atPress;

            if (!_isVisible())
                return false;

            if (isSourceTextBox && modifiers == ModifierKeys.None)
                return false;

            if (_getHandler() is IKeyboardHandler handler)
            {
                handler.OnKeyReleased(key, systemKey, modifiers);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns true if the event was dispatched to the handler.
        /// </summary>
        public bool HandleKeyDown(Key key, Key systemKey, ModifierKeys modifiers, bool isSourceTextBox)
        {
            _modifiersAtPress[key] = modifiers;

            if (!_isVisible())
                return false;

            if (isSourceTextBox && modifiers == ModifierKeys.None)
                return false;

            if (_getHandler() is IKeyboardHandler handler)
            {
                handler.OnKeyDown(key, systemKey, modifiers);
                return true;
            }

            return false;
        }
    }
}
