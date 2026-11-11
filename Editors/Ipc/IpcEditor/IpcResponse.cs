namespace Editors.Ipc
{
    public class IpcResponse
    {
        public bool Ok { get; set; }
        public string Error { get; set; }
        public string NormalizedPath { get; set; }
        public List<string> Files { get; set; }
        public bool? Truncated { get; set; }
        public object? Data { get; set; }
        // An editor action still running when the reply was due, most likely behind a dialog it opened.
        public bool? Pending { get; set; }

        public static IpcResponse Success() => new() { Ok = true };

        public static IpcResponse Failure(string error, string normalizedPath = null) => new()
        {
            Ok = false,
            Error = error,
            NormalizedPath = normalizedPath
        };
    }
}
