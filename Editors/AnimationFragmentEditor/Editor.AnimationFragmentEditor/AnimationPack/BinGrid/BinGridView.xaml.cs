using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Editors.AnimationFragmentEditor.AnimationPack.BinGrid
{
    public partial class BinGridView : UserControl
    {
        BinGridViewModel? _vm;
        bool _syncing;

        public BinGridView()
        {
            InitializeComponent();
            SetResourceReference(ThemeTextProperty, "ABrush.Foreground.Static");
            ShowStatusColours();
            DataContextChanged += (_, e) =>
            {
                if (_vm != null)
                    _vm.PropertyChanged -= Vm_PropertyChanged;
                _vm = e.NewValue as BinGridViewModel;
                if (_vm != null)
                    _vm.PropertyChanged += Vm_PropertyChanged;
            };
        }

        // Status colours. The theme has none, so the view sets BinGrid.Error, BinGrid.Warning and BinGrid.WarningStripe for the theme
        // in use, told from its text colour (light text: a dark theme), and again whenever the theme changes. Each colour passes 4.5:1
        // as text on every cell, selected-cell and panel background of the Dark, Amoled and Light themes (measured 2026-11-12,
        // lowest 4.68:1).

        static readonly DependencyProperty ThemeTextProperty = DependencyProperty.Register("ThemeText", typeof(Brush), typeof(BinGridView),
            new PropertyMetadata(null, (view, _) => ((BinGridView)view).ShowStatusColours()));

        void ShowStatusColours()
        {
            var lightText = GetValue(ThemeTextProperty) is SolidColorBrush { Color: var text } && text.R + text.G + text.B > 3 * 128;
            var error = lightText ? Color.FromRgb(0xFF, 0x9C, 0x90) : Color.FromRgb(0x8A, 0x14, 0x14);
            var warning = lightText ? Color.FromRgb(0xFF, 0xB5, 0x47) : Color.FromRgb(0x63, 0x40, 0x00);
            Resources["BinGrid.Error"] = Frozen(new SolidColorBrush(error));
            Resources["BinGrid.Warning"] = Frozen(new SolidColorBrush(warning));
            // Dashes 3px long down the row's edge, so a warning's stripe reads apart from an error's without its colour.
            Resources["BinGrid.WarningStripe"] = Frozen(new LinearGradientBrush(
                [new GradientStop(warning, 0), new GradientStop(warning, 0.5), new GradientStop(Colors.Transparent, 0.5), new GradientStop(Colors.Transparent, 1)],
                new Point(0, 0), new Point(0, 6)) { MappingMode = BrushMappingMode.Absolute, SpreadMethod = GradientSpreadMethod.Repeat });
        }

        static Brush Frozen(Brush brush)
        {
            brush.Freeze();
            return brush;
        }

        // The selection lives in the view model, which keeps it across a reload; the grid shows it.

        void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(BinGridViewModel.SelectedRows) or "" or null)
                ShowSelection();
        }

        void ShowSelection()
        {
            if (_vm == null || _syncing || Table.SelectedItems.Cast<BinGridRow>().ToHashSet().SetEquals(_vm.SelectedRows))
                return;
            _syncing = true;
            try
            {
                Table.SelectedItems.Clear();
                foreach (var row in _vm.SelectedRows)
                    Table.SelectedItems.Add(row);
                // A slot just added, or kept through an undo, may be off screen.
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
                _vm.SelectedRows = Table.SelectedItems.Cast<BinGridRow>().ToList();
            }
            finally
            {
                _syncing = false;
            }
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

        // Enter in a header field writes it, as leaving the field does.
        void Header_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || e.OriginalSource is not DependencyObject source)
                return;
            var combo = FindParent<ComboBox>(source);
            var binding = combo != null ? combo.GetBindingExpression(ComboBox.TextProperty) : (source as TextBox)?.GetBindingExpression(TextBox.TextProperty);
            binding?.UpdateSource();
        }

        static T? FindParent<T>(DependencyObject? item) where T : DependencyObject
        {
            while (item != null && item is not T)
                item = VisualTreeHelper.GetParent(item);
            return item as T;
        }

        void ClearGraph_Click(object sender, RoutedEventArgs e)
        {
            if (_vm != null)
                _vm.LocomotionGraph = "";
        }

        void EditBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not UIElement box)
                return;
            box.Focus();
            (sender as TextBox)?.SelectAll();
        }

        void ShowUnk_Click(object sender, RoutedEventArgs e) =>
            UnkColumn.Visibility = (sender as MenuItem)?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        // A path cell's menu, shared by the column's cells. The menu item's Tag names the column; the row is the DataContext of the
        // cell the menu opened on.

        static BinGridRow? RowOf(object sender) =>
            ((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget is FrameworkElement { DataContext: BinGridRow row } ? row : null;

        void Browse_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null || RowOf(sender) is not { } row || sender is not MenuItem { Tag: string column })
                return;
            var command = column switch
            {
                "Animation" => _vm.BrowseAnimationCommand,
                "Meta" => _vm.BrowseMetaCommand,
                _ => _vm.BrowseSoundCommand,
            };
            command.Execute(row);
        }

        void Open_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null || RowOf(sender) is not { } row || sender is not MenuItem { Tag: string column })
                return;
            _vm.OpenPathCommand.Execute(column switch { "Animation" => row.Animation, "Meta" => row.Meta, _ => row.Sound });
        }
    }

    /// <summary>A path cell's text: the file name, or "(none)" for an empty path.</summary>
    public sealed class FileNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is string path && path.Length != 0 ? Path.GetFileName(path) : "(none)";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
