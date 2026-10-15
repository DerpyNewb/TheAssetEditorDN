using System.Collections.Generic;
using System.Text.Json;
using Editors.Audio.AudioEditor.Events;
using Editors.Audio.AudioEditor.Presentation.Shared.Models;
using Editors.Audio.Shared.AudioProject.Models;
using Shared.Core.Events;

namespace Editors.Audio.AudioEditor.Core
{
    public interface IAudioProjectUndoService
    {
        /// <summary>Call before a change to the Audio Project, so it can be undone.</summary>
        void BeforeChange();
        bool Undo();
        bool Redo();
    }

    /// <summary>
    /// Undo for the Audio Project: the whole project, as JSON, before each change to it. An undo puts it back whole and shows it
    /// again with the same node selected. What JSON skips (the [JsonIgnore] fields) is the compiler's, worked out again on each compile.
    /// </summary>
    public class AudioProjectUndoService : IAudioProjectUndoService
    {
        // ponytail: a whole-project copy per step, the last 50 kept; record each row's change instead if projects grow large.
        const int Kept = 50;

        readonly IAudioEditorStateService _state;
        readonly IEventHub _eventHub;
        readonly List<string> _undo = [];
        readonly List<string> _redo = [];
        bool _restoring;

        public AudioProjectUndoService(IAudioEditorStateService state, IEventHub eventHub)
        {
            _state = state;
            _eventHub = eventHub;
            // Another project opened: its steps are not this one's.
            eventHub.Register<AudioProjectLoadedEvent>(this, _ =>
            {
                if (_restoring)
                    return;
                _undo.Clear();
                _redo.Clear();
            });
        }

        public void BeforeChange()
        {
            if (_state.AudioProject == null)
                return;
            // A change made of others (an edit is a remove) is one step: the inner call finds the project as the outer left it.
            var now = Snapshot();
            if (_undo.Count != 0 && _undo[^1] == now)
                return;
            Push(_undo, now);
            _redo.Clear();
        }

        public bool Undo() => Step(_undo, _redo);
        public bool Redo() => Step(_redo, _undo);

        bool Step(List<string> from, List<string> to)
        {
            if (from.Count == 0 || _state.AudioProject == null)
                return false;

            var json = from[^1];
            from.RemoveAt(from.Count - 1);
            Push(to, Snapshot());

            var selected = PathOf(_state.SelectedAudioProjectExplorerNode);
            _restoring = true;
            try
            {
                _state.StoreAudioProject(JsonSerializer.Deserialize<AudioProjectFile>(json)!);
                _eventHub.Publish(new AudioProjectLoadedEvent());
            }
            finally
            {
                _restoring = false;
            }
            _eventHub.Publish(new AudioProjectRestoredEvent(selected));
            return true;
        }

        string Snapshot() => JsonSerializer.Serialize(_state.AudioProject);

        static void Push(List<string> stack, string json)
        {
            stack.Add(json);
            if (stack.Count > Kept)
                stack.RemoveAt(0);
        }

        static List<string> PathOf(AudioProjectTreeNode? node)
        {
            var path = new List<string>();
            for (var at = node; at != null; at = at.Parent)
                path.Insert(0, at.Name);
            return path;
        }
    }
}
