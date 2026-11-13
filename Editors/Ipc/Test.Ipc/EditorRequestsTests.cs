using System.Text.Json;
using Editors.Ipc;
using Editors.KitbasherEditor.UiCommands;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Shared.Core.Commands;
using Shared.Core.Events;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.Ipc
{
    // The editor actions, sent through the handler the pipe calls, against a real Kitbash editor with Karl loaded.
    // Each test checks what changed inside the editor, not only the reply.
    public class EditorRequestsTests
    {
        const string KarlRmv = @"variantmeshes\wh_variantmodels\hu1\emp\emp_karl_franz\emp_karl_franz.rigid_model_v2";

        AssetEditorTestRunner _runner = null!;
        IIpcRequestHandler _handler = null!;
        Rmv2MeshNode _mesh = null!;

        [SetUp]
        public void OpenKarl()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true);
            _runner.CommandFactory.Create<OpenEditorCommand>().Execute(_runner.PackFileService.FindFile(KarlRmv)!);
            _handler = _runner.ServiceProvider.GetRequiredService<IIpcRequestHandler>();

            var main = Service<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)!;
            _mesh = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(main).First();
        }

        T Service<T>() where T : notnull => _runner.GetRequiredServiceInCurrentEditorScope<T>();
        IpcResponse Send(IpcRequest request) => _handler.HandleAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        List<ISelectable> Selected() => (Service<SelectionManager>().GetState() as ObjectSelectionState)?.CurrentSelection() ?? [];

        [Test]
        public void Editors_ListsTheKitbashTab_AsCurrent()
        {
            var reply = Send(new IpcRequest { Action = "editors" });

            var editors = (List<EditorRequests.EditorInfo>)reply.Data!;
            Assert.That(editors, Has.Count.EqualTo(1));
            Assert.That(editors[0].Type, Is.EqualTo("KitbasherViewModel"));
            Assert.That(editors[0].Current, Is.True);
        }

        [Test]
        public void Scene_ListsEachNodeOnce_WithItsPathAndCounts()
        {
            var reply = Send(new IpcRequest { Action = "scene" });

            var nodes = (List<EditorRequests.SceneNodeInfo>)reply.Data!;
            Assert.That(nodes.Select(x => x.Id).Distinct().Count(), Is.EqualTo(nodes.Count), "an id appears twice");
            var mesh = nodes.Single(x => x.Id == _mesh.Id);
            Assert.That(mesh.Path, Does.StartWith("Root/Editable Model/"));
            Assert.That(mesh.Path, Does.EndWith("/" + _mesh.Name));
            Assert.That(mesh.Vertices, Is.EqualTo(_mesh.Geometry.VertexCount()));
            Assert.That(mesh.Faces, Is.EqualTo(_mesh.Geometry.GetIndexCount() / 3));

            // The Python side reads these names off the wire.
            var json = JsonSerializer.Serialize(reply, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            Assert.That(json, Does.Contain("\"data\":[{\"id\":"));
            Assert.That(json, Does.Contain("\"vertices\":"));
        }

        [Test]
        public void Select_ById_ThenWithNoNodes_Clears()
        {
            Assert.That(Send(new IpcRequest { Action = "select", Nodes = [_mesh.Id] }).Ok, Is.True);
            Assert.That(Selected(), Is.EqualTo(new[] { _mesh }));
            Assert.That(Send(new IpcRequest { Action = "scene" }).Data, Has.Some.Matches<EditorRequests.SceneNodeInfo>(x => x.Id == _mesh.Id && x.Selected));

            Send(new IpcRequest { Action = "select", Nodes = [] });
            Assert.That(Selected(), Is.Empty);
        }

        [Test]
        public void Select_ByAName_SharedByEveryLod_IsRefused_AndNamesTheIds()
        {
            var nodes = (List<EditorRequests.SceneNodeInfo>)Send(new IpcRequest { Action = "scene" }).Data!;
            var shared = nodes.GroupBy(x => x.Path.Split('/').Last()).First(g => g.Count() > 1 && g.All(x => x.Vertices != null));

            var reply = Send(new IpcRequest { Action = "select", Nodes = [shared.Key] });

            Assert.That(reply.Ok, Is.False);
            foreach (var node in shared)
                Assert.That(reply.Error, Does.Contain(node.Id));
            Assert.That(Selected(), Is.Empty);
        }

        [Test]
        public void Visible_HidesAndShows()
        {
            Send(new IpcRequest { Action = "visible", Nodes = [_mesh.Id], Visible = false });
            Assert.That(_mesh.IsVisible, Is.False);
            Send(new IpcRequest { Action = "visible", Nodes = [_mesh.Id], Visible = true });
            Assert.That(_mesh.IsVisible, Is.True);
        }

        [Test]
        public void Select_AReferenceMesh_IsRefused_AsTheViewportRefusesIt()
        {
            Service<IUiCommandFactory>().Create<ImportReferenceMeshCommand>(x => x.Configure(KarlRmv)).Execute();
            var references = Service<SceneManager>().GetNodeByName<GroupNode>(SpecialNodes.ReferenceMeshs)!;
            var reference = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(references).First();

            var reply = Send(new IpcRequest { Action = "select", Nodes = [reference.Id] });

            Assert.That(reply.Ok, Is.False);
            Assert.That(reply.Error, Does.Contain(reference.Id));
            Assert.That(Selected(), Is.Empty);
        }

        [Test]
        public void Transform_OfAPartLockedAfterItWasSelected_IsRefused_AndMovesNothing()
        {
            var before = _mesh.Geometry.GetVertexById(0);
            Send(new IpcRequest { Action = "select", Nodes = [_mesh.Id] });
            _mesh.IsSelectable = false;

            var reply = Send(new IpcRequest { Action = "transform", Translate = [1, 0, 0] });

            Assert.That(reply.Ok, Is.False);
            Assert.That(reply.Error, Does.Contain(_mesh.Id));
            Assert.That(_mesh.Geometry.GetVertexById(0), Is.EqualTo(before));
        }

        [Test]
        public void Transform_MovesTheSelection_AndUndoPutsItBack()
        {
            var before = _mesh.Geometry.GetVertexById(0);
            Send(new IpcRequest { Action = "select", Nodes = [_mesh.Id] });

            var reply = Send(new IpcRequest { Action = "transform", Translate = [1, 0, 0] });

            Assert.That(reply.Ok, Is.True, reply.Error);
            Assert.That(Vector3.Distance(_mesh.Geometry.GetVertexById(0), before + Vector3.UnitX), Is.LessThan(1e-4));
            Assert.That(Send(new IpcRequest { Action = "command", Name = "UndoCommand" }).Ok, Is.True);
            Assert.That(Vector3.Distance(_mesh.Geometry.GetVertexById(0), before), Is.LessThan(1e-4));
        }

        [Test]
        public void Transform_WithNothingSelected_IsRefused()
        {
            var before = _mesh.Geometry.GetVertexById(0);
            var reply = Send(new IpcRequest { Action = "transform", Translate = [1, 0, 0] });

            Assert.That(reply.Ok, Is.False);
            Assert.That(reply.Error, Does.Contain("Nothing is selected"));
            Assert.That(_mesh.Geometry.GetVertexById(0), Is.EqualTo(before));
        }

        [Test]
        public void Command_WithNoName_ListsTheToolbar_AndByName_RunsIt()
        {
            var names = (List<string>)Send(new IpcRequest { Action = "command" }).Data!;
            Assert.That(names, Is.SupersetOf(new[] { "DeleteObjectCommand", "SaveCommand", "UndoCommand" }));

            Send(new IpcRequest { Action = "select", Nodes = [_mesh.Id] });
            Assert.That(Send(new IpcRequest { Action = "command", Name = "deleteobjectcommand" }).Ok, Is.True);

            Assert.That(Service<SceneManager>().GetEnumeratorConditional(x => x == _mesh), Is.Empty, "the mesh is still in the scene");
        }

        [Test]
        public void Camera_SetsInDegrees_AndReadsBack()
        {
            var reply = Send(new IpcRequest { Action = "camera", Yaw = 90, Zoom = 7, LookAt = [0, 1, 0] });

            var camera = Service<ArcBallCamera>();
            Assert.That(camera.Yaw, Is.EqualTo(MathHelper.PiOver2).Within(1e-5));
            Assert.That(camera.Zoom, Is.EqualTo(7));
            Assert.That(camera.LookAt, Is.EqualTo(new Vector3(0, 1, 0)));
            Assert.That(JsonSerializer.Serialize(reply.Data), Does.Contain("\"yaw\":90"));
        }

        [Test]
        public void AnEditorThatIsNotOpen_IsNamed()
        {
            var reply = Send(new IpcRequest { Action = "scene", Editor = 3 });

            Assert.That(reply.Ok, Is.False);
            Assert.That(reply.Error, Is.EqualTo("No editor 3; 1 are open, 0 to 0"));
        }

        [Test]
        public void AnEditorWithNoScene_IsRefused_BeforeOneIsBuiltForIt()
        {
            var material = @"variantmeshes\wh_variantmodels\hu1\emp\emp_karl_franz\materials\emp_karl_franz_body_01_weighted2_alpha_off.xml.material";
            _runner.CommandFactory.Create<OpenEditorCommand>().Execute(_runner.PackFileService.FindFile(material)!);
            var editors = (List<EditorRequests.EditorInfo>)Send(new IpcRequest { Action = "editors" }).Data!;
            Assert.That(editors, Has.Count.EqualTo(2));

            var reply = Send(new IpcRequest { Action = "scene", Editor = 1 });

            Assert.That(reply.Ok, Is.False);
            Assert.That(reply.Error, Does.EndWith("has no 3D scene"));
        }

        [Test]
        public async Task ACommandStillRunning_AnswersPending_InsteadOfHoldingThePipe()
        {
            var handler = new IpcRequestHandler(null!, null!, null!, null!, new EditorRequests(null!, null!), new NeverDispatcher());

            var request = handler.HandleAsync(new IpcRequest { Action = "command", Name = "SaveCommand" }, CancellationToken.None);
            Assert.That(await Task.WhenAny(request, Task.Delay(TimeSpan.FromSeconds(30))), Is.SameAs(request), "the reply waited for the UI thread");

            var reply = await request;
            Assert.That(reply.Ok, Is.True);
            Assert.That(reply.Pending, Is.True);
        }

        // The UI thread busy behind a modal dialog: the work never finishes while the request waits.
        class NeverDispatcher : IUiDispatcher
        {
            public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken) => new TaskCompletionSource<T>().Task;
        }
    }
}
