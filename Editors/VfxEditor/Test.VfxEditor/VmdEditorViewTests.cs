using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Editors.VfxEditor.VariantMesh;

namespace Test.VfxEditor
{
    // The real view, hosted off-screen in the app's theme, driven the way a person types into the grid.
    class VmdEditorViewTests
    {
        [Test, Apartment(ApartmentState.STA)]
        public void The_view_shows_the_rows_and_takes_a_typed_edit()
        {
            var (_, editor) = VmdEditorAppTests.Open(VmdFixtures.Unit);
            var (window, grid) = Host(editor);
            try
            {
                Assert.That(grid.Items.Count, Is.EqualTo(3));
                Type(grid, 1, "Path", "b\\axe.wsmodel");
                grid.CommitEdit(DataGridEditingUnit.Row, true);
                Pump();

                Assert.That(Encoding.UTF8.GetString(editor.Document!.Save()), Is.EqualTo(VmdFixtures.Unit.Replace("a\\sword.wsmodel", "b\\axe.wsmodel")));
            }
            finally
            {
                window.Close();
            }
        }

        // Ctrl+S does not take the focus from a cell being edited, so Save writes the cell first.
        [Test, Apartment(ApartmentState.STA)]
        public void Save_writes_a_cell_still_being_edited()
        {
            var (runner, editor) = VmdEditorAppTests.Open(VmdFixtures.Unit);
            var (window, grid) = Host(editor);
            try
            {
                Type(grid, 1, "Tags", "audio_melee_weapon_type:axe");
                editor.SaveFileCommand.Execute(null);

                var saved = runner.PackFileService.FindFile(VmdEditorAppTests.FilePath, runner.PackFileService.GetEditablePack())!.DataSource.ReadData();
                Assert.That(Encoding.UTF8.GetString(saved), Does.Contain("<META_DATA>audio_melee_weapon_type:axe</META_DATA>"));
            }
            finally
            {
                window.Close();
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void A_file_that_does_not_parse_shows_why_instead_of_a_grid()
        {
            var (_, editor) = VmdEditorAppTests.Open("<VARIANT_MESH><SLOT name=\"a\"></VARIANT_MESH>");
            var (window, grid) = Host(editor);
            try
            {
                Assert.That(grid.IsVisible, Is.False);
                Assert.That(Descendants(window).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("did not parse")), Is.True);
            }
            finally
            {
                window.Close();
            }
        }

        // The toggle at the top right, as in the bin grid: the file's text in the shared text editor, and what is typed there goes back
        // into the file when the grid comes back.
        [Test, Apartment(ApartmentState.STA)]
        public void Show_as_xml_swaps_the_grid_for_the_text_and_back()
        {
            var (_, editor) = VmdEditorAppTests.Open(VmdFixtures.Unit);
            var (window, grid) = Host(editor);
            try
            {
                var toggle = Descendants(window).OfType<ToggleButton>().Single(t => t.Content as string == "Show as XML");
                toggle.IsChecked = true;
                Pump();
                var text = Descendants(window).OfType<ICSharpCode.AvalonEdit.TextEditor>().Single();
                Assert.That((grid.IsVisible, text.IsVisible, text.Text), Is.EqualTo((false, true, VmdFixtures.Unit)));

                // Text that is not a variant mesh: the toggle stays on and the text stays up.
                text.Document.Text = "<VARIANT_MESH>";
                toggle.IsChecked = false;
                Pump();
                Assert.That((toggle.IsChecked, text.IsVisible), Is.EqualTo((true, true)));

                text.Document.Text = VmdFixtures.Unit.Replace("a\\sword.wsmodel", "b\\axe.wsmodel");
                toggle.IsChecked = false;
                Pump();
                Assert.That((grid.IsVisible, text.IsVisible), Is.EqualTo((true, false)));
                Assert.That(editor.Rows[1].Path, Is.EqualTo("b\\axe.wsmodel"));
            }
            finally
            {
                window.Close();
            }
        }

        static (Window Window, DataGrid Grid) Host(VmdEditorViewModel editor)
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            var window = new Window { Width = 1400, Height = 700, Left = -20000, Top = -20000, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
            foreach (var theme in new[] { "ColourDictionaries/DarkTheme.xaml", "ControlColours.xaml", "Controls.xaml" })
                window.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(new Uri("/AssetEditor;component/Themes/" + theme, UriKind.Relative)));
            // The shared text editor (Show as XML) reads its combo box template while it is built, before the view is in the window, so
            // from the app's resources, which the test has no App.xaml to give.
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources["ComboBoxTemplate"] = window.Resources["ComboBoxTemplate"];
            window.Content = new VmdEditorView { DataContext = editor };
            window.Show();
            Pump();
            return (window, Descendants(window).OfType<DataGrid>().Single());
        }

        /// <summary>Opens the cell for editing and types into its text box, leaving the edit open.</summary>
        static void Type(DataGrid grid, int row, string column, string text)
        {
            var target = grid.Columns.Single(c => c.Header as string == column);
            grid.SelectedItem = grid.Items[row];
            grid.CurrentCell = new DataGridCellInfo(grid.Items[row], target);
            Assert.That(grid.BeginEdit(), Is.True, $"{column} on row {row} did not open for editing");
            Pump();
            var box = Descendants(target.GetCellContent(grid.Items[row])!).OfType<TextBox>().FirstOrDefault()
                ?? (TextBox)target.GetCellContent(grid.Items[row])!;
            box.Text = text;
        }

        static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
        }

        static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                yield return child;
                foreach (var descendant in Descendants(child))
                    yield return descendant;
            }
        }
    }
}
