using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Diagnostics;
using Editors.AnimationMeta.MetaEditor.Examples;
using Editors.AnimationMeta.SuperView.Visualisation;
using Shared.Core.Misc;
using Shared.GameFormats.AnimationMeta.Parsing;

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

    public record NewTagItem(string Key, string Description, int Warhammer3Uses, ShownInSuperView.Mark SuperViewMark)
    {
        // Keys are "NAME_VERSION"; descriptions are stored per NAME. The mark is for the type Add creates, the key's first.
        public static List<NewTagItem> All(IMetaDataDatabase database) => database.GetSupportedTypes()
            .Select(key => new NewTagItem(key, database.GetDescriptionSafe(key[..key.LastIndexOf('_')]), database.GetWarhammer3UseCount(key),
                ShownInSuperView.Of(database.GetDefinition(key).First())))
            .ToList();
    }

    class NewTagWindowViewModel : NotifyPropertyChangedImpl
    {
        public NewTagWindowViewModel(List<NewTagItem> items, VanillaExamplesViewModel? examples = null)
        {
            Examples = examples;
            Items = CollectionViewSource.GetDefaultView(items);
            Items.Filter = x => x is NewTagItem item &&
                (item.Key.Contains(FilterText, StringComparison.OrdinalIgnoreCase) || item.Description.Contains(FilterText, StringComparison.OrdinalIgnoreCase));
        }

        public ICollectionView Items { get; }

        string _filterText = "";
        public string FilterText { get => _filterText; set { SetAndNotify(ref _filterText, value); Items.Refresh(); } }

        NewTagItem? _selectedItem;
        public NewTagItem? SelectedItem { get => _selectedItem; set { SetAndNotify(ref _selectedItem, value); _ = Examples?.Show(value?.Key); } }

        /// <summary>How vanilla uses the picked tag; "Insert like this one" adds a tag with an example's values.</summary>
        public VanillaExamplesViewModel? Examples { get; }
    }
}
