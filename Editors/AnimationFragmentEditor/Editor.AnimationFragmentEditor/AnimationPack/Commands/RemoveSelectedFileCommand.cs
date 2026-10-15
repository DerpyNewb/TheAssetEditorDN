using CommonControls.Editors.AnimationPack;
using Shared.Core.Events;

namespace Editors.AnimationFragmentEditor.AnimationPack.Commands
{
    public class RemoveSelectedFileCommand : IAeCommand
    {
        private AnimPackViewModel _editor = null!;

        public void Configure(AnimPackViewModel editor)
        {
            _editor = editor;
        }

        public void Execute()
        {
            var items = _editor.AnimationPackItems.PossibleValues;
            var file = _editor.AnimationPackItems.SelectedItem;
            var at = items.IndexOf(file);
            if (at < 0)
                return;
            items.RemoveAt(at);
            _editor.AnimationPackItems.RefreshFilter();
            _editor.Changed(() => items.Insert(at, file), () => items.Remove(file));
        }
    }

}
