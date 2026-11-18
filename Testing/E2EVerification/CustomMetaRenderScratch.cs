using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Moq;
using Shared.Core.Commands;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.Ui.BaseDialogs.StandardDialog;
using Shared.Ui.Common;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.E2EVerification
{
    // Scratch, not committed: SuperView's panel on Throt's ATTACK_5 with Throt's pack standing in for CA's, and the save's
    // question, drawn offscreen to PNGs in SCRATCH_OUT.
    internal class CustomMetaRenderScratch
    {
        class ScratchApp : Application, IAssetEditorMain
        {
            public IServiceProvider ServiceProvider { get; init; } = null!;
        }

        static void Pump(int ms)
        {
            var until = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < until)
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }

        static void Save(Window window, string path)
        {
            window.UpdateLayout();
            Pump(300);
            var bmp = new RenderTargetBitmap((int)Math.Max(window.ActualWidth, 1), (int)Math.Max(window.ActualHeight, 1), 96, 96, PixelFormats.Pbgra32);
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen())
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)), null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
            bmp.Render(bg);
            bmp.Render(window);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            using var file = File.Create(path);
            png.Save(file);
        }

        // The theme's window brushes, which CustomWindowStyle and the dock give the real windows and a plain one lacks.
        static Window Offscreen(object content, double w, double h) =>
            new()
            {
                Content = content, Width = w, Height = h, Left = -20000, Top = 0, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
                Background = (Brush)Application.Current.FindResource("Window.Static.Background"),
                Foreground = (Brush)Application.Current.FindResource("Window.Static.Foreground"),
            };

        [Test, Apartment(ApartmentState.STA), Explicit]
        public void Render()
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            var outDir = Environment.GetEnvironmentVariable("SCRATCH_OUT")!;
            Directory.CreateDirectory(outDir);
            var runner = new AssetEditorTestRunner();
            var loc = (LocalizationManager)runner.ServiceProvider.GetService(typeof(LocalizationManager))!;
            loc.LoadLanguage("en");
            var app = new ScratchApp { ServiceProvider = runner.ServiceProvider, ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var dict in new[] { "Themes/ColourDictionaries/DarkTheme.xaml", "Themes/ControlColours.xaml", "Themes/Controls.xaml" })
                app.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(new Uri($"/AssetEditor;component/{dict}", UriKind.Relative)));

            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            var pfs = runner.PackFileService;
            pfs.GetAllPackfileContainers().Single(x => x.Name.Contains("Throt")).IsCaPackFile = true;
            var superView = (Editors.AnimationMeta.SuperView.SuperViewViewModel)runner.CommandFactory.Create<OpenEditorCommand>()
                .Execute(pfs.FindFile(@"variantmeshes\variantmeshdefinitions\skv_throt.variantmeshdefinition")!, EditorEnums.SuperView_Editor)!;
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == "animations/database/battle/bin/hu17_dlc16_throt.bin");
            selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "ATTACK_5");
            superView.SelectedTabControllerIndex = 1;

            var panel = Offscreen(new Editors.AnimationMeta.SuperView.EditorView { DataContext = superView }, 420, 900);
            panel.Show();
            Save(panel, Path.Combine(outDir, "custommeta_vanilla.png"));

            // The slot grid, as the pane under the player shows it, then with an edit in it: bold, with Undo and Save lit.
            var grid = Offscreen(new Editors.AnimationMeta.SuperView.SlotGridView { DataContext = superView.SlotGrid }, 1180, 330);
            grid.Show();
            Save(grid, Path.Combine(outDir, "slotgrid.png"));
            var attackRow = superView.SlotGrid.SelectedRow!;
            attackRow.Weight += 1.5f;
            attackRow.Weapon3 = !attackRow.Weapon3;
            Save(grid, Path.Combine(outDir, "slotgrid_edited.png"));
            superView.SlotGrid.UndoCommand.Execute(null);
            superView.SlotGrid.UndoCommand.Execute(null);
            grid.Close();

            // The question a save asks, with the words SaveCommand writes.
            string? message = null;
            IReadOnlyList<string>? choices = null;
            runner.Dialogs.Setup(x => x.ShowChoiceBox(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .Callback<string, string, IReadOnlyList<string>>((m, _, c) => (message, choices) = (m, c)).Returns(2);
            var field = superView.MetaEditor.Tags.SelectMany(x => x.Variables).First(x => !x.IsReadOnly && x.Scrub == Shared.Ui.Common.Behaviors.ScrubMode.Integer);
            field.ValueAsString = (int.Parse(field.ValueAsString) + 1).ToString();
            superView.Save();
            var styled = StandardDialogs.ChoiceWindow(message!, "Save meta", choices!, _ => { });
            var content = styled.Content;
            styled.Content = null;
            var question = Offscreen(content, 520, 170);
            question.Show();
            Save(question, Path.Combine(outDir, "custommeta_question.png"));
            question.Close();

            // A slot with no meta shows New meta; a typed path no pack has says so.
            superView.SlotMeta = "";
            superView.Save();
            Save(panel, Path.Combine(outDir, "custommeta_newmeta.png"));
            superView.SlotMeta = "animations/no/such.anm.meta";
            Save(panel, Path.Combine(outDir, "custommeta_typo.png"));
            panel.Close();
            TestContext.Out.WriteLine($"sources: [{superView.PersistentMetaSource}] [{superView.MetaSource}] [{superView.SlotMetaSource}] noMeta={superView.SlotHasNoMeta}");
        }
    }
}
