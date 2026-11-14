using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimationMeta.Presentation;

namespace Editors.AnimationMeta.MetaEditor.Check
{
    /// <summary>The Check meta window: the problems the last check found in one editor's file. Picking one selects its tag.</summary>
    public partial class MetaCheckViewModel : ObservableObject
    {
        readonly MetaDataEditorViewModel _editor;
        readonly Func<MetaCheckResult> _check;

        [ObservableProperty] List<MetaProblem> _problems = [];
        [ObservableProperty] MetaProblem? _selected;
        [ObservableProperty] string _summary = "";
        [ObservableProperty] string _skipped = "";

        public string Title => $"Check meta - {_editor.DisplayName}";

        public MetaCheckViewModel(MetaDataEditorViewModel editor, Func<MetaCheckResult> check)
        {
            _editor = editor;
            _check = check;
            CheckAgain();
        }

        [RelayCommand]
        public void CheckAgain()
        {
            var result = _check();
            Problems = result.Problems;
            Skipped = string.Join(Environment.NewLine, result.Skipped);
            var tags = _editor.ParsedFile?.Attributes.Count ?? 0;
            Summary = result.Problems.Count == 0
                ? $"No problems found in {Count(tags, "tag")}."
                : $"{Count(result.Problems.Count, "problem")} in {Count(tags, "tag")}. Click one to select its tag.";
        }

        static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

        // The tag is found by reference, since an add, move or delete since the check changes the indices. Undo reads the file
        // again into new tags, so after one the old problems point at nothing.
        partial void OnSelectedChanged(MetaProblem? value)
        {
            if (value == null)
                return;
            var tag = _editor.Tags.FirstOrDefault(x => x._input == value.Tag);
            if (tag != null)
                _editor.SelectedTag = tag;
            else
                Summary = "That tag has changed since the check. Click Check again.";
        }
    }
}
