using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Editors.CscEditor.ViewModels;
using Shared.Ui.Common;

namespace Editors.CscEditor.Views
{
    public partial class CscEditorView : UserControl
    {
        Point _dragStart;
        CscElementViewModel? _dragCandidate;

        CscEditorViewModel? ViewModel => DataContext as CscEditorViewModel;

        public CscEditorView()
        {
            InitializeComponent();
            CurveEditor.CurvesModified += () => ViewModel?.OnCurvesModified();
            // One undo step per gesture: when a mouse button is let go, handled or not, so a checkbox, a slider or a curve drag
            // has already changed the scene; and when a typed field is left.
            AddHandler(MouseUpEvent, new MouseButtonEventHandler((_, _) => ViewModel?.Checkpoint()), handledEventsToo: true);
            LostKeyboardFocus += (_, _) => ViewModel?.Checkpoint();
            UndoRedoKeys.Attach(this, () => ViewModel?.Undo(), () => ViewModel?.Redo());
            DataContextChanged += (_, _) =>
            {
                if (ViewModel != null)
                    ViewModel.RedrawCurves = CurveEditor.Redraw;
            };
        }

        void ComponentTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (ViewModel == null)
                return;

            if (e.NewValue is CscElementViewModel vm)
                ViewModel.SelectedElement = vm;
            else if (e.NewValue is CscSceneRootViewModel root)
                ViewModel.SelectedSceneRoot = root;
        }

        void CurveVisibility_Click(object sender, RoutedEventArgs e) => CurveEditor.Redraw();

        void AuxField_LostFocus(object sender, RoutedEventArgs e) => ViewModel?.OnAuxFieldModified();

        // ---------------------------------------------------------------------
        // Tree drag-drop re-parenting
        // ---------------------------------------------------------------------

        void ComponentTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(ComponentTree);
            _dragCandidate = FindElementVm(e.OriginalSource);
        }

        void ComponentTree_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragCandidate == null || e.LeftButton != MouseButtonState.Pressed)
                return;

            var position = e.GetPosition(ComponentTree);
            if (System.Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                System.Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var dragged = _dragCandidate;
            _dragCandidate = null;
            DragDrop.DoDragDrop(ComponentTree, new DataObject(typeof(CscElementViewModel), dragged), DragDropEffects.Move);
        }

        void ComponentTree_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(CscElementViewModel)) is not CscElementViewModel dragged)
                return;

            var target = FindElementVm(e.OriginalSource);
            ViewModel?.ReparentElement(dragged, target); // target == null -> detach to top level
            e.Handled = true;
        }

        static CscElementViewModel? FindElementVm(object originalSource)
        {
            var current = originalSource as DependencyObject;
            while (current != null && current is not TreeViewItem)
                current = VisualTreeHelper.GetParent(current);
            return (current as TreeViewItem)?.DataContext as CscElementViewModel;
        }
    }
}
