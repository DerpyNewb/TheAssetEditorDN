using System.Windows.Input;
using Shared.Ui.Common.MenuSystem;
using Editors.KitbasherEditor.Core.MenuBarViews;

namespace Editors.KitbasherEditor.UiCommands
{
    public class RedoCommand : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Redo (Ctrl+Y)";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Custom;
        public Hotkey? HotKey { get; } = new Hotkey(Key.Y, ModifierKeys.Control);

        private readonly global::Shared.Core.Events.CommandManager _commandExecutor;

        public RedoCommand(global::Shared.Core.Events.CommandManager commandExecutor)
        {
            _commandExecutor = commandExecutor;
        }

        public void Execute() => _commandExecutor.Redo();
    }
}
