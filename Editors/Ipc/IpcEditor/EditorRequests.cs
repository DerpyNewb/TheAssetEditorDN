using Editors.KitbasherEditor.Core.MenuBarViews;
using Editors.KitbasherEditor.ViewModels;
using Editors.Shared.Core.Common.BaseControl;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Shared.Core.ToolCreation;

namespace Editors.Ipc
{
    // The pipe's way into an open editor: its tabs, its 3D scene, its camera and the Kitbash toolbar. Called on the UI
    // thread. Every change goes through the editor's own commands, so the editor's Undo takes it back.
    public class EditorRequests
    {
        public static readonly string[] Actions = ["editors", "scene", "select", "visible", "transform", "command", "camera"];

        readonly IEditorManager _editorManager;
        readonly IScopeRepository _scopes;

        public EditorRequests(IEditorManager editorManager, IScopeRepository scopes)
        {
            _editorManager = editorManager;
            _scopes = scopes;
        }

        public static bool Handles(string action) => Actions.Contains(action, StringComparer.OrdinalIgnoreCase);

        public IpcResponse Handle(IpcRequest request)
        {
            var action = request.Action.Trim().ToLowerInvariant();
            var editors = _editorManager.GetAllEditors();
            if (action == "editors")
            {
                var current = _editorManager.GetCurrentEditor();
                return Data(editors.Select((e, i) => new EditorInfo(i, e.DisplayName, e.GetType().Name, i == current)).ToList());
            }

            var index = request.Editor ?? _editorManager.GetCurrentEditor();
            if (index < 0 || index >= editors.Count)
                return IpcResponse.Failure(editors.Count == 0 ? "No editor is open" : $"No editor {index}; {editors.Count} are open, 0 to {editors.Count - 1}");
            var editor = editors[index];

            // SceneManager is registered for every editor, so asking a text editor for one would build an empty scene.
            if (editor is not (KitbasherViewModel or EditorHostBase))
                return IpcResponse.Failure($"{editor.DisplayName} ({editor.GetType().Name}) has no 3D scene");

            try
            {
                return action switch
                {
                    "scene" => Data(Scene(editor)),
                    "select" => Select(editor, request),
                    "visible" => SetVisible(editor, request),
                    "transform" => Transform(editor, request),
                    "command" => Command(editor, request.Name),
                    "camera" => Camera(editor, request),
                    _ => IpcResponse.Failure("Unsupported action"),
                };
            }
            catch (Exception e)
            {
                Logging.Create<EditorRequests>().Here().Error(e, $"IPC {action} failed");
                return IpcResponse.Failure($"{action} failed: {e.Message}");
            }
        }

        T Get<T>(IEditorInterface editor) where T : notnull => _scopes.GetRequiredService<T>(editor);

        List<SceneNodeInfo> Scene(IEditorInterface editor)
        {
            var selected = (Get<SelectionManager>(editor).GetState() as ObjectSelectionState)?.CurrentSelection() ?? [];
            var nodes = new List<SceneNodeInfo>();
            Walk(Get<SceneManager>(editor).RootNode, "", 0);
            return nodes;

            void Walk(ISceneNode node, string parentPath, int depth)
            {
                var path = parentPath.Length == 0 ? node.Name : $"{parentPath}/{node.Name}";
                var geometry = (node as ISelectable)?.Geometry;
                nodes.Add(new SceneNodeInfo(node.Id, path, depth, node.GetType().Name, node.IsVisible, node.IsEditable,
                    node is ISelectable s && selected.Contains(s), geometry?.VertexCount(), geometry?.GetIndexCount() / 3));
                foreach (var child in node.Children)
                    Walk(child, path, depth + 1);
            }
        }

        // A node by its id, its path from Root, or its name when only one node has it.
        (List<ISceneNode> Nodes, string? Error) Find(IEditorInterface editor, List<string>? keys)
        {
            var all = new List<(ISceneNode Node, string Path)>();
            Collect(Get<SceneManager>(editor).RootNode, "");
            var found = new List<ISceneNode>();
            foreach (var key in keys ?? [])
            {
                var hits = all.Where(x => x.Node.Id == key).ToList();
                if (hits.Count == 0)
                    hits = all.Where(x => string.Equals(x.Path, key, StringComparison.OrdinalIgnoreCase)).ToList();
                if (hits.Count == 0)
                    hits = all.Where(x => string.Equals(x.Node.Name, key, StringComparison.OrdinalIgnoreCase)).ToList();
                if (hits.Count == 0)
                    return ([], $"No node \"{key}\". Use the id or path from the scene action.");
                if (hits.Count > 1)
                    return ([], $"\"{key}\" names {hits.Count} nodes; use an id: {string.Join(", ", hits.Select(x => $"{x.Node.Id} ({x.Path})"))}");
                found.Add(hits[0].Node);
            }
            return (found, null);

            void Collect(ISceneNode node, string parentPath)
            {
                var path = parentPath.Length == 0 ? node.Name : $"{parentPath}/{node.Name}";
                all.Add((node, path));
                foreach (var child in node.Children)
                    Collect(child, path);
            }
        }

        IpcResponse Select(IEditorInterface editor, IpcRequest request)
        {
            var (nodes, error) = Find(editor, request.Nodes);
            if (error != null)
                return IpcResponse.Failure(error);
            var notSelectable = nodes.Where(x => x is not ISelectable).Select(x => x.Name).ToList();
            if (notSelectable.Count != 0)
                return IpcResponse.Failure($"Only meshes can be selected, not {string.Join(", ", notSelectable)}");

            // No nodes and no add clears the selection, as a click on empty space does.
            var items = nodes.Cast<ISelectable>().ToList();
            Get<IUiCommandFactory>(editor).CreateWithBuilder<ObjectSelectionCommand>()
                .Configure(x => x.Configure(items, request.Add == true, false)).BuildAndExecute();
            return Data(items.Select(x => x.Id).ToList());
        }

        IpcResponse SetVisible(IEditorInterface editor, IpcRequest request)
        {
            if (request.Visible == null)
                return IpcResponse.Failure("visible needs \"visible\": true or false");
            var (nodes, error) = Find(editor, request.Nodes);
            if (error != null)
                return IpcResponse.Failure(error);
            if (nodes.Count == 0)
                return IpcResponse.Failure("visible needs \"nodes\"");
            foreach (var node in nodes)
                node.IsVisible = request.Visible.Value;
            return Data(nodes.Select(x => x.Id).ToList());
        }

        // As the Kitbash transform box does it, one mode per undo step: scale, then rotate, then translate.
        IpcResponse Transform(IEditorInterface editor, IpcRequest request)
        {
            if (request.Translate == null && request.Rotate == null && request.Scale == null)
                return IpcResponse.Failure("transform needs translate, rotate or scale, each [x, y, z]");
            foreach (var (name, v) in new[] { ("translate", request.Translate), ("rotate", request.Rotate), ("scale", request.Scale) })
            {
                if (v != null && v.Length != 3)
                    return IpcResponse.Failure($"{name} must be [x, y, z]");
            }
            if (request.Scale is { } s && s[0] * s[1] * s[2] == 0)
                return IpcResponse.Failure("A scale with a 0 in it cannot be undone");

            var pivot = request.Pivot?.ToLowerInvariant() switch
            {
                null or "object" => PivotType.ObjectCenter,
                "origin" => PivotType.WorldOrigin,
                _ => (PivotType?)null,
            };
            if (pivot == null)
                return IpcResponse.Failure("pivot is \"object\" (default) or \"origin\"");

            var selection = Get<SelectionManager>(editor);
            var commandFactory = Get<IUiCommandFactory>(editor);
            var commands = Get<CommandManager>(editor);
            var logger = Get<IScopedLogger>(editor);
            var steps = 0;

            void Apply(Action<TransformGizmoWrapper> change)
            {
                var wrapper = TransformGizmoWrapper.CreateFromSelectionState(selection.GetState(), commandFactory, logger)
                    ?? throw new InvalidOperationException("Nothing is selected to transform; use select first");
                wrapper.Start(commands);
                change(wrapper);
                wrapper.Stop(commands);
                steps++;
            }

            if (request.Scale is { } scale)
                Apply(w => w.GizmoScaleEvent(new Vector3(scale[0] - 1, scale[1] - 1, scale[2] - 1), pivot.Value));    // the wrapper adds the 1 back
            if (request.Rotate is { } r)
                Apply(w => w.GizmoRotateEvent(Matrix.CreateRotationX(MathHelper.ToRadians(r[0])) * Matrix.CreateRotationY(MathHelper.ToRadians(r[1]))
                    * Matrix.CreateRotationZ(MathHelper.ToRadians(r[2])), pivot.Value));
            if (request.Translate is { } t)
                Apply(w => w.GizmoTranslateEvent(new Vector3(t[0], t[1], t[2]), pivot.Value));
            return Data(new { undoSteps = steps });
        }

        static readonly Type[] KitbashCommands = typeof(IKitbasherUiCommand).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IKitbasherUiCommand).IsAssignableFrom(t))
            .OrderBy(t => t.Name).ToArray();

        // A Kitbash toolbar or menu command by its class name, run as its button runs it. No name lists them.
        IpcResponse Command(IEditorInterface editor, string? name)
        {
            if (editor is not KitbasherViewModel)
                return IpcResponse.Failure("command works in the Kitbash editor only");
            if (string.IsNullOrWhiteSpace(name))
                return Data(KitbashCommands.Select(x => x.Name).ToList());

            var type = KitbashCommands.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (type == null)
                return IpcResponse.Failure($"No Kitbash command \"{name}\"; send command with no name for the list");

            var command = (IKitbasherUiCommand)typeof(IScopeRepository).GetMethod(nameof(IScopeRepository.GetRequiredService))!
                .MakeGenericMethod(type).Invoke(_scopes, [editor])!;
            command.Execute();
            return IpcResponse.Success();
        }

        IpcResponse Camera(IEditorInterface editor, IpcRequest request)
        {
            if (request.LookAt is { Length: not 3 })
                return IpcResponse.Failure("lookAt must be [x, y, z]");

            var camera = Get<ArcBallCamera>(editor);
            if (request.Yaw is { } yaw)
                camera.Yaw = MathHelper.ToRadians(yaw);
            if (request.Pitch is { } pitch)
                camera.Pitch = MathHelper.ToRadians(pitch);
            if (request.Zoom is { } zoom)
                camera.Zoom = zoom;
            if (request.LookAt is { } l)
                camera.LookAt = new Vector3(l[0], l[1], l[2]);

            return Data(new
            {
                yaw = MathHelper.ToDegrees(camera.Yaw),
                pitch = MathHelper.ToDegrees(camera.Pitch),
                zoom = camera.Zoom,
                lookAt = new[] { camera.LookAt.X, camera.LookAt.Y, camera.LookAt.Z },
            });
        }

        static IpcResponse Data(object data) => new() { Ok = true, Data = data };

        public record EditorInfo(int Index, string Title, string Type, bool Current);
        public record SceneNodeInfo(string Id, string Path, int Depth, string Type, bool Visible, bool Editable, bool Selected, int? Vertices, int? Faces);
    }
}
