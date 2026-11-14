using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Editors.KitbasherEditor.UiCommands;
using Editors.KitbasherEditor.ViewModels;
using Shared.Core.Commands;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.ToolCreation;

namespace Editors.Ipc
{
    public class ExternalFileOpenExecutor : IExternalFileOpenExecutor
    {
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly IScopeRepository _scopeRepository;
        private readonly IEditorManager _editorManager;
        private readonly IEditorDatabase _editorDatabase;
        private readonly IPackFileService _packFileService;

        public ExternalFileOpenExecutor(IUiCommandFactory uiCommandFactory, IScopeRepository scopeRepository, IEditorManager editorManager,
            IEditorDatabase editorDatabase, IPackFileService packFileService)
        {
            _uiCommandFactory = uiCommandFactory;
            _scopeRepository = scopeRepository;
            _editorManager = editorManager;
            _editorDatabase = editorDatabase;
            _packFileService = packFileService;
        }

        public async Task<string?> OpenAsync(PackFile file, bool bringToFront, bool openInExistingKitbashTab, EditorEnums? openWith, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var app = Application.Current;
            if (app?.Dispatcher == null || app.Dispatcher.CheckAccess())
                return OpenOnUiThread(file, bringToFront, openInExistingKitbashTab, openWith);

            return await app.Dispatcher.InvokeAsync(() => OpenOnUiThread(file, bringToFront, openInExistingKitbashTab, openWith));
        }

        private string? OpenOnUiThread(PackFile file, bool bringToFront, bool openInExistingKitbashTab, EditorEnums? openWith)
        {
            // An editor that cannot open the file is refused here: with one candidate, the editor database ignores the
            // editor asked for and opens the file in that one.
            if (openWith != null)
            {
                var candidates = _editorDatabase.EditorsFor(_packFileService.GetFullPath(file));
                if (candidates.Contains(openWith.Value) == false)
                    return $"{openWith} cannot open {file.Name}; " + (candidates.Count == 0 ? "no editor can" : $"these can: {string.Join(", ", candidates)}");
            }

            var intoKitbash = openWith == null || openWith == EditorEnums.Kitbash_Editor;
            if (openInExistingKitbashTab && intoKitbash && CanImportIntoKitbash(file) && TryImportIntoExistingKitbash(file))
            {
                if (bringToFront)
                    BringMainWindowToFront();
                return null;
            }

            var command = _uiCommandFactory.Create<OpenEditorCommand>();
            if (openWith != null)
                command.Execute(file, openWith);
            else if (ShouldForceKitbash(file))
                command.Execute(file, EditorEnums.Kitbash_Editor);
            else
                command.Execute(file);

            if (bringToFront)
                BringMainWindowToFront();
            return null;
        }

        private void BringMainWindowToFront()
        {
            var app = Application.Current;
            var window = app?.MainWindow ?? app?.Windows.OfType<Window>().FirstOrDefault();
            if (window == null)
                return;

            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;

            window.Activate();
            _ = window.Focus();

            if (window.Topmost == false)
            {
                window.Topmost = true;
                window.Topmost = false;
            }

            window.Activate();
        }

        private bool TryImportIntoExistingKitbash(PackFile file)
        {
            var existingKitbash = _editorManager.GetAllEditors().OfType<KitbasherViewModel>().LastOrDefault();
            if (existingKitbash == null)
                return false;

            var localCommandFactory = _scopeRepository.GetRequiredService<IUiCommandFactory>(existingKitbash);
            localCommandFactory.Create<ImportReferenceMeshCommand>(x => x.Configure(file)).Execute();

            SelectEditor(existingKitbash);
            return true;
        }

        private void SelectEditor(IEditorInterface editor)
        {
            if (_editorManager is not IEditorManager concreteEditorManager)
                return;

            _editorManager.SetEditorAsCurrent(editor);
        }

        public static bool ShouldForceKitbash(PackFile file)
        {
            return string.Equals(file.Extension, ".wsmodel", StringComparison.InvariantCultureIgnoreCase)
                || string.Equals(file.Extension, ".variantmeshdefinition", StringComparison.InvariantCultureIgnoreCase);
        }

        public static bool CanImportIntoKitbash(PackFile file)
        {
            return string.Equals(file.Extension, ".rigid_model_v2", StringComparison.InvariantCultureIgnoreCase)
                || string.Equals(file.Extension, ".wsmodel", StringComparison.InvariantCultureIgnoreCase)
                || string.Equals(file.Extension, ".variantmeshdefinition", StringComparison.InvariantCultureIgnoreCase);
        }
    }
}
