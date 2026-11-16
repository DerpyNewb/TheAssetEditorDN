using System.Windows;
using System.Windows.Controls;
using AvalonDock.Layout;
using Editors.Shared.Core.Common.BaseControl;

namespace Test.Editors.Shared.Core
{
    // An editor's own panes: a side pane takes the editor panel's place as a tab, a pane under the player gets a row of its
    // own beneath the player and leaves the editor panel where it is.
    [Apartment(ApartmentState.STA)]
    public class EditorPaneLayoutTests
    {
        // The editors' default: the 3D view, the player under it, scene content over the editor's panel on the right.
        static LayoutRoot Layout()
        {
            static LayoutAnchorable Pane(string id) => new() { ContentId = id, Title = id, Content = new TextBlock { Text = id } };
            var group = new LayoutAnchorablePaneGroup { Orientation = Orientation.Vertical, DockWidth = new GridLength(400) };
            group.Children.Add(new LayoutAnchorablePane(Pane("SceneContent")));
            group.Children.Add(new LayoutAnchorablePane(Pane("EditorPanel")));
            var left = new LayoutPanel { Orientation = Orientation.Vertical };
            left.Children.Add(new LayoutDocumentPane(new LayoutDocument { ContentId = "GameView", Title = "3D view", Content = new ContentControl() }));
            left.Children.Add(new LayoutAnchorablePane(Pane("AnimationPlayer")) { DockHeight = new GridLength(130) });
            var root = new LayoutPanel { Orientation = Orientation.Horizontal };
            root.Children.Add(left);
            root.Children.Add(group);
            return new LayoutRoot { RootPanel = root };
        }

        static LayoutAnchorable Find(LayoutRoot layout, string id) => layout.Descendents().OfType<LayoutAnchorable>().Single(x => x.ContentId == id);

        static void Place(LayoutRoot layout, params EditorPane[] panes) =>
            EditorPaneLayout.Place(layout, panes, x => new TextBlock { Text = x.ContentId }, x => x);

        [Test]
        public void APaneUnderThePlayer_GetsARowBeneathIt_AndTheEditorPanelStays()
        {
            var layout = Layout();

            Place(layout, new EditorPane("Keyframes", "Keyframes", typeof(TextBlock), new object(), UnderPlayer: true));

            var player = (LayoutAnchorablePane)Find(layout, "AnimationPlayer").Parent;
            var keyframes = (LayoutAnchorablePane)Find(layout, "Keyframes").Parent;
            Assert.That(keyframes, Is.Not.SameAs(player), "a row of its own, not a tab beside the player");
            var column = (LayoutPanel)player.Parent;
            Assert.That(column.Children.IndexOf(keyframes), Is.EqualTo(column.Children.IndexOf(player) + 1));
            Assert.That(Find(layout, "EditorPanel"), Is.Not.Null);
            Assert.That(((TextBlock)Find(layout, "Keyframes").Content).Text, Is.EqualTo("Keyframes"));
        }

        [Test]
        public void SidePanes_TakeTheEditorPanelsPlace()
        {
            var layout = Layout();
            var panelPane = Find(layout, "EditorPanel").Parent;

            Place(layout, new EditorPane("One", "One", typeof(TextBlock), new object()), new EditorPane("Two", "Two", typeof(TextBlock), new object()));

            Assert.That(layout.Descendents().OfType<LayoutAnchorable>().Any(x => x.ContentId == "EditorPanel"), Is.False);
            Assert.That(Find(layout, "One").Parent, Is.SameAs(panelPane));
            Assert.That(Find(layout, "Two").Parent, Is.SameAs(panelPane));
        }
    }
}
