using Editors.KitbasherEditor.ChildEditors.SaveDialog;
using GameWorld.Core.Components;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services.SceneSaving;
using GameWorld.Core.Services.SceneSaving.Lod;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Commands;
using Shared.Core.Events;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.SaveDialog
{
    // SaveValidatorService was written and never called: nothing checked a model before Kitbash saved it.
    public class SaveCheckTests
    {
        AssetEditorTestRunner _runner = null!;
        IPackFileContainer _output = null!;
        MainEditableNode _karl = null!;

        [SetUp]
        public void Setup()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(TestFiles.KarlPackFile, true);
            _output = _runner.CreateOutputPack();
            _runner.CommandFactory.Create<OpenEditorCommand>().Execute(_runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);

            var settings = _runner.GetRequiredServiceInCurrentEditorScope<GeometrySaveSettings>();
            settings.IsUserInitialized = true;
            settings.DisplayDialogOnNextSave = false;
            _karl = _runner.GetRequiredServiceInCurrentEditorScope<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel);
        }

        // Every LOD: his LOD 0 and 1 are four-weight vertices up to 3/255 short of 1, and his LOD 2 and 3 two-weight vertices
        // with only the first weight set, both as CA ship them.
        [Test]
        public void CasOwnKarlFranz_PassesTheWeightChecks_InEveryLod()
        {
            var settings = _runner.GetRequiredServiceInCurrentEditorScope<GeometrySaveSettings>();
            settings.LodGenerationMethod = LodStrategy.NoLodGeneration;

            var problems = _runner.GetRequiredServiceInCurrentEditorScope<SaveValidatorService>().Validate(_karl, settings);

            Assert.That(problems.Errors.Select(x => x.Description), Has.None.Contains("bone weights"));
        }

        [Test]
        public void NothingFound_SavesWithoutAsking()
        {
            var result = _runner.GetRequiredServiceInCurrentEditorScope<IUiCommandFactory>().Create<SaveCommand>().ExecuteWithResult();

            _runner.Dialogs.Verify(x => x.ShowYesNoBox(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            Assert.That(result?.GeneratedMesh, Is.Not.Null);
        }

        [Test]
        public void AVertexWithNoWeight_IsNamedBeforeTheSave_AndCancelSavesNothing()
        {
            var mesh = _karl.GetMeshesInLod(0, true)[0];
            mesh.Geometry.VertexArray[0].BlendWeights = Vector4.Zero;
            string? asked = null;
            _runner.Dialogs.Setup(x => x.ShowYesNoBox(It.IsAny<string>(), It.IsAny<string>())).Callback((string message, string _) => asked = message).Returns(ShowMessageBoxResult.Cancel);

            var result = _runner.GetRequiredServiceInCurrentEditorScope<IUiCommandFactory>().Create<SaveCommand>().ExecuteWithResult();

            Assert.That(asked, Does.Contain($"Error - {mesh.Name}: 1 of {mesh.Geometry.VertexArray.Length} vertices have no bone weights").And.EndWith("Save anyway?"));
            Assert.That(result, Is.Null);
            Assert.That(_output.GetFileCount(), Is.EqualTo(0), "Cancel still saved");
        }

        [Test]
        public void SaveAnyway_Saves()
        {
            _karl.GetMeshesInLod(0, true)[0].Geometry.VertexArray[0].BlendWeights = Vector4.Zero;
            _runner.Dialogs.Setup(x => x.ShowYesNoBox(It.IsAny<string>(), It.IsAny<string>())).Returns(ShowMessageBoxResult.OK);

            var result = _runner.GetRequiredServiceInCurrentEditorScope<IUiCommandFactory>().Create<SaveCommand>().ExecuteWithResult();

            Assert.That(result?.GeneratedMesh, Is.Not.Null);
            Assert.That(_output.GetFileCount(), Is.GreaterThan(0));
        }

        [Test]
        public void ALongList_IsCut_SoTheBoxKeepsItsButtons()
        {
            var problems = new Shared.Core.ErrorHandling.ErrorList();
            for (var i = 0; i < 25; i++)
                problems.Warning($"mesh {i}", "a warning");
            problems.Error("body", "an error");

            var text = SaveCommandBase.Describe(problems);

            var lines = text.Split('\n');
            Assert.That(lines, Has.Length.EqualTo(21));
            Assert.That(lines[0], Is.EqualTo("Error - body: an error"), "errors come first");
            Assert.That(lines[^1], Is.EqualTo("...and 6 more."));
        }
    }
}
