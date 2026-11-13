using System.Text;
using Editors.Ipc;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;

namespace Test.Ipc
{
    // open with "openWith", through the handler the pipe calls and the real executor and editor database. A model used to
    // go to Kitbash whatever was asked, so SuperView and the grids could not be driven over the pipe.
    public class OpenWithTests
    {
        const string WsModel = @"variantmeshes\derpy\derpy_test.wsmodel";

        AssetEditorTestRunner _runner = null!;
        IIpcRequestHandler _handler = null!;

        [SetUp]
        public void Setup()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            var pack = _runner.CreateOutputPack();
            _runner.PackFileService.AddFilesToPack(pack, [new NewPackFileEntry(@"variantmeshes\derpy",
                PackFile.CreateFromBytes("derpy_test.wsmodel", Encoding.UTF8.GetBytes("<model version=\"1\"></model>")))]);
            _handler = _runner.ServiceProvider.GetRequiredService<IIpcRequestHandler>();
        }

        IpcResponse Send(IpcRequest request) => _handler.HandleAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        IList<IEditorInterface> Editors() => _runner.ServiceProvider.GetRequiredService<IEditorManager>().GetAllEditors();

        [Test]
        public void AWsmodel_OpensInTheEditorNamed_NotInKitbash()
        {
            var reply = Send(new IpcRequest { Action = "open", Path = WsModel, OpenWith = "xml", BringToFront = false });

            Assert.That(reply.Ok, Is.True, reply.Error);
            Assert.That(Editors(), Has.Count.EqualTo(1));
            Assert.That(Editors()[0].GetType().Name, Does.StartWith("TextEditorViewModel"));
        }

        [Test]
        public void AnEditorThatCannotOpenTheFile_IsRefused_AndNamesTheOnesThatCan()
        {
            var reply = Send(new IpcRequest { Action = "open", Path = WsModel, OpenWith = "Texture", BringToFront = false });

            Assert.That(reply.Ok, Is.False);
            Assert.That(reply.Error, Does.Contain("Kitbash_Editor").And.Contain("XML_Editor"));
            Assert.That(Editors(), Is.Empty);
        }

        [TestCase("SuperView", EditorEnums.SuperView_Editor)]
        [TestCase("superview_editor", EditorEnums.SuperView_Editor)]
        [TestCase("VariantMesh", EditorEnums.VariantMesh_Editor)]
        [TestCase("XML_VariantMesh_Editor", EditorEnums.XML_VariantMesh_Editor)]
        [TestCase("Kitbash", EditorEnums.Kitbash_Editor)]
        public void EditorNames_ReadWithOrWithoutTheEditorSuffix(string name, EditorEnums expected)
        {
            Assert.That(IpcRequestHandler.ParseEditor(name), Is.EqualTo(expected));
        }

        [Test]
        public void AnUnknownEditorName_IsRefused_BeforeAnythingOpens()
        {
            var reply = Send(new IpcRequest { Action = "open", Path = WsModel, OpenWith = "Paint", BringToFront = false });

            Assert.That(reply.Ok, Is.False);
            Assert.That(reply.Error, Does.Contain("No editor \"Paint\"").And.Contain("SuperView_Editor"));
            Assert.That(Editors(), Is.Empty);
        }
    }
}
