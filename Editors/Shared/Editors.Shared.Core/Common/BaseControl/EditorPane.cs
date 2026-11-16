namespace Editors.Shared.Core.Common.BaseControl
{
    /// <summary>A pane of an editor's own, in place of the single editor panel: the host makes <paramref name="ViewType"/> and
    /// gives it <paramref name="DataContext"/>. <paramref name="Title"/> is a loc key, looked up as <c>{loc:Loc}</c> does. <paramref name="ContentId"/> is what a saved layout knows it by.
    /// <paramref name="UnderPlayer"/> puts it in a row of its own under the animation player instead, and the editor panel stays.</summary>
    public sealed record EditorPane(string ContentId, string Title, Type ViewType, object DataContext, bool UnderPlayer = false);
}
