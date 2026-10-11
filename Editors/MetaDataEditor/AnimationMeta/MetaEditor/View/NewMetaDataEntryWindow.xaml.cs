using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Diagnostics;
using Shared.Core.Misc;

namespace Editors.AnimationMeta.Presentation.View
{
    public partial class NewMetaDataEntryWindow : Window
    {
        public NewMetaDataEntryWindow()
        {
            InitializeComponent();
        }

        private void OnOkClick(object sender, RoutedEventArgs e) => HandleOnClick();

        private void HandleOnClick()
        {
            var model = DataContext as NewTagWindowViewModel;
            Guard.IsNotNull(model, $"{nameof(model)} - DataContext must be of type {nameof(NewTagWindowViewModel)}");

            if (model.SelectedItem == null)
            {
                MessageBox.Show("Nothing selected");
                return;
            }

            DialogResult = true;
            Close();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var model = DataContext as NewTagWindowViewModel;
            Guard.IsNotNull(model, $"{nameof(model)} - DataContext must be of type {nameof(NewTagWindowViewModel)}");
            if (model.SelectedItem != null)
                HandleOnClick();
        }
    }

    record NewTagItem(string Key, string Description, int Warhammer3Uses);

    class NewTagWindowViewModel : NotifyPropertyChangedImpl
    {
        public NewTagWindowViewModel(List<NewTagItem> items)
        {
            Items = CollectionViewSource.GetDefaultView(items);
            Items.Filter = x => x is NewTagItem item &&
                (item.Key.Contains(FilterText, StringComparison.OrdinalIgnoreCase) || item.Description.Contains(FilterText, StringComparison.OrdinalIgnoreCase));
        }

        public ICollectionView Items { get; }

        string _filterText = "";
        public string FilterText { get => _filterText; set { SetAndNotify(ref _filterText, value); Items.Refresh(); } }

        NewTagItem? _selectedItem;
        public NewTagItem? SelectedItem { get => _selectedItem; set { SetAndNotify(ref _selectedItem, value); } }
    }
}
