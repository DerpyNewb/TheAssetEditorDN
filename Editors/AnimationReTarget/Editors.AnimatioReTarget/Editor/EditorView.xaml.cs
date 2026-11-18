using System.Windows.Controls;
using System.Windows.Input;
using Shared.Ui.Common;

namespace Editors.AnimatioReTarget.Editor
{
    /// <summary>
    /// Interaction logic for EditorView.xaml
    /// </summary>
    public partial class EditorView : UserControl
    {
        public EditorView()
        {
            InitializeComponent();
            // One undo step per gesture: when a mouse button is let go, handled or not, so a checkbox or a slider has already
            // changed; and when a typed field is left. A drag of the bone handle in the 3D view ends a step of its own.
            AddHandler(MouseUpEvent, new MouseButtonEventHandler((_, _) => (DataContext as AnimationRetargetEditor)?.Checkpoint()), handledEventsToo: true);
            LostKeyboardFocus += (_, _) => (DataContext as AnimationRetargetEditor)?.Checkpoint();
            UndoRedoKeys.Attach(this, () => (DataContext as AnimationRetargetEditor)?.Undo(), () => (DataContext as AnimationRetargetEditor)?.Redo());
        }
    }
}
