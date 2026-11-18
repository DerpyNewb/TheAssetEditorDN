namespace Shared.Core.Misc
{
    /// <summary>
    /// Undo by whole snapshots, for an editor whose document raises no change events. <see cref="Checkpoint"/> takes the document
    /// as it is now and records a step only if it differs from the last snapshot, so it can be called after any gesture that may
    /// have changed something: a mouse button let go, a field left, a command run.
    /// </summary>
    /// <param name="take">The document as it is now.</param>
    /// <param name="same">Whether two snapshots hold the same document.</param>
    /// <param name="restore">Puts a snapshot back. Its second argument is the snapshot taken just after the step's edit, for
    /// showing where the step was made: an undo leaves that snapshot and a redo arrives at it.</param>
    public sealed class SnapshotUndo<T>(Func<T> take, Func<T, T, bool> same, Action<T, T> restore) where T : class
    {
        T? _current;
        bool _restoring;
        readonly Stack<T> _undo = new();
        readonly Stack<T> _redo = new();

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        /// <summary>Forgets every step; the document as it is now is where undo stops.</summary>
        public void Reset()
        {
            _undo.Clear();
            _redo.Clear();
            _current = take();
        }

        /// <summary>The document as it is now becomes the last snapshot without being a step, for a change that is not an edit.</summary>
        public void Rebase() => _current = take();

        /// <summary>Records the document as one step if it changed since the last snapshot. True when it did.</summary>
        public bool Checkpoint()
        {
            // A restore can set off the editor's own change events, and a checkpoint there would push the half-restored document
            // as a step and drop the redo steps.
            if (_restoring)
                return false;
            var now = take();
            var changed = _current != null && !same(now, _current);
            if (changed)
            {
                _undo.Push(_current!);
                _redo.Clear();
            }
            _current = now;
            return changed;
        }

        /// <summary>A change not yet checkpointed is recorded first, so it is what this undoes. True when something was undone.</summary>
        public bool Undo() => Step(_undo, _redo, isUndo: true);

        public bool Redo() => Step(_redo, _undo, isUndo: false);

        bool Step(Stack<T> from, Stack<T> to, bool isUndo)
        {
            Checkpoint();
            if (from.Count == 0)
                return false;
            var leaving = _current!;
            to.Push(leaving);
            var arriving = from.Pop();
            _restoring = true;
            try
            {
                restore(arriving, isUndo ? leaving : arriving);
            }
            finally
            {
                _restoring = false;
            }
            _current = take();
            return true;
        }
    }
}
