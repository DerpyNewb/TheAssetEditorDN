using Editors.AnimationMeta.Presentation;
using Editors.Ipc;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Commands;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Parsing;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.Ipc
{
    // The vm action through the handler the pipe calls, against a real Meta editor: one of the editors with no 3D scene and
    // so no other editor actions. Each test checks what changed inside the editor, not only the reply.
    public class ViewModelRequestsTests
    {
        const string MetaPath = @"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";

        AssetEditorTestRunner _runner = null!;
        IIpcRequestHandler _handler = null!;
        MetaDataEditorViewModel _editor = null!;

        [SetUp]
        public void OpenMeta()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            _editor = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<MetaDataEditorViewModel>(_runner.PackFileService.FindFile(MetaPath)!, EditorEnums.Meta_Editor)!;
            _handler = _runner.ServiceProvider.GetRequiredService<IIpcRequestHandler>();
        }

        IpcResponse Send(IpcRequest request) => _handler.HandleAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        static IpcRequest Vm(string name, string? path = null, string? value = null) => new() { Action = "vm", Name = name, Path = path, Value = value };

        [Test]
        public void Tree_ReadsAnEditorWithNoScene()
        {
            Assert.That(Send(new IpcRequest { Action = "scene" }).Error, Does.Contain("has no 3D scene"));

            var reply = Send(Vm("tree"));

            var members = (List<ViewModelPaths.Member>)reply.Data!;
            Assert.That(members.Single(x => x.Path == "Tags").Count, Is.EqualTo(_editor.Tags.Count).And.GreaterThan(1));
            Assert.That(members.Single(x => x.Path == "DeleteActionCommand").Kind, Is.EqualTo("command"));
        }

        [Test]
        public void Set_EditsATagsField_AsTypingInItsBoxDoes()
        {
            var reply = Send(Vm("set", "Tags[IsDecodedCorrectly=true].Variables[FieldName=Start Time].ValueAsString", "0.5"));

            Assert.That(reply.Ok, Is.True, reply.Error);
            var tag = (DecodedMetaEntryBase)_editor.Tags.First(x => x.IsDecodedCorrectly)._input;
            Assert.That(tag.StartTime, Is.EqualTo(0.5f));
            Assert.That(_editor.HasUnsavedChanges, Is.True);
        }

        [Test]
        public void SetThenInvoke_DeletesTheSelectedTag()
        {
            var count = _editor.Tags.Count;
            var second = _editor.Tags[1]._input;

            Assert.That(Send(Vm("set", "Tags[0].IsSelected", "true")).Ok, Is.True);
            var reply = Send(Vm("invoke", "DeleteActionCommand"));

            Assert.That(reply.Ok, Is.True, reply.Error);
            Assert.That(_editor.Tags, Has.Count.EqualTo(count - 1));
            Assert.That(_editor.Tags[0]._input, Is.SameAs(second), "the first tag went; the rows are rebuilt around the tags left");
        }

        [Test]
        public void Set_AnAtPath_SelectsThatTag()
        {
            Assert.That(Send(Vm("set", "SelectedTag", "@Tags[1]")).Ok, Is.True);

            Assert.That(_editor.SelectedTag, Is.SameAs(_editor.Tags[1]));
            Assert.That(_editor.SelectedAttribute, Is.SameAs(_editor.Tags[1]._input), "the editor reacted as to a click");
        }

        [Test]
        public void ABadPath_IsAnErrorReply_AndThePipeGoesOn()
        {
            var reply = Send(Vm("get", "Tags[999]"));

            Assert.That((reply.Ok, reply.Error), Is.EqualTo((false, $"Tags has {_editor.Tags.Count} items; [999] is past them")));
            Assert.That(Send(Vm("get", "Tags[0].DisplayName")).Ok, Is.True);
            Assert.That(Send(Vm("frob")).Error, Is.EqualTo("vm has no \"frob\"; one of tree, get, set, invoke"));
        }
    }
}
