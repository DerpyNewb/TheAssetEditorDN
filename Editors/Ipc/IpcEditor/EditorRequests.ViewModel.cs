using System.Reflection;

namespace Editors.Ipc
{
    // vm: any editor's view model by path (ViewModelPaths), so the editors with no 3D scene and no actions of their own (the
    // meta, audio, VFX, texture, twui, pack and text editors among them) can be read and driven over the pipe too.
    public partial class EditorRequests
    {
        public const int VmLimit = 400;

        static IpcResponse ViewModel(object editor, IpcRequest request)
        {
            try
            {
                switch (request.Name?.Trim().ToLowerInvariant())
                {
                    case null or "" or "tree":
                        var (members, truncated) = ViewModelPaths.Tree(editor, request.Path, request.Depth ?? 1, request.Limit ?? VmLimit);
                        return new IpcResponse { Ok = true, Data = members, Truncated = truncated ? true : null };
                    case "get":
                        return Data(string.IsNullOrWhiteSpace(request.Path)
                            ? new ViewModelPaths.Member("", "object", editor.GetType().Name)
                            : ViewModelPaths.Get(editor, request.Path));
                    case "set":
                        return Data(ViewModelPaths.Set(editor, request.Path ?? "", request.Value));
                    case "invoke":
                        return Data(ViewModelPaths.Invoke(editor, request.Path ?? "", request.Value));
                    default:
                        return IpcResponse.Failure($"vm has no \"{request.Name}\"; one of tree, get, set, invoke");
                }
            }
            // Anything thrown, the editor's own exceptions included, is the reply: one bad path must not take the pipe down.
            catch (Exception e)
            {
                return IpcResponse.Failure((e as TargetInvocationException)?.InnerException?.Message ?? e.Message);
            }
        }
    }
}
