using CommonControls.BaseDialogs;
using CommonControls.Editors.AnimationPack;
using Shared.Core.Events;

namespace Editors.AnimationFragmentEditor.AnimationPack.Commands
{
    public class RenameSelectedFileCommand : IAeCommand
    {
        private AnimPackViewModel _editor = null!;

        public void Configure(AnimPackViewModel editor)
        {
            _editor = editor;
        }

        public void Execute()
        {
            var animFile = _editor.AnimationPackItems.PossibleValues.FirstOrDefault(file => file == _editor.AnimationPackItems.SelectedItem);
            if (animFile == null)
                return;

            var oldName = animFile.FileName;
            var window = new TextInputWindow("Rename Anim File", oldName);
            if (window.ShowDialog() != true || window.TextValue == oldName)
                return;
            var newName = window.TextValue;
            animFile.FileName = newName;
            _editor.Changed(() => animFile.FileName = oldName, () => animFile.FileName = newName);

            // way to refresh the view
            _editor.AnimationPackItems.RefreshFilter();
        }
    }

}
