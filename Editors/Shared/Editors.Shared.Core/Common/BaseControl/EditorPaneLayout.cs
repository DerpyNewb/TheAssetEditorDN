using System.Windows;
using AvalonDock.Layout;

namespace Editors.Shared.Core.Common.BaseControl
{
    /// <summary>Puts an editor's own panes into the host's dock layout.</summary>
    public static class EditorPaneLayout
    {
        /// <summary>
        /// Side panes go where the single editor panel is, as tabs, and the panel goes; panes under the player get one row of
        /// their own beneath it. They go in before the panel comes out: a pane left empty is removed from the layout.
        /// </summary>
        public static void Place(LayoutRoot layout, IReadOnlyList<EditorPane> panes, Func<EditorPane, object> view, Func<string, string> title)
        {
            LayoutAnchorable Make(EditorPane x) => new() { ContentId = x.ContentId, Title = title(x.Title), Content = view(x) };

            var under = panes.Where(x => x.UnderPlayer).ToList();
            if (under.Count > 0)
            {
                var player = (LayoutAnchorablePane)layout.Descendents().OfType<LayoutAnchorable>().Single(x => x.ContentId == "AnimationPlayer").Parent;
                var column = (LayoutPanel)player.Parent;
                var row = new LayoutAnchorablePane { DockHeight = new GridLength(170) };
                foreach (var pane in under)
                    row.Children.Add(Make(pane));
                column.Children.Insert(column.Children.IndexOf(player) + 1, row);
            }

            var side = panes.Where(x => x.UnderPlayer == false).ToList();
            if (side.Count == 0)
                return;
            var single = layout.Descendents().OfType<LayoutAnchorable>().Single(x => x.ContentId == "EditorPanel");
            var parent = (LayoutAnchorablePane)single.Parent;
            foreach (var pane in side)
                parent.Children.Add(Make(pane));
            parent.RemoveChild(single);
        }
    }
}
