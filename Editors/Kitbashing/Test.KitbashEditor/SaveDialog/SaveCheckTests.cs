using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Editors.KitbasherEditor.ChildEditors.PinTool;
using Editors.KitbasherEditor.ChildEditors.SaveDialog;
using GameWorld.Core.Components;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services.SceneSaving;
using GameWorld.Core.Services.SceneSaving.Lod;
using Microsoft.Xna.Framework;
using Shared.Core.Commands;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.KitbashEditor.SaveDialog
{
    // SaveValidatorService was written and never called: nothing checked a model before Kitbash saved it. A save now lists what it
    // finds, with a fix beside each problem that has one, and asks whether to save anyway; Check model shows the same list.
    public class SaveCheckTests
    {
        AssetEditorTestRunner _runner = null!;
        IPackFileContainer _output = null!;
        MainEditableNode _karl = null!;
        Rmv2MeshNode _mesh = null!;

        [SetUp]
        public void Setup()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(TestFiles.KarlPackFile, true);
            _output = _runner.CreateOutputPack();
            _runner.CommandFactory.Create<OpenEditorCommand>().Execute(_runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!);

            var settings = Service<GeometrySaveSettings>();
            settings.IsUserInitialized = true;
            settings.DisplayDialogOnNextSave = false;
            _karl = Service<SceneManager>().GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel);
            _mesh = _karl.GetMeshesInLod(0, true)[0];
        }

        T Service<T>() where T : notnull => _runner.GetRequiredServiceInCurrentEditorScope<T>();

        SaveCommand Save(Func<MainEditableNode, ErrorList, bool> askToSaveAnyway)
        {
            var command = Service<IUiCommandFactory>().Create<SaveCommand>();
            command.AskToSaveAnyway = askToSaveAnyway;
            return command;
        }

        SaveCheckViewModel Check(bool forSave)
        {
            var check = Service<SaveCheckViewModel>();
            check.Initialize(_karl, Service<SaveValidatorService>().Validate(_karl, Service<GeometrySaveSettings>()), forSave);
            return check;
        }

        static SaveCheckRow Row(SaveCheckViewModel check, SaveFix fix) => check.Problems.Single(x => (x.Problem as SaveProblem)?.Fix == fix);

        // Every LOD: his LOD 0 and 1 are four-weight vertices up to 3/255 short of 1, and his LOD 2 and 3 two-weight vertices
        // with only the first weight set, both as CA ship them.
        [Test]
        public void CasOwnKarlFranz_PassesTheWeightChecks_InEveryLod()
        {
            var settings = Service<GeometrySaveSettings>();
            settings.LodGenerationMethod = LodStrategy.NoLodGeneration;

            var problems = Service<SaveValidatorService>().Validate(_karl, settings);

            Assert.That(problems.Errors.Select(x => x.Description), Has.None.Contains("bone weights"));
        }

        [Test]
        public void NothingFound_SavesWithoutAsking()
        {
            var asked = false;
            var result = Save((_, _) => asked = true).ExecuteWithResult();

            Assert.That(asked, Is.False);
            Assert.That(result?.GeneratedMesh, Is.Not.Null);
            Assert.That(Check(forSave: false).NothingFound, Is.True, "Check model lists a problem in Karl");
        }

        [Test]
        public void AVertexWithNoWeight_IsListedBeforeTheSave_AndCancelSavesNothing()
        {
            _mesh.Geometry.VertexArray[0].BlendWeights = Vector4.Zero;
            ErrorList? asked = null;

            var result = Save((_, problems) => { asked = problems; return false; }).ExecuteWithResult();

            var problem = asked!.Errors.OfType<SaveProblem>().Single(x => x.Fix == SaveFix.BindToBone);
            Assert.That(problem.Mesh, Is.SameAs(_mesh));
            Assert.That(problem.Description, Does.StartWith($"1 of {_mesh.Geometry.VertexArray.Length} vertices have no bone weights"));
            Assert.That(result, Is.Null);
            Assert.That(_output.GetFileCount(), Is.EqualTo(0), "Cancel still saved");
        }

        [Test]
        public void SaveAnyway_Saves()
        {
            _mesh.Geometry.VertexArray[0].BlendWeights = Vector4.Zero;

            var result = Save((_, _) => true).ExecuteWithResult();

            Assert.That(result?.GeneratedMesh, Is.Not.Null);
            Assert.That(_output.GetFileCount(), Is.GreaterThan(0));
        }

        // The save normalises every vertex. The fix normalises only the ones the check flags, so it leaves CA's own vertices, which
        // miss 1 by their byte rounding, as they are.
        [Test]
        public void NormaliseWeights_ScalesOnlyTheVerticesTheCheckFlags_AsOneUndoStep()
        {
            Assert.That(_mesh.Geometry.WeightCount, Is.EqualTo(4));
            var vertices = _mesh.Geometry.VertexArray;
            vertices[0].BlendWeights = new Vector4(0.3f);
            var ca = Enumerable.Range(1, vertices.Length - 1).First(i => MathF.Abs(vertices[i].GetBoneWeights().Sum() - 1) > 1e-4f);
            var caWeights = vertices[ca].BlendWeights;
            var check = Check(forSave: false);

            Row(check, SaveFix.NormaliseWeights).Fix!.Execute(null);

            Assert.That(Vector4.Distance(_mesh.Geometry.VertexArray[0].BlendWeights, new Vector4(0.25f)), Is.LessThan(1e-6f));
            Assert.That(_mesh.Geometry.VertexArray[ca].BlendWeights, Is.EqualTo(caWeights), "a vertex the check passed was changed");
            Assert.That((check.Problems.Count, check.NothingFound), Is.EqualTo((0, true)), "the list did not check again after the fix");

            Service<Shared.Core.Events.CommandManager>().Undo();
            Assert.That(_mesh.Geometry.VertexArray[0].BlendWeights, Is.EqualTo(new Vector4(0.3f)));
            Assert.That(check.Problems.Select(x => (x.Problem as SaveProblem)?.Fix), Is.EqualTo(new SaveFix?[] { SaveFix.NormaliseWeights }), "the list did not check again after the undo");
        }

        [Test]
        public void VerticesWithNoWeight_OfferTheBindFix_WhichOpensThePinToolInBoneModeWithTheMesh()
        {
            _mesh.Geometry.VertexArray[0].BlendWeights = Vector4.Zero;
            var check = Check(forSave: false);
            IReadOnlyList<Rmv2MeshNode>? opened = null;
            check.OpenPinToolToBind = x => opened = x;

            Row(check, SaveFix.BindToBone).Fix!.Execute(null);

            Assert.That(opened, Is.EqualTo(new[] { _mesh }));
            var pinTool = Service<PinToolViewModel>();
            pinTool.AffectedMeshCollection.Add(_karl.GetMeshesInLod(0, true)[1]);
            pinTool.ShowBind(opened!);
            Assert.That(pinTool.SelectedRiggingMode, Is.EqualTo(RiggingMode.Bone));
            Assert.That(pinTool.AffectedMeshCollection, Is.EqualTo(new[] { _mesh }));
        }

        [Test]
        public void ATextureInNoPack_OffersReplaceInTexturePaths_WithThePathToFind()
        {
            const string Missing = @"variantmeshes\nowhere\missing_base_colour.dds";
            _mesh.Material.GetCapability<MetalRoughCapability>().BaseColour.TexturePath = Missing;
            var check = Check(forSave: false);
            string? opened = null;
            check.OpenReplaceTexturePaths = x => opened = x;

            Row(check, SaveFix.ReplaceTexturePath).Fix!.Execute(null);

            Assert.That(opened, Is.EqualTo(Missing));
        }

        // The save's window is modal and the tool needs the main window to select in, so the save stops and the tool opens after.
        [Test]
        public void DuringASave_AToolsFixCancelsTheSave_AndOpensTheToolOnceTheWindowHasClosed()
        {
            _mesh.Geometry.VertexArray[0].BlendWeights = Vector4.Zero;
            var check = Check(forSave: true);
            var closes = 0;
            check.CloseRequested += () => closes++;
            IReadOnlyList<Rmv2MeshNode>? opened = null;
            check.OpenPinToolToBind = x => opened = x;

            Row(check, SaveFix.BindToBone).Fix!.Execute(null);

            Assert.That((closes, opened, check.SaveAnyway), Is.EqualTo((1, (IReadOnlyList<Rmv2MeshNode>?)null, false)));
            check.AfterClose!();
            Assert.That(opened, Is.EqualTo(new[] { _mesh }));

            var saving = Check(forSave: true);
            saving.CloseRequested += () => closes++;
            saving.ConfirmSaveCommand.Execute(null);
            Assert.That((closes, saving.SaveAnyway), Is.EqualTo((2, true)));
        }

        [Test]
        public void ErrorsAreListedFirst()
        {
            var meshes = _karl.GetMeshesInLod(0, true);
            meshes[0].Geometry.VertexArray[0].BlendWeights = new Vector4(0.3f);
            meshes[1].Geometry.VertexArray[0].BlendWeights = Vector4.Zero;

            var check = Check(forSave: false);

            Assert.That(check.Problems.Select(x => x.Problem.IsError), Is.EqualTo(new[] { true, false }));
        }

        // The view's XAML loads in no other test: a bad binding path shows only when it opens. In a plain window, because an
        // AssetEditorWindow needs the app's main window, which belongs to another test's thread.
        [Test, Apartment(ApartmentState.STA)]
        public void TheView_ShowsAFixButtonOnlyBesideAProblemWithAFix_AndTheSaveButtonsOnlyDuringASave()
        {
            AppResources.Load();
            _mesh.Geometry.VertexArray[0].BlendWeights = Vector4.Zero;
            _karl.GetMeshesInLod(0, true)[1].PivotPoint = new Vector3(0, 1, 0);
            foreach (var forSave in new[] { true, false })
            {
                var check = Check(forSave);
                Assert.That(check.Problems.Count(x => x.HasFix), Is.EqualTo(1));
                Assert.That(check.Problems.Count, Is.GreaterThan(1), "the pivot warnings have no fix");
                var view = new SaveCheckView { DataContext = check };
                var host = new Window { Content = view, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None, Left = -10000, Width = 620, Height = 420 };
                try
                {
                    host.Show();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    var fixes = Shown(view, "SaveCheckFix");
                    Assert.That(fixes.Select(x => x.Command), Is.EqualTo(new[] { Row(check, SaveFix.BindToBone).Fix }));
                    Assert.That(Shown(view, "SaveCheckSaveAnyway").Concat(Shown(view, "SaveCheckCancel")).Select(x => x.Command),
                        Is.EqualTo(forSave ? new ICommand[] { check.ConfirmSaveCommand, check.CloseCommand } : Array.Empty<ICommand>()));
                    Assert.That(Shown(view, "SaveCheckAgain").Concat(Shown(view, "SaveCheckClose")).Select(x => x.Command),
                        Is.EqualTo(forSave ? Array.Empty<ICommand>() : new ICommand[] { check.RefreshCommand, check.CloseCommand }));
                }
                finally
                {
                    host.Close();
                }
            }

            var english = new LocalizationManager();
            english.LoadLanguage("en");
            var keys = new[] { "Title", "SaveTitle", "SaveHeader", "NothingFound", "SaveAnyway", "Cancel", "CheckAgain", "CheckAgainTooltip", "Close" }
                .Concat(new[] { SaveFix.NormaliseWeights, SaveFix.BindToBone, SaveFix.ReplaceTexturePath }.SelectMany(x => new[] { $"Fix.{x}", $"Fix.{x}.Tooltip" }));
            Assert.That(keys.Where(x => english.Get("KitbashTool.SaveCheck." + x) == "KitbashTool.SaveCheck." + x), Is.Empty);
        }

        static List<Button> Shown(DependencyObject root, string automationId) => Descendants(root).OfType<Button>()
            .Where(x => AutomationProperties.GetAutomationId(x) == automationId && x.IsVisible)
            .ToList();

        static IEnumerable<DependencyObject> Descendants(DependencyObject parent) =>
            Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(parent)).Select(i => VisualTreeHelper.GetChild(parent, i)).SelectMany(x => Descendants(x).Prepend(x));
    }
}
