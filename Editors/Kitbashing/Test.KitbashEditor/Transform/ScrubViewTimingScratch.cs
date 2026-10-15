using System.Diagnostics;
using System.Windows;
using Editors.KitbasherEditor.ViewModels;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using KitbasherEditor.ViewModels.MenuBarViews;
using KitbasherEditor.Views;
using Shared.Core.Commands;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;
using static KitbasherEditor.ViewModels.MenuBarViews.TransformToolViewModel;

namespace Test.KitbashEditor.Transform
{
    // Scratch: a scrub step with the real Kitbash view laid out. Not committed.
    public class ScrubViewTimingScratch
    {
        [Test, Apartment(ApartmentState.STA)]
        public void TimeAScrubWithTheView()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(TestFiles.KarlPackFile, true);
            runner.CommandFactory.Create<OpenEditorCommand>().Execute(runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);
            T S<T>() where T : notnull => runner.GetRequiredServiceInCurrentEditorScope<T>();

            AppResources.Load();
            var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "AssetEditor", "debug", "AssetEditor.dll"));
            System.Reflection.Assembly.LoadFrom(root);
            foreach (var dict in new[] { "Themes/ColourDictionaries/DarkTheme.xaml", "Themes/ControlColours.xaml", "Themes/Controls.xaml" })
                Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(new Uri($"/AssetEditor;component/{dict}", UriKind.Relative)));
            Application.Current.Resources["ViewTemplateDataSelector"] = new Shared.Ui.Common.DataTemplates.ViewTemplateDataSelector();
            var view = new KitbasherView { DataContext = S<KitbasherViewModel>() };
            void Lay() { view.Measure(new Size(1600, 900)); view.Arrange(new Rect(0, 0, 1600, 900)); view.UpdateLayout(); }
            Lay();

            var main = S<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            var mesh = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(main).First();
            ((ObjectSelectionState)S<SelectionManager>().CreateSelectionSate(GeometrySelectionMode.Object, null!)).ModifySelection([mesh], false);
            Lay();
            var tool = S<TransformToolViewModel>();
            tool.SetMode(TransformMode.Rotate);
            tool.Vector3.Y.Value = 1;
            Lay();

            var sw = Stopwatch.StartNew();
            for (var i = 2; i < 42; i++)
            {
                tool.Vector3.Y.Value = i;
                Lay();
            }
            TestContext.Out.WriteLine($"step with the view: {sw.Elapsed.TotalMilliseconds / 40:F2} ms");
        }
    }
}
