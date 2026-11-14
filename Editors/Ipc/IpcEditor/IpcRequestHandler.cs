using Shared.Core.ToolCreation;

namespace Editors.Ipc
{
    public class IpcRequestHandler : IIpcRequestHandler
    {
        private readonly ILogger _logger = Logging.Create<IpcRequestHandler>();
        private readonly IExternalPackLoader _packLoader;
        private readonly IExternalPackFileLookup _packFileLookup;
        private readonly IExternalFileOpenExecutor _fileOpenExecutor;
        private readonly IIpcUserNotifier _userNotifier;
        private readonly EditorRequests? _editorRequests;
        private readonly IUiDispatcher? _uiDispatcher;

        static readonly TimeSpan EditorReplyTimeout = TimeSpan.FromSeconds(5);

        public IpcRequestHandler(IExternalPackLoader packLoader, IExternalPackFileLookup packFileLookup, IExternalFileOpenExecutor fileOpenExecutor, IIpcUserNotifier userNotifier,
            EditorRequests? editorRequests = null, IUiDispatcher? uiDispatcher = null)
        {
            _packLoader = packLoader;
            _packFileLookup = packFileLookup;
            _fileOpenExecutor = fileOpenExecutor;
            _userNotifier = userNotifier;
            _editorRequests = editorRequests;
            _uiDispatcher = uiDispatcher;
        }

        public async Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var action = request.Action?.Trim();
            if (string.IsNullOrWhiteSpace(action))
                return IpcResponse.Failure("Unsupported action");

            // ping: is an AssetEditor listening. load: a pack from disk, with no file opened. find: the paths open takes.
            if (Is(action, "ping"))
                return IpcResponse.Success();
            if (EditorRequests.Handles(action) && _editorRequests != null && _uiDispatcher != null)
            {
                // A command can open a modal dialog and hold the UI thread until it closes. Answer before then, so the
                // caller can see the dialog; the dialog's own loop keeps serving the pipe's next requests.
                var work = _uiDispatcher.InvokeAsync(() => _editorRequests.Handle(request), cancellationToken);
                if (await Task.WhenAny(work, Task.Delay(EditorReplyTimeout, cancellationToken)) == work)
                    return await work;
                return new IpcResponse { Ok = true, Pending = true };
            }
            if (Is(action, "open") == false && Is(action, "load") == false && Is(action, "find") == false)
                return IpcResponse.Failure("Unsupported action");

            var packPathOnDisk = GetPackPathOnDisk(request);
            if (string.IsNullOrWhiteSpace(packPathOnDisk) == false)
            {
                var loadResult = await _packLoader.EnsureLoadedAsync(packPathOnDisk, cancellationToken);
                if (loadResult.Success == false)
                    return IpcResponse.Failure(loadResult.Error ?? "Pack file load failed");
            }

            if (Is(action, "load"))
                return string.IsNullOrWhiteSpace(packPathOnDisk) ? IpcResponse.Failure("packPathOnDisk is empty") : IpcResponse.Success();
            if (Is(action, "find"))
                return Find(request);

            var normalizedPath = PackPathResolver.ResolvePackPath(request.Path);
            if (string.IsNullOrWhiteSpace(normalizedPath))
                return IpcResponse.Failure("Path is empty");

            EditorEnums? openWith = null;
            if (string.IsNullOrWhiteSpace(request.OpenWith) == false)
            {
                openWith = ParseEditor(request.OpenWith);
                if (openWith == null)
                    return IpcResponse.Failure($"No editor \"{request.OpenWith}\"; one of {string.Join(", ", Enum.GetValues<EditorEnums>().Where(x => x != EditorEnums.None))}");
            }

            var packFile = _packFileLookup.FindByPath(normalizedPath);
            if (packFile == null)
            {
                _logger.Here().Information($"External open failed. File not found: {normalizedPath}");
                await _userNotifier.ShowExternalOpenFailedAsync(normalizedPath, cancellationToken);
                return IpcResponse.Failure("File not found", normalizedPath);
            }

            var bringToFront = request.BringToFront != false;
            var openInExistingKitbashTab = request.OpenInExistingKitbashTab == true;
            var error = await _fileOpenExecutor.OpenAsync(packFile, bringToFront, openInExistingKitbashTab, openWith, cancellationToken);

            return error == null ? IpcResponse.Success() : IpcResponse.Failure(error, normalizedPath);
        }

        // "SuperView", "superview_editor" and "SuperView_Editor" all name EditorEnums.SuperView_Editor.
        public static EditorEnums? ParseEditor(string name)
        {
            name = name.Trim();
            foreach (var editor in Enum.GetValues<EditorEnums>().Where(x => x != EditorEnums.None))
            {
                var full = editor.ToString();
                var shortName = full.EndsWith("_Editor", StringComparison.Ordinal) ? full[..^"_Editor".Length] : full;
                if (string.Equals(name, full, StringComparison.OrdinalIgnoreCase) || string.Equals(name, shortName, StringComparison.OrdinalIgnoreCase))
                    return editor;
            }
            return null;
        }

        private IpcResponse Find(IpcRequest request)
        {
            var text = request.Path?.Trim().Replace('/', '\\');
            if (string.IsNullOrWhiteSpace(text))
                return IpcResponse.Failure("Path is empty");

            var limit = request.Limit is > 0 ? request.Limit.Value : DefaultFindLimit;
            var files = _packFileLookup.Search(text, limit + 1);
            return new IpcResponse { Ok = true, Files = files.Take(limit).ToList(), Truncated = files.Count > limit };
        }

        public const int DefaultFindLimit = 200;

        static bool Is(string action, string name) => string.Equals(action, name, StringComparison.OrdinalIgnoreCase);

        private static string GetPackPathOnDisk(IpcRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.PackPathOnDisk) == false)
                return request.PackPathOnDisk;

            return string.Empty;
        }
    }
}
