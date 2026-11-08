namespace Editors.Shared.Core.Common.BaseControl
{
    /// <summary>A pane of an editor's own, in place of the single editor panel: the host makes <paramref name="ViewType"/> and
    /// gives it <paramref name="DataContext"/>. <paramref name="ContentId"/> is what a saved layout knows it by.</summary>
    public sealed record EditorPane(string ContentId, string Title, Type ViewType, object DataContext);
}
