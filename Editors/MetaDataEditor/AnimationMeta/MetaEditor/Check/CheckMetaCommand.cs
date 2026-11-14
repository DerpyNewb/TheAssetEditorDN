using System.Windows;
using Editors.AnimationMeta.Presentation;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.MetaEditor.Check
{
    /// <summary>Opens the Check meta window for an editor, modeless so the tags can be fixed with it open, and one per editor:
    /// clicking the button again checks again and brings it forward.</summary>
    public class CheckMetaCommand : IAeCommand
    {
        static readonly Dictionary<MetaDataEditorViewModel, MetaCheckWindow> s_open = [];

        readonly IPackFileService _packFileService;
        readonly IMetaDataDatabase _database;
        MetaDataEditorViewModel _editor = null!;

        public CheckMetaCommand(IPackFileService packFileService, IMetaDataDatabase database)
        {
            _packFileService = packFileService;
            _database = database;
        }

        public void Configure(MetaDataEditorViewModel editor) => _editor = editor;

        public void Execute()
        {
            if (s_open.TryGetValue(_editor, out var open))
            {
                ((MetaCheckViewModel)open.DataContext).CheckAgain();
                open.Activate();
                return;
            }

            var editor = _editor;
            var window = new MetaCheckWindow { DataContext = new MetaCheckViewModel(editor, () => Check(editor)), Owner = Application.Current?.MainWindow };
            window.Closed += (_, _) => s_open.Remove(editor);
            s_open[editor] = window;
            window.Show();
        }

        public MetaCheckResult Check(MetaDataEditorViewModel editor)
        {
            if (editor.ParsedFile == null)
                return new MetaCheckResult([], ["No meta file is open."]);
            return MetaChecker.Check(editor.ParsedFile, _database, editor.CheckScene?.Invoke(), _packFileService);
        }
    }
}
