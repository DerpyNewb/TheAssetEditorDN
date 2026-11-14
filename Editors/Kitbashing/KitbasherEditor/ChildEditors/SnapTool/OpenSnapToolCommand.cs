using System.Windows;
using Editors.KitbasherEditor.Core.MenuBarViews;
using Shared.Core.Misc;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor.ChildEditors.SnapTool
{
    public class OpenSnapToolCommand : IScopedKitbasherUiCommand, IDisposable
    {
        public string ToolTip { get; set; } = "Move the selection onto a bone or onto picked vertices";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey { get; } = null;

        private readonly IAbstractFormFactory<SnapToolWindow> _windowFactory;
        private Window? _windowInstance;

        public OpenSnapToolCommand(IAbstractFormFactory<SnapToolWindow> windowFactory)
        {
            _windowFactory = windowFactory;
        }

        public void Execute()
        {
            if (_windowInstance != null)
            {
                _windowInstance.Activate();
                return;
            }

            _windowInstance = _windowFactory.Create();
            _windowInstance.Closed += OnWindowClosed;
            _windowInstance.Show();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (_windowInstance != null)
                _windowInstance.Closed -= OnWindowClosed;
            _windowInstance = null;
        }

        public void Dispose()
        {
            _windowInstance?.Close();
            _windowInstance = null;
        }
    }
}
