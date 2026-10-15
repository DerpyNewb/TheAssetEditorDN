using Microsoft.Extensions.DependencyInjection;

namespace Shared.Core.Events
{
    public interface IAeCommand
    {
        void Execute();
    }

    public interface IAeUndoCommandCommand : IAeCommand
    {
        void Undo();
        /// <summary>
        /// The step again, after an undo. Most commands make their change in Execute, so they run it again; a command whose change
        /// was made before it went on the stack (a gizmo drag) replays it here instead.
        /// </summary>
        void Redo() => Execute();
        string HintText { get; }
        bool IsMutation { get; }
    }

    public interface IUiCommandFactory
    {
        T Create<T>(Action<T>? configure = null) where T : IAeCommand;
        CommandBuilder<T> CreateWithBuilder<T>() where T : IAeUndoCommandCommand;
    }

    public class UiCommandFactory : IUiCommandFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly CommandManager _commandManager;

        public UiCommandFactory(IServiceProvider serviceProvider, CommandManager manager)
        {
            _serviceProvider = serviceProvider;
            _commandManager = manager;
        }

        public T Create<T>(Action<T>? configure = null) where T : IAeCommand
        {
            var instance = _serviceProvider.GetRequiredService<T>();
            configure?.Invoke(instance);
            return instance;
        }

        public CommandBuilder<T> CreateWithBuilder<T>() where T : IAeUndoCommandCommand
        {
            var instance = _serviceProvider.GetRequiredService<T>();
            return new CommandBuilder<T>(_commandManager, instance); ;
        }
    }
}
