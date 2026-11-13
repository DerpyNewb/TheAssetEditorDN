using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Editors.VfxEditor.VariantMesh
{
    public partial class VmdEditorView : UserControl
    {
        VmdEditorViewModel? _vm;
        bool _syncing;

        public VmdEditorView()
        {
            InitializeComponent();
            SetResourceReference(ThemeTextProperty, "ABrush.Foreground.Static");
            ShowStatusColours();
            DataContextChanged += (_, e) =>
            {
                if (_vm != null)
                {
                    _vm.PropertyChanged -= Vm_PropertyChanged;
                    _vm.FlushPendingEdits = null;
                }
                _vm = e.NewValue as VmdEditorViewModel;
                if (_vm != null)
                {
                    _vm.PropertyChanged += Vm_PropertyChanged;
                    _vm.FlushPendingEdits = FlushPendingEdits;
                }
            };
        }

        // Status colours, the bin grid's: the theme has none, so they are set for the theme in use, told from its text colour (light
        // text: a dark theme), and again whenever it changes. Each passes 4.5:1 as text on every cell, selected-cell and panel
        // background of the Dark, Amoled and Light themes (measured 2026-11-12, lowest 4.68:1).

        static readonly DependencyProperty ThemeTextProperty = DependencyProperty.Register("ThemeText", typeof(Brush), typeof(VmdEditorView),
            new PropertyMetadata(null, (view, _) => ((VmdEditorView)view).ShowStatusColours()));

        void ShowStatusColours()
        {
            var lightText = GetValue(ThemeTextProperty) is SolidColorBrush { Color: var text } && text.R + text.G + text.B > 3 * 128;
            var error = lightText ? Color.FromRgb(0xFF, 0x9C, 0x90) : Color.FromRgb(0x8A, 0x14, 0x14);
            var warning = lightText ? Color.FromRgb(0xFF, 0xB5, 0x47) : Color.FromRgb(0x63, 0x40, 0x00);
            Resources["Vmd.Error"] = Frozen(new SolidColorBrush(error));
            Resources["Vmd.Warning"] = Frozen(new SolidColorBrush(warning));
            // Dashes 3px long down the row's edge, so a warning's stripe reads apart from an error's without its colour.
            Resources["Vmd.WarningStripe"] = Frozen(new LinearGradientBrush(
                [new GradientStop(warning, 0), new GradientStop(warning, 0.5), new GradientStop(Colors.Transparent, 0.5), new GradientStop(Colors.Transparent, 1)],
                new Point(0, 0), new Point(0, 6)) { MappingMode = BrushMappingMode.Absolute, SpreadMethod = GradientSpreadMethod.Repeat });
        }

        static Brush Frozen(Brush brush)
        {
            brush.Freeze();
            return brush;
        }

        // A cell still being edited is written before Save, Undo or a toolbar command, none of which take its focus.
        void FlushPendingEdits()
        {
            Table.CommitEdit(DataGridEditingUnit.Cell, true);
            Table.CommitEdit(DataGridEditingUnit.Row, true);
        }

        // The selection lives in the view model, which keeps it across a rebuild; the grid shows it.

        void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(VmdEditorViewModel.SelectedRows) or "" or null)
                ShowSelection();
        }

        void ShowSelection()
        {
            if (_vm == null || _syncing || Table.SelectedItems.Cast<VmdRow>().ToHashSet().SetEquals(_vm.SelectedRows))
                return;
            _syncing = true;
            try
            {
                Table.SelectedItems.Clear();
                foreach (var row in _vm.SelectedRows)
                    Table.SelectedItems.Add(row);
                if (_vm.SelectedRows.Count != 0)
                    Table.ScrollIntoView(_vm.SelectedRows[0]);
            }
            finally
            {
                _syncing = false;
            }
        }

        void Table_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vm == null || _syncing)
                return;
            _syncing = true;
            try
            {
                _vm.SelectedRows = Table.SelectedItems.Cast<VmdRow>().ToList();
            }
            finally
            {
                _syncing = false;
            }
        }

        // A new slot opens its name cell for typing.
        void AddSlot_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null)
                return;
            _vm.AddSlotCommand.Execute(null);
            if (_vm.SelectedRows is not [var row])
                return;
            Table.UpdateLayout();
            Table.ScrollIntoView(row);
            Table.CurrentCell = new DataGridCellInfo(row, Table.Columns[0]);
            Table.BeginEdit();
        }

        // Keys: Ctrl+C and Ctrl+V copy and paste slots, not the grid's own text copy; inside a cell being edited the text box takes
        // them first. Ctrl+H goes to the find box.

        void Copy_Executed(object sender, ExecutedRoutedEventArgs e) => _vm?.CopyCommand.Execute(null);
        void Paste_Executed(object sender, ExecutedRoutedEventArgs e) => _vm?.PasteCommand.Execute(null);

        void View_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
            {
                FindBox.Focus();
                FindBox.SelectAll();
                e.Handled = true;
            }
        }

        void EditBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not UIElement box)
                return;
            box.Focus();
            (sender as TextBox)?.SelectAll();
        }

        void ShowImposter_Click(object sender, RoutedEventArgs e) =>
            ImposterColumn.Visibility = (sender as MenuItem)?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        // A path cell's menu, shared by the column's cells. The item's Tag names the column; the row is the DataContext of the cell
        // the menu opened on.

        static VmdRow? RowOf(object sender) =>
            ((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget is FrameworkElement { DataContext: VmdRow row } ? row : null;

        void Browse_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null || RowOf(sender) is not { } row || sender is not MenuItem { Tag: string column })
                return;
            (column == "Imposter" ? _vm.BrowseImposterCommand : _vm.BrowsePathCommand).Execute(row);
        }

        void Open_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null || RowOf(sender) is not { } row || sender is not MenuItem { Tag: string column })
                return;
            _vm.OpenPathCommand.Execute(column == "Imposter" ? row.Imposter : row.Path);
        }
    }

    /// <summary>A path cell's text: the file name, or "(none)" for an empty path.</summary>
    public sealed class VmdFileNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is string path && path.Length != 0 ? System.IO.Path.GetFileName(path.Replace('\\', '/')) : "(none)";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
