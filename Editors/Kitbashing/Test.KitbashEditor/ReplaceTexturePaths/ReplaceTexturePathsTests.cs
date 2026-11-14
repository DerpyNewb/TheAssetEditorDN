using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Editors.KitbasherEditor.ChildEditors.ReplaceTexturePaths;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Rendering.Materials.Serialization;
using GameWorld.Core.Rendering.Materials.Shaders.MetalRough;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Types;
using Test.TestingUtility.TestUtility;

namespace Test.KitbashEditor.ReplaceTexturePaths
{
    // Find and replace in texture paths: the shader material (drawn, and written by the save), the loaded RMV2 material (read by
    // Copy textures to pack), the scope, case and undo.
    internal class ReplaceTexturePathsTests
    {
        const string Base = @"variantmeshes/wh3_dlc23_chd/tex/chd_hobgoblin_base_colour.dds";
        const string Mask = @"variantmeshes/wh3_dlc23_chd/tex/chd_hobgoblin_mask.dds";
        const string Emissive = @"variantmeshes/wh3_dlc23_chd/tex/chd_hobgoblin_emissive.dds";
        const string Blood = @"variantmeshes/_variantmodels/man/tex/blood_map_base_colour.dds";

        CommandManager _commands = null!;
        Mock<IPackFileService> _packFileService = null!;
        Mock<IStandardDialogs> _dialogs = null!;

        [SetUp]
        public void SetUp()
        {
            _commands = new CommandManager(new Mock<IEventHub>().Object, MockScopedLogger.Create());
            _packFileService = new Mock<IPackFileService>();
            _packFileService.Setup(x => x.FindFile(It.IsAny<string>(), It.IsAny<IPackFileContainer?>())).Returns(PackFile.CreateFromASCII("t.dds", ""));
            _dialogs = new Mock<IStandardDialogs>();
        }

        ReplaceTexturePathsViewModel CreateViewModel(Rmv2ModelNode model, params ISceneNode[] selected)
        {
            var viewModel = new ReplaceTexturePathsViewModel(_commands, _packFileService.Object, _dialogs.Object);
            viewModel.Initialize(model, selected);
            return viewModel;
        }

        static Rmv2MeshNode CreateMesh(string name)
        {
            var shader = new EmissiveMaterial(new Mock<IScopedResourceLibrary>().Object);
            var metalRough = shader.GetCapability<MetalRoughCapability>();
            metalRough.BaseColour.TexturePath = Base;
            metalRough.Mask.TexturePath = Mask;
            shader.GetCapability<EmissiveCapability>().Emissive.TexturePath = Emissive;
            shader.GetCapability<BloodCapability>().BloodMask.TexturePath = Blood;

            var rmv = new WeightedMaterial { ModelName = name };
            rmv.SetTexture(TextureType.BaseColour, Base);
            rmv.SetTexture(TextureType.Mask, Mask);
            return new Rmv2MeshNode(null!, rmv, shader, null!);
        }

        static (Rmv2ModelNode Model, Rmv2MeshNode Lod0, Rmv2MeshNode Lod0Grouped, Rmv2MeshNode Lod1) CreateModel()
        {
            var model = new Rmv2ModelNode("model");
            var lod0 = model.AddObject(new Rmv2LodNode("lod0", 0));
            var lod1 = model.AddObject(new Rmv2LodNode("lod1", 1));
            var group = lod0.AddObject(new GroupNode("group"));
            return (model, lod0.AddObject(CreateMesh("a")), group.AddObject(CreateMesh("b")), lod1.AddObject(CreateMesh("c")));
        }

        static string BaseOf(Rmv2MeshNode mesh) => mesh.Material.GetCapability<MetalRoughCapability>().BaseColour.TexturePath;

        [Test]
        public void Replace_ChangesTheDrawnAndSavedPaths_AndTheLoadedRmvCopy_AsOneUndoStep()
        {
            var (model, mesh, _, _) = CreateModel();
            var viewModel = CreateViewModel(model);
            viewModel.Find = "_base_colour";
            viewModel.Replace = "_recoloured_base_colour";

            // Three meshes, each with a base colour and a blood map that match; the masks and emissives do not.
            Assert.That(viewModel.ChangeCount, Is.EqualTo(6));
            Assert.That(viewModel.Apply(), Is.True);

            var newBase = @"variantmeshes/wh3_dlc23_chd/tex/chd_hobgoblin_recoloured_base_colour.dds";
            var metalRough = mesh.Material.GetCapability<MetalRoughCapability>();
            Assert.That(metalRough.BaseColour.TexturePath, Is.EqualTo(newBase), "the shader copy, which the 3D view draws");
            Assert.That(mesh.Material.GetCapability<BloodCapability>().BloodMask.TexturePath, Does.EndWith("blood_map_recoloured_base_colour.dds"), "every capability's slots");
            Assert.That(metalRough.Mask.TexturePath, Is.EqualTo(Mask));
            Assert.That(mesh.Material.GetCapability<EmissiveCapability>().Emissive.TexturePath, Is.EqualTo(Emissive));
            Assert.That(mesh.RmvMaterial.GetTexture(TextureType.BaseColour)!.Value.Path, Is.EqualTo(newBase), "the loaded RMV2 copy");
            Assert.That(mesh.RmvMaterial.GetTexture(TextureType.Mask)!.Value.Path, Is.EqualTo(Mask));
            var saved = new MaterialToRmvSerializer().CreateMaterialFromCapabilityMaterial(mesh.Material);
            Assert.That(saved.GetTexture(TextureType.BaseColour)!.Value.Path, Is.EqualTo(newBase), "the material the save writes");

            _commands.Undo();
            Assert.That(metalRough.BaseColour.TexturePath, Is.EqualTo(Base));
            Assert.That(mesh.Material.GetCapability<BloodCapability>().BloodMask.TexturePath, Is.EqualTo(Blood));
            Assert.That(mesh.RmvMaterial.GetTexture(TextureType.BaseColour)!.Value.Path, Is.EqualTo(Base));
            Assert.That(_commands.CanUndo(), Is.False, "one undo step");

            _commands.Redo();
            Assert.That(metalRough.BaseColour.TexturePath, Is.EqualTo(newBase));
            Assert.That(mesh.RmvMaterial.GetTexture(TextureType.BaseColour)!.Value.Path, Is.EqualTo(newBase));
        }

        [Test]
        public void Scope_IsEveryMeshInEveryLod_OrOnlyTheSelectedOnes()
        {
            var (model, lod0, grouped, lod1) = CreateModel();
            var outside = CreateMesh("reference");
            var viewModel = CreateViewModel(model, lod1, outside);
            viewModel.Find = "chd_hobgoblin_base";
            viewModel.Replace = "chd_ogre_base";

            Assert.That(viewModel.HasSelection, Is.True);
            Assert.That(viewModel.ChangeCount, Is.EqualTo(3), "all meshes: both LODs, and the one in a group");
            viewModel.OnlySelected = true;
            Assert.That(viewModel.ChangeCount, Is.EqualTo(1), "a selected mesh outside the editable model does not count");

            viewModel.Apply();
            Assert.That(BaseOf(lod1), Does.Contain("chd_ogre_base"));
            Assert.That(BaseOf(lod0), Is.EqualTo(Base));
            Assert.That(BaseOf(grouped), Is.EqualTo(Base));
            Assert.That(BaseOf(outside), Is.EqualTo(Base));
        }

        [Test]
        public void Case_IsIgnored_UnlessMatchCaseIsOn()
        {
            var (model, mesh, _, _) = CreateModel();
            var viewModel = CreateViewModel(model);
            viewModel.Find = "CHD_HOBGOBLIN_BASE";
            viewModel.Replace = "chd_ogre_base";
            Assert.That(viewModel.ChangeCount, Is.EqualTo(3));

            viewModel.MatchCase = true;
            Assert.That((viewModel.ChangeCount, viewModel.CanApply, viewModel.Apply()), Is.EqualTo((0, false, false)));
            Assert.That(BaseOf(mesh), Is.EqualTo(Base));

            viewModel.MatchCase = false;
            viewModel.Apply();
            Assert.That(BaseOf(mesh), Is.EqualTo(@"variantmeshes/wh3_dlc23_chd/tex/chd_ogre_base_colour.dds"));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void View_Loads_ReplaceIsEnabledOnlyWhenSomethingWouldChange_AndClosesTheWindow()
        {
            // The view in a plain window: an AssetEditorWindow reads the app's main window, which another STA test's thread owns.
            var (model, mesh, _, _) = CreateModel();
            var viewModel = CreateViewModel(model);
            var window = new Window { Content = new ReplaceTexturePathsView { DataContext = viewModel }, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000, Width = 520, Height = 300 };
            window.Show();
            try
            {
                var replaceButton = Descendants(window).OfType<Button>().Single(x => x.IsDefault);
                Assert.That(replaceButton.IsEnabled, Is.False);
                Descendants(window).OfType<TextBox>().First().Text = "hobgoblin_base";
                Assert.That((viewModel.Find, viewModel.ChangeCount, replaceButton.IsEnabled), Is.EqualTo(("hobgoblin_base", 3, true)), "the find box is live");

                Descendants(window).OfType<TextBox>().ElementAt(1).Text = "ogre_base";
                replaceButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.That((BaseOf(mesh), window.IsVisible), Is.EqualTo((@"variantmeshes/wh3_dlc23_chd/tex/chd_ogre_base_colour.dds", false)), "Replace applies, then closes the window");
            }
            finally
            {
                window.Close();
            }

            var english = new LocalizationManager();
            english.LoadLanguage("en");
            var keys = new[] { "Menu", "Title", "Find", "ReplaceWith", "MatchCase", "OnlySelected", "OnlySelectedTooltip", "ChangeCount", "Replace", "Cancel" };
            Assert.That(keys.Where(x => english.Get("KitbashTool.ReplaceTexturePaths." + x) == "KitbashTool.ReplaceTexturePaths." + x), Is.Empty);
        }

        static IEnumerable<DependencyObject> Descendants(DependencyObject parent) =>
            Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(parent)).Select(i => VisualTreeHelper.GetChild(parent, i)).SelectMany(x => Descendants(x).Prepend(x));

        [Test]
        public void NewPathsNoPackHolds_AreReportedOnce_ButStillReplaced()
        {
            var (model, mesh, _, _) = CreateModel();
            _packFileService.Setup(x => x.FindFile(It.Is<string>(p => p.Contains("ogre")), It.IsAny<IPackFileContainer?>())).Returns((PackFile?)null);
            var viewModel = CreateViewModel(model);
            viewModel.Find = "hobgoblin_base";
            viewModel.Replace = "ogre_base";
            viewModel.Apply();

            Assert.That(BaseOf(mesh), Does.Contain("ogre_base"));
            _dialogs.Verify(x => x.ShowDialogBox(It.Is<string>(m => m.Contains("chd_ogre_base_colour.dds")), It.IsAny<string>()), Times.Once);
        }
    }
}
