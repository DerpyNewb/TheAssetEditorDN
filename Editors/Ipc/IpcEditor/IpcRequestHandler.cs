namespace Editors.Ipc
{
    public class IpcRequestHandler : IIpcRequestHandler
    {
        private readonly ILogger _logger = Logging.Create<IpcRequestHandler>();
        private readonly IExternalPackLoader _packLoader;
        private readonly IExternalPackFileLookup _packFileLookup;
        private readonly IExternalFileOpenExecutor _fileOpenExecutor;
        private readonly IIpcUserNotifier _userNotifier;

        public IpcRequestHandler(IExternalPackLoader packLoader, IExternalPackFileLookup packFileLookup, IExternalFileOpenExecutor fileOpenExecutor, IIpcUserNotifier userNotifier)
        {
            _packLoader = packLoader;
            _packFileLookup = packFileLookup;
            _fileOpenExecutor = fileOpenExecutor;
            _userNotifier = userNotifier;
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

            var packFile = _packFileLookup.FindByPath(normalizedPath);
            if (packFile == null)
            {
                _logger.Here().Information($"External open failed. File not found: {normalizedPath}");
                await _userNotifier.ShowExternalOpenFailedAsync(normalizedPath, cancellationToken);
                return IpcResponse.Failure("File not found", normalizedPath);
            }

            var bringToFront = request.BringToFront != false;
            var openInExistingKitbashTab = request.OpenInExistingKitbashTab == true;
            await _fileOpenExecutor.OpenAsync(packFile, bringToFront, openInExistingKitbashTab, cancellationToken);

            return IpcResponse.Success();
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
