using System.IO;
using Editors.AnimationMeta.SuperView;
using GameWorld.Core.SceneNodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Shared.Core.Commands;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationPack;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimationMeta
{
    // A .variantmeshdefinition offers SuperView in the tool selector, beside the XML and Kitbash editors.
    public class SuperViewOpenFileTests
    {
        const string ThrotVmd = @"variantmeshes\variantmeshdefinitions\skv_throt.variantmeshdefinition";

        AssetEditorTestRunner _runner = null!;

        [SetUp]
        public void Setup()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
        }

        [Test]
        public void VariantMesh_OpensInSuperView_WithItsSkeletonAndAnimations()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);

            Assert.That(superView.CurrentFile, Is.SameAs(vmd));
            var asset = superView.SceneObjects[0];
            Assert.That(asset.Data.MeshName.Value, Is.EqualTo(vmd.Name));
            Assert.That(asset.Data.Skeleton, Is.Not.Null, "the mesh's skeleton did not load");

            // Picking an animation set and slot loads that animation's meta, as when SuperView is opened empty.
            var selection = asset.FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == @"animations/database/battle/bin/hu17_dlc16_throt.bin");
            selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "ATTACK_5");
            Assert.That(superView.MetaEditor.ParsedFile, Is.Not.Null);
            Assert.That(superView.MetaEditor.Tags, Is.Not.Empty);
        }

        // SuperView's tab goes red with its meta editors: an edited field says SuperView has unsaved changes, so its tab heard.
        [Test]
        public void VariantMesh_AnEditedTagMarksTheTabUnsaved()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == @"animations/database/battle/bin/hu17_dlc16_throt.bin");
            selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "ATTACK_5");
            var said = new List<string?>();
            superView.PropertyChanged += (_, e) => said.Add(e.PropertyName);
            Assert.That(superView.HasUnsavedChanges, Is.False, "as loaded");

            var field = superView.MetaEditor.Tags.SelectMany(x => x.Variables).First(x => !x.IsReadOnly && x.Scrub == Shared.Ui.Common.Behaviors.ScrubMode.Integer);
            field.ValueAsString = (int.Parse(field.ValueAsString) + 1).ToString();
            Assert.That((superView.HasUnsavedChanges, said.Contains(nameof(superView.HasUnsavedChanges))), Is.EqualTo((true, true)));
        }

        // The meta props always play; the main model has to as well, or it stands in its bind pose
        // under them until the "Animation" toggle is found.
        [Test]
        public void VariantMesh_MainModelPlaysThePickedSlot()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == @"animations/database/battle/bin/hu17_dlc16_throt.bin");
            selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "ATTACK_5");

            var data = superView.SceneObjects[0].Data;
            var meshes = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(data.ModelNode);
            Assert.That(meshes, Is.Not.Empty);
            Assert.That(meshes.All(x => x.AnimationPlayer == data.Player), "a mesh is not driven by the model's player");

            // One step of what AnimationsContainerComponent does every frame.
            Matrix[] Pose(double ms)
            {
                data.Player.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(ms)));
                return Enumerable.Range(0, data.Skeleton.BoneCount).Select(data.Skeleton.GetAnimatedWorldTranform).ToArray();
            }
            var start = Pose(0);
            var later = Pose(400);

            Assert.That(data.Player.IsEnabled, Is.True, "the main model's player is off");
            Assert.That(later, Is.Not.EqualTo(start), "no bone moved in 400ms of the slot's animation");
        }

        // A slot of the animation set says which weapon bones it shows (bit N for be_prop_N), and the game hides the weapons on
        // the rest: STAND_NO_WEAPON shows none, and a Doomseeker's STAND_IDLE_1 only his left flail, the right one replaced by
        // the flail its animated prop swings. SuperView drew every weapon in every slot.
        [Test]
        public void AnimationSlot_ShowsOnlyTheWeaponBonesItFlags()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == @"animations/database/battle/bin/hu17_dlc16_throt.bin");
            var weapons = SceneNodeHelper.GetChildrenOfType<SlotNode>(superView.SceneObjects[0].Data.ModelNode)
                .Where(x => x.AttachmentBoneName?.StartsWith("be_prop_") == true && x.Children.Count > 0).ToList();
            var attack = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "ATTACK_5");

            // Throt's set shows every weapon in the slots the test pack carries, so ATTACK_5 is given the flags of a slot
            // that shows only his first weapon bone, then of one that shows all six.
            var kept = weapons[0].AttachmentBoneName;
            attack.WeaponBools = 1 << int.Parse(kept["be_prop_".Length..]);
            selection.FragmentSlotList.SelectedItem = attack;
            var onlyFirst = weapons.Where(x => x.IsVisible).Select(x => x.AttachmentBoneName).ToList();

            selection.FragmentSlotList.SelectedItem = null;
            attack.WeaponBools = 0b111111;
            selection.FragmentSlotList.SelectedItem = attack;
            Assert.That((weapons.Count > 1, onlyFirst, weapons.All(x => x.IsVisible)), Is.EqualTo((true, new List<string> { kept }, true)));
        }

        // A skeleton with no battle animation table (Glottkin's only set is cam_lord_glt01_dlc29_ghurk.bin) still lists its
        // campaign animation set, and a slot from it plays with the set's persistent meta. Its hand poses and docks take the
        // battle slot names the meta's HandPose and DockEquipment tags look up.
        [Test]
        public void VariantMesh_ListsItsCampaignAnimationSet()
        {
            var battle = PackFileServiceUtility.GetAllAnimPacks(_runner.PackFileService)
                .SelectMany(x => AnimationPackSerializer.Load(x, _runner.PackFileService).GetGenericAnimationSets())
                .First(x => x.FullPath == @"animations/database/battle/bin/hu17_dlc16_throt.bin");
            var attack = battle.Entries.First(x => x.SlotName == "ATTACK_5");
            var persist = battle.Entries.First(x => x.SlotName == "PERSISTENT_METADATA_ALIVE");
            var bin = new CampaignAnimationBin
            {
                Version = 3,
                SkeletonName = battle.SkeletonName,
                Status =
                [
                    new()
                    {
                        Name = "global",
                        PersitantMetaData = [new() { Animation = "", Type = "global", AnimationMeta = persist.MetaFile, SoundMeta = "" }],
                        Poses = [new() { Animation = attack.AnimationFile, Type = "global", AnimationMeta = "", SoundMeta = "", PoseId = 3 }],
                        Docks = [new() { Animation = attack.AnimationFile, Type = "global", AnimationMeta = "", SoundMeta = "", Dock = "DOCK_EQPT_RHAND_2" }],
                    },
                    new() { Name = "status_battle", Action = [new() { Animation = attack.AnimationFile, Type = "global", Meta = attack.MetaFile, SoundMeta = "", ActionType = "battle_fatality", ActionId = 1 }] },
                ],
            };
            var pack = _runner.CreateOutputPack();
            _runner.PackFileService.AddFilesToPack(pack, [new NewPackFileEntry(@"animations\campaign\database\bin", PackFile.CreateFromBytes("cam_throt.bin", CampaignAnimationBinLoader.Write(bin, "cam_throt")))]);

            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var selection = superView.SceneObjects[0].FragAndSlotSelection;
            selection.FragmentList.SelectedItem = selection.FragmentList.PossibleValues.First(x => x.FullPath == @"animations/campaign/database/bin/cam_throt.bin");
            selection.FragmentSlotList.SelectedItem = selection.FragmentSlotList.PossibleValues.First(x => x.SlotName == "status_battle/battle_fatality");
            Assert.That(superView.MetaEditor.ParsedFile, Is.Not.Null);
            Assert.That((selection.MetaDataName, selection.MetaDataPersistName), Is.EqualTo((attack.MetaFile, persist.MetaFile)));
            Assert.That(selection.FragmentSlotList.PossibleValues.Select(x => x.SlotName), Is.SupersetOf(new[] { "HAND_POSE_GRIP", "DOCK_EQUIPMENT_RIGHT_HAND_2" }));
            // A campaign set has no weapon flags, so its slots hide no weapon.
            var weapons = SceneNodeHelper.GetChildrenOfType<SlotNode>(superView.SceneObjects[0].Data.ModelNode).Where(x => x.AttachmentBoneName?.StartsWith("be_prop_") == true).ToList();
            Assert.That((weapons.Count > 0, weapons.All(x => x.IsVisible)), Is.EqualTo((true, true)), "a campaign slot hid a weapon");
        }

        [Test]
        public void VariantMesh_AlreadyOpenInKitbash_StillOpensInSuperView()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var open = _runner.CommandFactory.Create<OpenEditorCommand>();
            open.Execute(vmd, EditorEnums.Kitbash_Editor);

            Assert.That(open.Execute(vmd, EditorEnums.SuperView_Editor), Is.InstanceOf<SuperViewViewModel>());
        }

        // A vfx file added or saved in any pack (the VFX editor's save, a replaced texture) rebuilds the scene's effects, so
        // SuperView shows it without the tab being reopened, and so does a model, material or animation a rigid particle may
        // draw, wherever it lives; any other file leaves the scene alone.
        [TestCase(@"vfx\test_smoke.xml", true)]
        [TestCase(@"variantmeshes\wh_variantmodels\x\x.rigid_model_v2", true)]
        [TestCase(@"variantmeshes\wh_variantmodels\x\x.wsmodel", true)]
        [TestCase(@"variantmeshes\wh_variantmodels\x\x.xml.material", true)]
        [TestCase(@"animations\battle\x\x.anim", true)]
        [TestCase(@"text\notes.txt", false)]
        public void AChangedVfxFile_RebuildsTheScene(string path, bool rebuilds)
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var data = superView.SceneObjects[0].Data;
            var pack = _runner.CreateOutputPack();

            var before = data.MetaDataItems;
            _runner.PackFileService.AddFilesToPack(pack, [new NewPackFileEntry(Path.GetDirectoryName(path)!, PackFile.CreateFromBytes(Path.GetFileName(path), [1]))]);
            Assert.That(data.MetaDataItems, rebuilds ? Is.Not.SameAs(before) : Is.SameAs(before), "on add");

            before = data.MetaDataItems;
            _runner.PackFileService.SaveFile(_runner.PackFileService.FindFile(path, pack)!, [2]);
            Assert.That(data.MetaDataItems, rebuilds ? Is.Not.SameAs(before) : Is.SameAs(before), "on save");
        }

        // The bar above the 3D view, beside Camera, opens the Kitbash editor's render settings, the light that lights the models
        // and lit smoke, and takes a screenshot. The side panel, under Show VFX, no longer carries them.
        [Test, Apartment(ApartmentState.STA)]
        public void TheViewBarOffersTheRenderSettingsAndAScreenshot()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            // The app-level converters, as App.xaml declares them, which the test has no App.xaml to give it.
            var app = System.Windows.Application.Current ?? new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            static Shared.Ui.Common.ValueConverters.BoolToVisibilityConverter Vis(System.Windows.Visibility t, System.Windows.Visibility f) => new() { TrueValue = t, FalseValue = f };
            static Shared.Ui.Common.ValueConverters.BoolToColourConverter Brush(System.Windows.Media.Color t, System.Windows.Media.Color f) => new() { TrueValue = new(t), FalseValue = new(f) };
            var (visible, hidden, collapsed) = (System.Windows.Visibility.Visible, System.Windows.Visibility.Hidden, System.Windows.Visibility.Collapsed);
            app.Resources["BoolToCollapsedConverter"] = Vis(visible, collapsed);
            app.Resources["BoolToHiddenConverter"] = Vis(visible, hidden);
            app.Resources["InvBoolToHiddenConverter"] = Vis(hidden, visible);
            app.Resources["NullToVisibilityConverter"] = new Shared.Ui.Common.ValueConverters.NullVisibilityConverter();
            app.Resources["ColourConverter_black_red"] = Brush(System.Windows.Media.Colors.Black, System.Windows.Media.Colors.Red);
            app.Resources["BoolToColour_white_red"] = Brush(System.Windows.Media.Colors.White, System.Windows.Media.Colors.Red);
            app.Resources["BoolToColour_black_gray"] = Brush(System.Windows.Media.Colors.Black, System.Windows.Media.Colors.Gray);
            app.Resources["BoolToColour_white_gray"] = Brush(System.Windows.Media.Colors.White, System.Windows.Media.Colors.Gray);
            app.Resources["InvBoolConverter"] = new Shared.Ui.Common.ValueConverters.InverseBooleanConverter();
            app.Resources["BoolToChangedPrefixStr"] = new Shared.Ui.Common.ValueConverters.BoolToStringConverter { TrueValue = "*" };
            var view = new AnimationEditor.Common.BaseControl.EditorHostView { DataContext = superView };
            view.Measure(new System.Windows.Size(1400, 900));
            view.Arrange(new System.Windows.Rect(0, 0, 1400, 900));
            view.UpdateLayout();
            var panel = Descendants(view).OfType<EditorView>().Single();
            var bar = Descendants(view).OfType<System.Windows.Controls.MenuItem>().Select(x => x.Command).ToList();
            var side = Descendants(panel).OfType<System.Windows.Controls.Button>().Select(x => x.Command).ToList();
            System.Windows.Input.ICommand[] both =[superView.OpenRenderSettingsCommand, superView.TakeScreenshotCommand];
            Assert.That((both.All(bar.Contains), both.Any(side.Contains)), Is.EqualTo((true, false)));
        }

        // A screenshot is the next frame at twice the view's size, named after the file, saved where PhotoStudio saves its own.
        [Test]
        public void AScreenshotSavesTheNextFrameAtTwiceItsSize()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            var superView = _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var folder = Path.Combine(Path.GetTempPath(), "superview_shots_" + Guid.NewGuid().ToString("N"));
            (superView.ScreenshotFolder, superView.OpenScreenshotFolder) = (folder, false);
            try
            {
                superView.TakeScreenshotCommand.Execute(null);
                var engine = _runner.GetRequiredServiceInCurrentEditorScope<GameWorld.Core.Components.Rendering.RenderEngineComponent>();
                var device = _runner.ServiceProvider.GetRequiredService<GameWorld.Core.WpfWindow.IWpfGame>().GraphicsDevice;
                using var target = new Microsoft.Xna.Framework.Graphics.RenderTarget2D(device, 64, 48, false,
                    Microsoft.Xna.Framework.Graphics.SurfaceFormat.Color, Microsoft.Xna.Framework.Graphics.DepthFormat.Depth24);
                device.SetRenderTarget(target);
                engine.Draw(new GameTime());
                device.SetRenderTarget(null);
                var shot = Directory.GetFiles(folder, "*.png").Single();
                var frame = System.Windows.Media.Imaging.BitmapDecoder.Create(new Uri(shot), System.Windows.Media.Imaging.BitmapCreateOptions.None,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
                Assert.That((Path.GetFileName(shot).StartsWith("skv_throt_"), frame.PixelWidth, frame.PixelHeight), Is.EqualTo((true, 128, 96)));
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }

        // Faction colours tint the parts the mask texture marks, as in game, so picking them in the render settings shows on the
        // model. The material that WH3 models load with had no tint, and the colours reached nothing.
        [Test]
        public void FactionColoursTintTheMaskedParts()
        {
            var vmd = _runner.PackFileService.FindFile(ThrotVmd)!;
            _runner.CommandFactory.Create<OpenEditorCommand>().Execute<SuperViewViewModel>(vmd, EditorEnums.SuperView_Editor);
            var store = _runner.GetRequiredServiceInCurrentEditorScope<GameWorld.Core.Components.Rendering.SceneRenderParametersStore>();

            // Throt has no faction-coloured parts, as legendary characters do not: his masks' colour channels are empty. Every
            // mesh takes a white mask instead, so all of it is faction coloured.
            var white = System.Windows.Media.Imaging.BitmapSource.Create(4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null,
                Enumerable.Repeat((byte)255, 4 * 4 * 4).ToArray(), 16);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(white));
            using var png = new MemoryStream();
            encoder.Save(png);
            _runner.PackFileService.AddFilesToPack(_runner.CreateOutputPack(), [new NewPackFileEntry("test", PackFile.CreateFromBytes("white_mask.png", png.ToArray()))]);
            static IEnumerable<GameWorld.Core.SceneNodes.ISceneNode> Every(GameWorld.Core.SceneNodes.ISceneNode node) => node.Children.SelectMany(x => Every(x).Prepend(x));
            var meshes = Every(_runner.GetRequiredServiceInCurrentEditorScope<GameWorld.Core.Components.SceneManager>().RootNode).OfType<GameWorld.Core.SceneNodes.Rmv2MeshNode>().ToList();
            foreach (var mask in meshes.Select(x => x.Material.GetCapability<GameWorld.Core.Rendering.Materials.Capabilities.MetalRoughCapability>().Mask))
                (mask.TexturePath, mask.UseTexture) = (@"test\white_mask.png", true);

            // Each material WH3 models load with, alone: Throt's head takes the default one and his body the emissive one.
            var tinted = new Dictionary<GameWorld.Core.Rendering.Materials.Shaders.CapabilityMaterialsEnum, int>();
            foreach (var type in meshes.Select(x => x.Material.Type).Distinct())
            {
                foreach (var mesh in meshes)
                    mesh.IsVisible = mesh.Material.Type == type;
                (store.FactionColour0, store.FactionColour1, store.FactionColour2) = (Vector3.UnitY, Vector3.UnitY, Vector3.UnitY);
                var green = RenderFrame();
                (store.FactionColour0, store.FactionColour1, store.FactionColour2) = (Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ);
                var blue = RenderFrame();
                tinted[type] = green.Zip(blue).Count(x => Math.Abs(x.First.G - x.Second.G) + Math.Abs(x.First.B - x.Second.B) > 16);
            }
            Assert.That(tinted.Keys, Is.EquivalentTo(new[] { GameWorld.Core.Rendering.Materials.Shaders.CapabilityMaterialsEnum.MetalRoughPbr_Default, GameWorld.Core.Rendering.Materials.Shaders.CapabilityMaterialsEnum.MetalRoughPbr_Emissive }));
            Assert.That(tinted.Values, Is.All.GreaterThan(100), string.Join(", ", tinted));
        }

        // One frame of the editor's scene, the model framed from the front, as the app draws it.
        Microsoft.Xna.Framework.Color[] RenderFrame()
        {
            var device = _runner.ServiceProvider.GetRequiredService<GameWorld.Core.WpfWindow.IWpfGame>().GraphicsDevice;
            _runner.GetRequiredServiceInCurrentEditorScope<GameWorld.Core.Components.Rendering.ArcBallCamera>().ViewMatrixOverride =
                Matrix.CreateLookAt(new Vector3(0, 1.2f, 4), new Vector3(0, 1, 0), Vector3.Up);
            var components = _runner.GetRequiredServiceInCurrentEditorScope<IEnumerable<IGameComponent>>().ToList();
            using var target = new Microsoft.Xna.Framework.Graphics.RenderTarget2D(device, 256, 192, false,
                Microsoft.Xna.Framework.Graphics.SurfaceFormat.Color, Microsoft.Xna.Framework.Graphics.DepthFormat.Depth24);
            var time = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1 / 60.0));
            device.SetRenderTarget(target);
            foreach (var component in components.OfType<IUpdateable>().OrderBy(x => x.UpdateOrder))
                component.Update(time);
            foreach (var component in components.OfType<IDrawable>().OrderBy(x => x.DrawOrder))
                component.Draw(time);
            device.SetRenderTarget(null);
            var pixels = new Microsoft.Xna.Framework.Color[target.Width * target.Height];
            target.GetData(pixels);
            return pixels;
        }

        static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject parent)
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                yield return child;
                foreach (var descendant in Descendants(child))
                    yield return descendant;
            }
        }
    }
}
