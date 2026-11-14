using System.ComponentModel;
using System.Windows;
using Editors.AnimationMeta.Presentation;
using Shared.Core.Events;

namespace Editors.AnimationMeta.MetaEditor.Examples
{
    /// <summary>Opens a window of vanilla examples that follows the tag selected in an editor. It is modeless, so it stays open
    /// while tags are picked, and one per editor: the button brings an open one forward.</summary>
    public class OpenVanillaExamplesCommand : IAeCommand
    {
        static readonly Dictionary<MetaDataEditorViewModel, VanillaExamplesWindow> s_open = [];

        readonly VanillaMetaExamples _examples;
        MetaDataEditorViewModel _editor = null!;

        public OpenVanillaExamplesCommand(VanillaMetaExamples examples) => _examples = examples;

        public void Configure(MetaDataEditorViewModel editor) => _editor = editor;

        public void Execute()
        {
            if (s_open.TryGetValue(_editor, out var open))
            {
                open.Activate();
                return;
            }

            var editor = _editor;
            var window = new VanillaExamplesWindow { DataContext = Follow(editor, out var stop), Owner = Application.Current?.MainWindow };
            window.Title = $"Vanilla examples - {editor.DisplayName}";
            window.Closed += (_, _) =>
            {
                stop();
                s_open.Remove(editor);
            };
            s_open[editor] = window;
            window.Show();
        }

        /// <summary>The examples of the editor's selected tag, shown again whenever another tag is selected, whose button copies
        /// the picked example into the selected tag.</summary>
        public VanillaExamplesViewModel Follow(MetaDataEditorViewModel editor, out Action stop)
        {
            var model = new VanillaExamplesViewModel(_examples) { Used = x => CopyInto(editor, x) };
            PropertyChangedEventHandler follow = (_, e) =>
            {
                if (e.PropertyName == nameof(MetaDataEditorViewModel.SelectedTag))
                    _ = model.Show(KeyOf(editor));
            };
            editor.PropertyChanged += follow;
            stop = () => editor.PropertyChanged -= follow;
            _ = model.Show(KeyOf(editor));
            return model;
        }

        static string? KeyOf(MetaDataEditorViewModel editor) => editor.SelectedTag?._input is { } tag ? $"{tag.Name}_{tag.Version}" : null;

        /// <summary>Puts a tag with the example's values in place of the selected one, which is then selected again. A tag of
        /// another name or version is left alone: the window lists the selected tag's examples, so that means it changed since.</summary>
        public void CopyInto(MetaDataEditorViewModel editor, VanillaExample example)
        {
            var index = editor.SelectedTag == null ? -1 : editor.Tags.IndexOf(editor.SelectedTag);
            if (editor.ParsedFile == null || index < 0 || KeyOf(editor) != example.Key)
                return;
            editor.ParsedFile.Attributes[index] = _examples.Create(example);
            editor.UpdateView();
            editor.SelectedTag = editor.Tags[index];
        }
    }
}
