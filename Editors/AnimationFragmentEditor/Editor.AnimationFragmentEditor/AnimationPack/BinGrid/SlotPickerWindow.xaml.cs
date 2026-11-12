using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Editors.AnimationFragmentEditor.AnimationPack.BinGrid
{
    /// <summary>The searchable slot list Add slot and Duplicate open. A slot the table already has is shown greyed and cannot be
    /// picked.</summary>
    public partial class SlotPickerWindow : Window
    {
        public sealed record Slot(string Name, bool IsFree);

        readonly ICollectionView _view;
        string? _picked;

        SlotPickerWindow(IEnumerable<string> slots, IReadOnlyCollection<string> taken)
        {
            InitializeComponent();
            var inTable = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _view = CollectionViewSource.GetDefaultView(slots.Select(x => new Slot(x, !inTable.Contains(x))).ToList());
            SlotList.ItemsSource = _view;
            FilterBox.TextChanged += (_, _) =>
            {
                var text = FilterBox.Text.Trim();
                _view.Filter = text.Length == 0 ? null : x => ((Slot)x).Name.Contains(text, StringComparison.OrdinalIgnoreCase);
            };
            Loaded += (_, _) => FilterBox.Focus();
        }

        /// <summary>The slot picked, or null when the window was cancelled.</summary>
        public static string? Pick(IEnumerable<string> slots, IReadOnlyCollection<string> taken)
        {
            var window = new SlotPickerWindow(slots, taken) { Owner = Application.Current?.MainWindow };
            return window.ShowDialog() == true ? window._picked : null;
        }

        void Choose(Slot? slot)
        {
            slot ??= _view.Cast<Slot>().FirstOrDefault(x => x.IsFree);
            if (slot is not { IsFree: true })
                return;
            _picked = slot.Name;
            DialogResult = true;
        }

        void Ok_Click(object sender, RoutedEventArgs e) => Choose(SlotList.SelectedItem as Slot);

        void SlotList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is Slot slot)
                Choose(slot);
        }

        // Down from the filter goes into the list, so the keyboard alone can filter, pick and press Enter.
        void FilterBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Down || SlotList.Items.Count == 0)
                return;
            SlotList.SelectedItem = _view.Cast<Slot>().FirstOrDefault(x => x.IsFree);
            if (SlotList.SelectedItem != null)
            {
                SlotList.ScrollIntoView(SlotList.SelectedItem);
                (SlotList.ItemContainerGenerator.ContainerFromItem(SlotList.SelectedItem) as ListBoxItem)?.Focus();
            }
            e.Handled = true;
        }
    }
}
