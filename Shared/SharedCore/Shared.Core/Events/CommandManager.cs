using Shared.Core.ErrorHandling;

namespace Shared.Core.Events
{
    public record CommandStackChangedEvent(string HintText, bool IsMutation);
    public record CommandStackUndoEvent(string HintText);
    public record CommandStackRedoEvent(string HintText);

    public class CommandManager
    {
        protected ILogger _logger;
        private readonly Stack<IAeUndoCommandCommand> _commands = new Stack<IAeUndoCommandCommand>();
        // Undone steps, for redo; a new step clears it.
        private readonly Stack<IAeUndoCommandCommand> _undone = new Stack<IAeUndoCommandCommand>();
        private readonly IEventHub _eventHub;

        public CommandManager(IEventHub eventHub, IScopedLogger scopedLogger)
        {
            _logger = scopedLogger.ForContext<CommandManager>();
            _eventHub = eventHub;
        }

        public void ExecuteCommand(IAeUndoCommandCommand command, bool isUndoable = true)
        {
            if (command == null)
                throw new ArgumentNullException("Command is null");
            _logger.Here().Information($"Executing {command.GetType().Name}");
            try
            {
                command.Execute();
            }
            catch (Exception e)
            {
                // Not recorded: a step that stopped part way has nothing reliable to undo, and undoing what it never did
                // (a divide that never removed the mesh it divides) adds a second copy.
                _logger.Here().Error($"Failed to execute command : {e}");
                return;
            }

            if (isUndoable)
            {
                _commands.Push(command);
                _undone.Clear();
                _eventHub.Publish(new CommandStackChangedEvent(command.HintText, command.IsMutation));
            }
        }

        public bool CanUndo() => _commands.Count != 0;

        /// <summary>The step the next undo takes back, so an edit still under way can fold into it rather than add another.</summary>
        public IAeUndoCommandCommand? Peek() => _commands.Count == 0 ? null : _commands.Peek();

        public bool CanRedo() => _undone.Count != 0;

        /// <param name="keepForRedo">False for an undo that takes back a preview about to be replaced, which is no step to redo.</param>
        public void Undo(bool keepForRedo = true)
        {
            if (CanUndo())
            {
                var command = _commands.Pop();
                _logger.Here().Information($"Undoing {command.GetType().Name}");
                try
                {
                    command.Undo();
                    // Only a clean undo can be redone; redoing over a half-undone step would apply it twice.
                    if (keepForRedo)
                        _undone.Push(command);
                }
                catch (Exception e)
                {
                    _logger.Here().Error($"Failed to Undoing command : {e}");
                    _undone.Clear();
                }

                var undoText = GetUndoHint();
                _eventHub.Publish(new CommandStackUndoEvent(undoText));
            }
        }

        public void Redo()
        {
            if (!CanRedo())
                return;

            var command = _undone.Pop();
            _logger.Here().Information($"Redoing {command.GetType().Name}");
            try
            {
                command.Redo();
            }
            catch (Exception e)
            {
                _logger.Here().Error($"Failed to redo command : {e}");
                _undone.Clear();
                _eventHub.Publish(new CommandStackChangedEvent(command.HintText, command.IsMutation));
                return;
            }
            _commands.Push(command);

            _eventHub.Publish(new CommandStackChangedEvent(command.HintText, command.IsMutation));
            _eventHub.Publish(new CommandStackRedoEvent(command.HintText));
        }

        public string GetUndoHint()
        {
            if (!CanUndo())
                return "No items to undo";

            return _commands.Peek().HintText;
        }
    }
}
