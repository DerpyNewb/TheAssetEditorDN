using Shared.Core.Events;

namespace Editors.KitbasherEditor.Commands
{
    /// <summary>Several commands as one undo step. Undo takes them back last first, so one Undo restores what all of them changed.</summary>
    public class CompoundCommand : IAeUndoCommandCommand
    {
        List<IAeUndoCommandCommand> _steps = [];

        public string HintText => _steps.Count == 0 ? "" : _steps[^1].HintText;
        public bool IsMutation => _steps.Any(x => x.IsMutation);

        public void Configure(IEnumerable<IAeUndoCommandCommand> steps) => _steps = steps.ToList();

        public void Execute() => Run(x => x.Execute());
        public void Redo() => Run(x => x.Redo());

        public void Undo()
        {
            for (var i = _steps.Count - 1; i >= 0; i--)
                _steps[i].Undo();
        }

        // A step that throws leaves the ones before it undone: the command manager records nothing for a step that fails, so
        // what they changed could not be undone otherwise.
        void Run(Action<IAeUndoCommandCommand> run)
        {
            for (var i = 0; i < _steps.Count; i++)
            {
                try
                {
                    run(_steps[i]);
                }
                catch
                {
                    for (var j = i - 1; j >= 0; j--)
                        _steps[j].Undo();
                    throw;
                }
            }
        }
    }
}
