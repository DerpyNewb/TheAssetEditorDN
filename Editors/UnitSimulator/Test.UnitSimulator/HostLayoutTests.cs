using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AnimationEditor.Common.BaseControl;
using Editors.Shared.Core.Common.BaseControl;
using Editors.UnitSimulator;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.UnitSimulator
{
    // The 3D editors' right column: the scene content above, the editor's own panel below. The scene content took all the height
    // its parts wanted, so each part added squeezed the panel under it, and the bar between them only looked like a splitter.
    public class HostLayoutTests
    {
        static UnitSimulatorViewModel Open()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            return UnitSimulatorAppTests.Open(runner);
        }

        static IEnumerable<DependencyObject> Visuals(DependencyObject root) =>
            Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(root)).Select(i => VisualTreeHelper.GetChild(root, i)).SelectMany(x => Visuals(x).Prepend(x));

        static void Lay(FrameworkElement view)
        {
            view.Measure(new Size(1600, 900));
            view.Arrange(new Rect(0, 0, 1600, 900));
            view.UpdateLayout();
        }

        static (EditorHostView View, Expander Scene, GridSplitter Splitter, ContentControl Panel) Show(UnitSimulatorViewModel sim)
        {
            AppResources.Load();
            var view = new EditorHostView { DataContext = sim };
            Lay(view);
            var splitter = view.FindName("SceneSplitter") as GridSplitter;
            Assert.That(splitter, Is.Not.Null, "no splitter between the scene content and the editor's panel");
            return (view, (Expander)view.FindName("SceneContent"), splitter!, (ContentControl)view.FindName("EditorPanel"));
        }

        static double Column(Expander scene) => ((FrameworkElement)VisualTreeHelper.GetParent(scene)).ActualHeight;

        [Test, Apartment(ApartmentState.STA)]
        public void TheEditorsPanel_KeepsItsShare_AndTheSceneContentScrolls()
        {
            var (_, scene, splitter, panel) = Show(Open());

            Assert.That(panel.ActualHeight, Is.EqualTo((Column(scene) - splitter.ActualHeight) * 3 / 5).Within(2));
            var list = Visuals(scene).OfType<ScrollViewer>().First();
            Assert.That(list.ExtentHeight, Is.GreaterThan(list.ViewportHeight), "the scene content's parts do not fit its share, so it scrolls");
        }

        // Dragged by the 120 pixels the mouse moved, and the split is kept on the editor, as the column widths are.
        [Test, Apartment(ApartmentState.STA)]
        public void DraggingTheSplitter_MovesTheSplit_AndTheEditorKeepsIt()
        {
            var sim = Open();
            var (view, scene, splitter, panel) = Show(sim);
            var (sceneBefore, panelBefore) = (scene.ActualHeight, panel.ActualHeight);
            var kept = sim.SceneContentHeight;

            splitter.RaiseEvent(new DragStartedEventArgs(0, 0));
            splitter.RaiseEvent(new DragDeltaEventArgs(0, 120));
            splitter.RaiseEvent(new DragCompletedEventArgs(0, 120, false));
            Lay(view);

            Assert.That((scene.ActualHeight - sceneBefore, panelBefore - panel.ActualHeight), Is.EqualTo((120.0, 120.0)).Using<(double, double)>((a, b) =>
                Math.Abs(a.Item1 - b.Item1) <= 2 && Math.Abs(a.Item2 - b.Item2) <= 2));
            Assert.That(sim.SceneContentHeight, Is.Not.EqualTo(kept));
        }

        // Collapsed, the scene content is its header and the panel takes the rest; opened again, it has the height it was left at.
        [Test, Apartment(ApartmentState.STA)]
        public void CollapsingTheSceneContent_GivesThePanelTheColumn_AndOpeningItBringsBackItsHeight()
        {
            var sim = Open();
            sim.SceneContentHeight = new GridLength(300);
            var (view, scene, splitter, panel) = Show(sim);

            scene.IsExpanded = false;
            Lay(view);
            Assert.That(splitter.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(scene.ActualHeight, Is.EqualTo(scene.DesiredSize.Height).Within(1), "the row kept its height, not only the header's");
            Assert.That(panel.ActualHeight, Is.EqualTo(Column(scene) - scene.ActualHeight).Within(2));

            scene.IsExpanded = true;
            Lay(view);
            Assert.That(scene.ActualHeight, Is.EqualTo(300).Within(1));
        }

        // A part is taller than a small share; scrolled by whole parts, the list could only show its top. By pixel, its end is reached.
        [Test, Apartment(ApartmentState.STA)]
        public void APartTallerThanTheSceneContentsShare_ScrollsToItsEnd()
        {
            var sim = Open();
            sim.SceneContentHeight = new GridLength(150);
            var (view, scene, _, _) = Show(sim);
            var list = Visuals(scene).OfType<ScrollViewer>().First();
            var last = Visuals(list).OfType<ListViewItem>().Last();
            Assert.That(last.ActualHeight, Is.GreaterThan(list.ViewportHeight), "no part taller than the share, so this proves nothing");

            list.ScrollToEnd();
            Lay(view);
            var bottom = last.TransformToAncestor(list).Transform(new Point(0, last.ActualHeight)).Y;
            Assert.That(bottom, Is.LessThanOrEqualTo(list.ViewportHeight + 1));
        }
    }
}
