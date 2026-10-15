using System.Collections.Generic;

namespace Editors.Audio.AudioEditor.Events
{
    /// <summary>An undo or redo put the Audio Project back and the explorer was rebuilt; the node at this path of names was selected.</summary>
    public record AudioProjectRestoredEvent(List<string> SelectedNodePath);
}
