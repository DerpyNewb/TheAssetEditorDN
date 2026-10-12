using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Editors.VfxEditor.Preview;
using Editors.VfxEditor.ViewModels;
using Editors.VfxEditor.Views;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Grid;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Vfx;
using GameWorld.Core.WpfWindow;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.Commands;
using Shared.Core.Services;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;
using Vector3 = Microsoft.Xna.Framework.Vector3;
using Xna = Microsoft.Xna.Framework;

namespace Test.VfxEditor
{
    // Opens a vfx file the way the app does (its DI container, editor database and game window) and drives the tab.
    class VfxEditorAppTests
    {
        [Test]
        public void A_vfx_file_opens_in_the_editor_takes_a_linked_edit_and_saves()
        {
            var runner = new AssetEditorTestRunner();
            var source = runner.CreateEmptyPackFile("source", false);
            runner.PackFileService.AddFilesToPack(source, [new NewPackFileEntry("vfx", PackFile.CreateFromBytes("fx.xml", Fixtures.Utf16(Fixtures.Default)))]);
            var output = runner.CreateOutputPack();

            var editor = Open(runner);
            Assert.That(editor.Tree.Select(n => n.Label), Is.EqualTo(new[] { "Effect", "Emitters", "References", "Shader library", "Editor data" }));

            editor.SelectedNode = editor.Tree[1].Children[0];
            string Value(string label) => editor.Rows.Single(r => r.Label == label).Value;
            Assert.That(Value("A"), Is.EqualTo("1"));
            editor.Rows.Single(r => r.Label == "A").Value = "2";   // Ultra and High held 1, Medium 5
            Assert.That(editor.HasUnsavedChanges, Is.True);
            editor.Tier = 1;
            Assert.That(Value("A"), Is.EqualTo("2"));
            editor.Tier = 2;
            Assert.That(Value("A"), Is.EqualTo("5"));

            Assert.That(editor.Save(), Is.True);
            var saved = runner.PackFileService.FindFile("vfx\\fx.xml", output)!.DataSource.ReadData();
            Assert.That(Encoding.Unicode.GetString(saved, 2, saved.Length - 2), Is.EqualTo(Fixtures.Default
                .Replace("\"A\" quality=\"0\" value=\"1\"", "\"A\" quality=\"0\" value=\"2\"")
                .Replace("\"A\" quality=\"1\" value=\"1\"", "\"A\" quality=\"1\" value=\"2\"")));
            Assert.That(editor.HasUnsavedChanges, Is.False);
        }

        // A text box writes its value when it loses focus, and the toolbar takes no focus from it (a ToolBar is a focus
        // scope), nor does Ctrl+S. Picking a tier or saving must still take the typed value, at the tier it was typed at.
        [Test, Apartment(ApartmentState.STA)]
        public void A_typed_value_lands_before_the_tier_changes_and_before_a_save()
        {
            var runner = new AssetEditorTestRunner();
            var source = runner.CreateEmptyPackFile("source", false);
            runner.PackFileService.AddFilesToPack(source, [new NewPackFileEntry("vfx", PackFile.CreateFromBytes("fx.xml", Fixtures.Utf16(Fixtures.Default)))]);
            var output = runner.CreateOutputPack();
            var editor = Open(runner);
            var view = new VfxEditorView { DataContext = editor };
            editor.SelectedNode = editor.Tree[1].Children[0];
            TextBox Box()
            {
                view.Measure(new Size(1600, 900));
                view.Arrange(new Rect(0, 0, 1600, 900));
                view.UpdateLayout();
                return Descendants(view).OfType<TextBox>().First(b => b.DataContext is PropertyRow { Label: "A" });
            }

            Box().Text = "2";
            Assert.That(editor.HasUnsavedChanges, Is.False);   // typed, not yet written
            editor.Tier = 1;                                     // what picking High does
            Assert.That(editor.Rows.Single(r => r.Label == "A").Value, Is.EqualTo("2"));

            Box().Text = "3";
            editor.SaveFileCommand.Execute(null);
            var saved = runner.PackFileService.FindFile("vfx\\fx.xml", output)!.DataSource.ReadData();
            Assert.That(Encoding.Unicode.GetString(saved, 2, saved.Length - 2),
                Does.Contain("<property name=\"A\" quality=\"0\" value=\"3\"/>\r\n\t\t\t<property name=\"A\" quality=\"1\" value=\"3\"/>"));
        }

        // The vocabulary arrives seconds after the first editor opens and rebuilds the rows; a value typed by then is kept.
        [Test, Apartment(ApartmentState.STA)]
        public void A_value_typed_before_the_vocabulary_arrives_is_kept()
        {
            var runner = new AssetEditorTestRunner();
            var source = runner.CreateEmptyPackFile("source", false);
            runner.PackFileService.AddFilesToPack(source, [new NewPackFileEntry("vfx", PackFile.CreateFromBytes("fx.xml", Fixtures.Utf16(Fixtures.Default)))]);
            runner.CreateOutputPack();
            var editor = Open(runner);
            var view = new VfxEditorView { DataContext = editor };
            editor.SelectedNode = editor.Tree[1].Children[0];
            view.Measure(new Size(1600, 900));
            view.Arrange(new Rect(0, 0, 1600, 900));
            view.UpdateLayout();

            Descendants(view).OfType<TextBox>().First(b => b.DataContext is PropertyRow { Label: "A" }).Text = "2";
            editor.OnVocabularyArrived();

            Assert.That(editor.Rows.Single(r => r.Label == "A").Value, Is.EqualTo("2"));
        }

        // The Add pickers are bound as the view binds them; refilling their lists must not drop the name picked.
        [Test, Apartment(ApartmentState.STA)]
        public void A_name_picked_for_add_survives_a_value_edit()
        {
            var runner = new AssetEditorTestRunner();
            var source = runner.CreateEmptyPackFile("source", false);
            runner.PackFileService.AddFilesToPack(source, [
                new NewPackFileEntry("vfx", PackFile.CreateFromBytes("fx.xml", Fixtures.Utf16(Fixtures.Default))),
                new NewPackFileEntry("vfx", PackFile.CreateFromBytes("lib.xml", Fixtures.Utf16(Fixtures.Effect(emitters: Fixtures.Emitters("a", "b", "c")))))]);
            runner.CreateOutputPack();
            var editor = Open(runner);
            editor.SelectedNode = editor.Tree[2].Children[0];
            var picker = new ComboBox { DataContext = editor };
            picker.SetBinding(ItemsControl.ItemsSourceProperty, "OverrideChoices");
            picker.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, "NewOverrideName");
            editor.NewOverrideName = "c";

            editor.Rows.Single(r => r.Label == "scale_min").Value = "2.00";

            Assert.That((editor.HasUnsavedChanges, editor.NewOverrideName), Is.EqualTo((true, "c")));
        }

        // A tree click commits the box it leaves; rebuilding the tree then would drop the item being clicked.
        [Test]
        public void An_edit_that_only_renames_a_node_keeps_the_tree()
        {
            var runner = new AssetEditorTestRunner();
            var source = runner.CreateEmptyPackFile("source", false);
            runner.PackFileService.AddFilesToPack(source, [new NewPackFileEntry("vfx", PackFile.CreateFromBytes("fx.xml", Fixtures.Utf16(Fixtures.Default)))]);
            runner.CreateOutputPack();
            var editor = Open(runner);
            var reference = editor.Tree[2].Children[0];
            editor.SelectedNode = reference;

            editor.Rows.Single(r => r.Label == "vfx_ref").Value = "lib2";

            Assert.That(editor.Tree[2].Children[0], Is.SameAs(reference));
            Assert.That(reference.Label, Does.StartWith("lib2"));
        }

        [Test]
        public void A_preview_input_that_is_not_a_finite_number_is_ignored()
        {
            var (_, editor, _) = OpenWithAGuideModelOffered();
            editor.LoopLength = double.NaN;
            editor.Speed = double.PositiveInfinity;
            editor.ProjectileSpeed = double.NaN;
            Assert.That((editor.LoopLength, editor.Speed, editor.ProjectileSpeed), Is.EqualTo((4d, 1d, 60d)));
        }

        [Test]
        public void An_effect_cannot_reference_itself()
        {
            var runner = new AssetEditorTestRunner();
            var source = runner.CreateEmptyPackFile("source", false);
            runner.PackFileService.AddFilesToPack(source, [new NewPackFileEntry("vfx", PackFile.CreateFromBytes("fx.xml", Fixtures.Utf16(Fixtures.Default)))]);
            runner.CreateOutputPack();
            var editor = Open(runner);
            var references = editor.Tree[2].Children.Count;

            editor.NewReferenceTarget = "FX.xml";
            editor.AddReferenceCommand.Execute(null);

            Assert.That((editor.Tree[2].Children.Count, editor.HasUnsavedChanges), Is.EqualTo((references, false)));
            Assert.That(editor.Status, Does.Contain("itself"));
        }

        [Test]
        public void A_linked_edit_says_which_tiers_it_left_alone()
        {
            var runner = new AssetEditorTestRunner();
            var source = runner.CreateEmptyPackFile("source", false);
            runner.PackFileService.AddFilesToPack(source, [new NewPackFileEntry("vfx", PackFile.CreateFromBytes("fx.xml", Fixtures.Utf16(Fixtures.Default)))]);
            runner.CreateOutputPack();
            var editor = Open(runner);
            VfxTreeNode FirstColour() => editor.Tree[1].Children[0].Children[0].Children.First(n => n.Fragment == "vs_colour");

            editor.Tier = 1;
            editor.ThisTierOnly = true;
            editor.SelectedNode = FirstColour();
            editor.RemoveModifierCommand.Execute(null);
            editor.Tier = 0;
            editor.ThisTierOnly = false;
            editor.SelectedNode = FirstColour();
            editor.RemoveModifierCommand.Execute(null);

            Assert.That(editor.Status, Does.Contain("High"));
        }

        // Opened from Tools, the tab has no file: it says so, and Open picks one from the packs, asking first when the
        // open file has edits that are not saved.
        [Test]
        public void The_tools_menu_opens_the_editor_and_open_picks_a_file_from_the_packs()
        {
            var runner = new AssetEditorTestRunner();
            var source = runner.CreateEmptyPackFile("source", false);
            runner.PackFileService.AddFilesToPack(source, [new NewPackFileEntry("vfx", PackFile.CreateFromBytes("fx.xml", Fixtures.Utf16(Fixtures.Default)))]);
            runner.CreateOutputPack();
            var file = runner.PackFileService.FindFile("vfx\\fx.xml")!;
            runner.Dialogs.Setup(x => x.DisplayBrowseDialog(It.IsAny<List<string>>())).Returns(new BrowseDialogResultFile(true, file));

            var database = (IEditorDatabase)runner.ServiceProvider.GetService(typeof(IEditorDatabase))!;
            var info = database.GetEditorInfos().Single(x => x.EditorEnum == EditorEnums.Vfx_Editor);
            Assert.That((info.AddToolbarButton, info.ToolbarName, info.IsToolbarButtonEnabled), Is.EqualTo((true, "VFX Editor", true)));

            var editor = Open(runner, null);
            Assert.That((editor.Tree.Count, editor.CurrentFile), Is.EqualTo((0, (PackFile?)null)));
            Assert.That(editor.LoadError, Does.Contain("Open"));

            editor.OpenFileCommand.Execute(null);
            Assert.That((editor.DisplayName, editor.Tree.Count, editor.HasLoadError, editor.CurrentFile), Is.EqualTo(("fx.xml", 5, false, file)));

            editor.SelectedNode = editor.Tree[1].Children[0];
            editor.Rows.Single(r => r.Label == "A").Value = "2";
            runner.Dialogs.Setup(x => x.ShowYesNoBox(It.IsAny<string>(), It.IsAny<string>())).Returns(ShowMessageBoxResult.Cancel);
            editor.OpenFileCommand.Execute(null);
            Assert.That((editor.HasUnsavedChanges, editor.Rows.Single(r => r.Label == "A").Value), Is.EqualTo((true, "2")));
        }

        // A model picked as a guide stands where the effect is: at the origin standing still, along the flight when the
        // effect flies, and it stays in the scene when the preview is rebuilt.
        [Test]
        public void A_guide_model_rides_where_the_effect_is()
        {
            var (runner, editor, preview) = OpenWithAGuideModelOffered();

            editor.LoadModelCommand.Execute(null);
            Assert.That(editor.ModelName, Is.EqualTo("emp_karl_franz.rigid_model_v2"));
            editor.PreviewTime = 0.25;
            Assert.That(preview.Model!.ModelMatrix.Translation, Is.EqualTo(Vector3.Zero));

            editor.LoopLength = 1;
            editor.ProjectileSpeed = 60;
            editor.ProjectileAxis = 0;   // +Z
            editor.Projectile = true;
            editor.PreviewTime = 0.25;
            var node = preview.Model!;
            var root = runner.GetRequiredServiceInCurrentEditorScope<SceneManager>().RootNode;
            Assert.That((node.ModelMatrix.Translation, root.Children.Contains(node)), Is.EqualTo((new Vector3(0, 0, -15), true)));

            editor.ClearModelCommand.Execute(null);
            Assert.That((preview.Model, editor.ModelName, root.Children.Contains(node)), Is.EqualTo(((SceneNode?)null, (string?)null, false)));
        }

        // Static draws from the projectile's frame: it and its model hold the centre while the particles, the trail, are
        // drawn shifted back by the flight. Switching the view back re-places both without moving the time.
        [Test]
        public void A_static_view_holds_the_projectile_at_the_centre_and_shifts_the_trail_back()
        {
            var (runner, editor, preview) = OpenWithAGuideModelOffered();
            editor.LoadModelCommand.Execute(null);
            editor.LoopLength = 1;
            editor.ProjectileSpeed = 60;
            editor.ProjectileAxis = 0;   // +Z
            editor.Projectile = true;
            editor.StaticView = true;
            editor.PreviewTime = 0.25;
            var effect = runner.GetRequiredServiceInCurrentEditorScope<SceneManager>().RootNode.Children.OfType<VfxEffectNode>().Single();
            Assert.That((preview.Model!.ModelMatrix.Translation, effect.ModelMatrix.Translation), Is.EqualTo((Vector3.Zero, new Vector3(0, 0, 15))));

            editor.StaticView = false;
            Assert.That((preview.Model!.ModelMatrix.Translation, effect.ModelMatrix.Translation), Is.EqualTo((new Vector3(0, 0, -15), Vector3.Zero)));
        }

        // The simulator runs whole 1/60 s steps, so at 0.26 s the particles are at 0.25 s. The model and the Static view
        // follow the particles, or the trail would shake against them by up to one step's flight.
        [TestCase(false, -15f, 0f)]
        [TestCase(true, 0f, 15f)]
        public void The_model_and_the_static_view_follow_the_simulated_time(bool staticView, float modelZ, float effectZ)
        {
            var (runner, editor, preview) = OpenWithAGuideModelOffered();
            editor.LoadModelCommand.Execute(null);
            editor.LoopLength = 1;
            editor.ProjectileSpeed = 60;
            editor.ProjectileAxis = 0;   // +Z
            editor.Projectile = true;
            editor.StaticView = staticView;
            editor.PreviewTime = 0.26;
            var effect = runner.GetRequiredServiceInCurrentEditorScope<SceneManager>().RootNode.Children.OfType<VfxEffectNode>().Single();
            Assert.That(preview.Model!.ModelMatrix.Translation.Z, Is.EqualTo(modelZ).Within(1e-3));
            Assert.That(effect.ModelMatrix.Translation.Z, Is.EqualTo(effectZ).Within(1e-3));
        }

        [Test]
        public void A_projectile_flies_along_plus_z_unless_told_otherwise()
        {
            var (_, editor, preview) = OpenWithAGuideModelOffered();
            editor.LoadModelCommand.Execute(null);
            editor.LoopLength = 1;
            editor.ProjectileSpeed = 60;
            editor.Projectile = true;
            editor.PreviewTime = 0.25;
            Assert.That((editor.AxisNames[editor.ProjectileAxis], preview.Model!.ModelMatrix.Translation), Is.EqualTo(("+Z", new Vector3(0, 0, -15))));
        }

        [Test]
        public void The_grid_can_be_switched_off()
        {
            var (runner, editor, _) = OpenWithAGuideModelOffered();
            var grid = runner.GetRequiredServiceInCurrentEditorScope<GridComponent>();
            Assert.That((editor.ShowGrid, grid.ShowGrid), Is.EqualTo((true, true)));
            editor.ShowGrid = false;
            Assert.That(grid.ShowGrid, Is.False);
        }

        // The list under the viewport takes no room while the preview has nothing to say, and some once it does.
        [Test, Apartment(ApartmentState.STA)]
        public void The_preview_list_takes_no_room_while_it_is_empty()
        {
            var (_, editor, _) = OpenWithAGuideModelOffered();
            var view = new VfxEditorView { DataContext = editor };
            double ListHeight()
            {
                view.Measure(new Size(1600, 900));
                view.Arrange(new Rect(0, 0, 1600, 900));
                view.UpdateLayout();
                return Descendants(view).OfType<ListBox>().Single(x => x.ItemsSource == editor.PreviewLines).ActualHeight;
            }

            editor.PreviewLines.Clear();
            Assert.That(ListHeight(), Is.Zero);
            editor.PreviewLines.Add("a line");
            Assert.That(ListHeight(), Is.GreaterThan(10));
        }

        // New starts an unsaved effect; New emitter on its Emitters node gives it an emitter with what vanilla's usually carry
        // (here, the one vanilla file in the packs); Save asks where, as Save As does.
        [Test]
        public void A_new_effect_gets_a_usual_emitter_and_saves_where_it_is_told()
        {
            var runner = new AssetEditorTestRunner();
            var source = runner.CreateEmptyPackFile("source", false);
            runner.PackFileService.AddFilesToPack(source, [new NewPackFileEntry("vfx", PackFile.CreateFromBytes("fx.xml", Fixtures.Utf16(Fixtures.Default)))]);
            source.IsCaPackFile = true;   // the vocabulary reads CA's packs
            var output = runner.CreateOutputPack();
            var editor = Open(runner, path: null);

            editor.NewFileCommand.Execute(null);
            Assert.That((editor.IsLoaded, editor.HasUnsavedChanges, editor.CurrentFile), Is.EqualTo((true, true, (PackFile?)null)));
            editor.SelectedNode = editor.Tree[1];
            editor.NewEmitterId = "puff";
            editor.NewEmitterCommand.Execute(null);
            var puff = editor.Tree[1].Children.Single();
            Assert.That(puff.Label, Is.EqualTo("puff (continuous_mesh)"));
            Assert.That(puff.Children[0].Children.Select(n => n.Label), Is.EqualTo(new[] { "ps_texture", "vs_colour" }));

            runner.Dialogs.Setup(x => x.DisplaySaveDialog(It.IsAny<IPackFileService>(), It.IsAny<List<string>>())).Returns(new SaveDialogResult(true, null, @"vfx\my_puff.xml"));
            Assert.That(editor.Save(), Is.True);
            Assert.That(runner.PackFileService.FindFile(@"vfx\my_puff.xml", output), Is.Not.Null);
            Assert.That(editor.CurrentFile?.Name, Is.EqualTo("my_puff.xml"));
        }

        // The two parts of the file that are not the effect: the shader library its attributes edit, and Terry's list of
        // components, shown and kept in step by the edits rather than edited.
        [Test]
        public void The_shader_library_and_editor_data_are_in_the_tree()
        {
            var runner = new AssetEditorTestRunner();
            var source = runner.CreateEmptyPackFile("source", false);
            runner.PackFileService.AddFilesToPack(source, [new NewPackFileEntry("vfx", PackFile.CreateFromBytes("fx.xml", Fixtures.Utf16(Fixtures.WithComponents)))]);
            var editor = Open(runner);
            Assert.That(editor.Tree[4].Children.Select(n => n.Label), Is.EqualTo(new[] { "e1", "xref" }));
            editor.SelectedNode = editor.Tree[3];
            Assert.That(editor.Rows.Select(r => (r.Label, r.Value)), Is.EqualTo(new[] { ("file", "fx/particle_vfx_library.hlsl") }));
        }

        // A texture path row shows the texture it names and browses the packs for another; a material path row opens the
        // material in the app's text editor.
        [Test]
        public void A_texture_row_shows_and_browses_and_a_material_row_opens()
        {
            var runner = new AssetEditorTestRunner();
            var source = runner.CreateEmptyPackFile("source", false);
            var effect = Fixtures.Default.Replace("<texture_property name=\"diffuse\" path=\"vfx/a.dds\"/>",
                "<texture_property name=\"diffuse\" path=\"vfx/a.dds\"/><property name=\"material\" type=\"file\" path=\"vfx\\materials\\m.xml.material\"/>");
            runner.PackFileService.AddFilesToPack(source, [
                new NewPackFileEntry("vfx", PackFile.CreateFromBytes("fx.xml", Fixtures.Utf16(effect))),
                new NewPackFileEntry("vfx", PackFile.CreateFromBytes("a.dds", Fixtures.Dds(4, 2, 255, 0, 0, 255))),
                new NewPackFileEntry(@"vfx\textures", PackFile.CreateFromBytes("b.dds", Fixtures.Dds(8, 8, 0, 255, 0, 255))),
                new NewPackFileEntry(@"vfx\materials", PackFile.CreateFromBytes("m.xml.material", Encoding.UTF8.GetBytes("<PROPERTY_SET/>")))]);
            runner.CreateOutputPack();
            var editor = Open(runner);
            editor.SelectedNode = editor.Tree[1].Children[0].Children[0].Children.First(n => n.Fragment == "ps_texture");
            PropertyRow Row(string label) => editor.Rows.Single(r => r.Label == label);

            Assert.That(Row("diffuse path").Thumbnail, Is.InstanceOf<BitmapSource>().With.Property("PixelWidth").EqualTo(4));
            var b = runner.PackFileService.FindFile(@"vfx\textures\b.dds")!;
            runner.Dialogs.Setup(x => x.DisplayBrowseDialog(It.IsAny<List<string>>())).Returns(new BrowseDialogResultFile(true, b));
            editor.BrowseFileCommand.Execute(Row("diffuse path"));
            Assert.That(Row("diffuse path").Value, Is.EqualTo(@"vfx\textures\b.dds").IgnoreCase);
            Assert.That(Row("diffuse path").Thumbnail, Is.InstanceOf<BitmapSource>().With.Property("PixelWidth").EqualTo(8));

            editor.OpenPathCommand.Execute(Row("material path"));
            var material = runner.PackFileService.FindFile(@"vfx\materials\m.xml.material");
            Assert.That(runner.ServiceProvider.GetRequiredService<IEditorManager>().GetAllEditors().OfType<IFileEditor>().Select(e => e.CurrentFile), Does.Contain(material));
        }

        // The curve graph's points reach the screen: a binding that cannot convert them only logs, and draws an empty box.
        [Test, Apartment(ApartmentState.STA)]
        public void A_curve_row_draws_its_graph()
        {
            var runner = new AssetEditorTestRunner();
            var source = runner.CreateEmptyPackFile("source", false);
            runner.PackFileService.AddFilesToPack(source, [new NewPackFileEntry("vfx", PackFile.CreateFromBytes("fx.xml", Fixtures.Utf16(Fixtures.Default)))]);
            var editor = Open(runner);
            var view = new VfxEditorView { DataContext = editor };
            editor.SelectedNode = editor.Tree[1].Children[0];
            view.Measure(new Size(1600, 900));
            view.Arrange(new Rect(0, 0, 1600, 900));
            view.UpdateLayout();
            var lines = Descendants(view).OfType<System.Windows.Shapes.Polyline>().ToList();
            Assert.That(lines.Select(l => l.Points.Count), Is.EqualTo(new[] { 61, 61 }), "the Curve fixture's two channels");
        }

        // Rename takes the id across the emitter's records and keeps its node picked; the type picker writes its type.
        [Test]
        public void An_emitter_is_renamed_and_retyped_from_its_node()
        {
            var runner = new AssetEditorTestRunner();
            var source = runner.CreateEmptyPackFile("source", false);
            runner.PackFileService.AddFilesToPack(source, [new NewPackFileEntry("vfx", PackFile.CreateFromBytes("fx.xml", Fixtures.Utf16(Fixtures.Default)))]);
            var editor = Open(runner);
            editor.SelectedNode = editor.Tree[1].Children[0];

            editor.NewEmitterId = "e9";
            editor.RenameEmitterCommand.Execute(null);
            Assert.That(editor.SelectedNode?.Label, Is.EqualTo("e9 (continuous_mesh)"));
            Assert.That(editor.Status, Does.Contain("still names e1"));

            Assert.That(editor.EmitterTypes, Is.EqualTo(new[] { "continuous_mesh", "continuous_rigid" }));
            editor.EmitterType = "continuous_rigid";
            Assert.That((editor.SelectedNode?.Label, editor.EmitterType), Is.EqualTo(("e9 (continuous_rigid)", "continuous_rigid")));
        }

        // A continuous_rigid particle draws its model through the model's own materials, standing where the particle is.
        [Test]
        public void A_rigid_particle_draws_its_model()
        {
            var (runner, preview) = ShowRigidModel();
            Assert.That(DrawEffect(runner, DepthStencilState.Default).Count(x => x != Xna.Color.Transparent), Is.GreaterThan(100), string.Join("; ", preview.Lines));
        }

        // A rigid particle's model takes its chain's colour and alpha, as a quad does (vs_colour and vs_vertex_alpha are on 1,643
        // and 2,209 of vanilla's 2,924 rigid chains): Karl Franz in red at half alpha hides half of what is behind him, one layer
        // deep, and draws no green or blue.
        [Test]
        public void A_rigid_model_takes_its_particles_colour_and_alpha()
        {
            const string Key = "linear,linear,-0.05,0.0,0.05,0.0,0";
            var chain = "<behaviour_chain id=\"rigid\"><quality value=\"0\">"
                + $"<modifier fragment=\"vs_colour\"><property name=\"curve_colour\" type=\"curve\">one,0,0;0.0,1.0,{Key}|none,0,0;0.0,0.0,{Key}:r,0;0,0|g,0;1,1|b,0;1,1</property></modifier>"
                + $"<modifier fragment=\"vs_vertex_alpha\"><property name=\"curve_alpha\" type=\"curve\">half,0,0;0.0,0.5,{Key}:alpha,0;0,0</property></modifier>"
                + "</quality></behaviour_chain>";
            var (runner, preview) = ShowRigidModel(chain);
            var drawn = DrawEffect(runner, DepthStencilState.DepthRead).Where(x => x.A > 0).ToList();
            Assert.That(drawn, Has.Count.GreaterThan(100), string.Join("; ", preview.Lines));
            Assert.Multiple(() =>
            {
                Assert.That(drawn.Max(x => Math.Max(x.G, x.B)), Is.LessThan(8), "red");
                // Pieces of the model laid on one another at one depth (38 of 547 pixels here) both pass the depth test.
                Assert.That(drawn.Count(x => Math.Abs(x.A - 128) <= 2), Is.GreaterThan(drawn.Count * 9 / 10),
                    "half, one layer: " + string.Join(", ", drawn.GroupBy(x => x.A).Select(g => $"{g.Key} x{g.Count()}")));
            });
        }

        // A rigid particle turns with its emitter (ParticleRelativeSpawnOrientation is RELATIVE_TO_VFX_UP_AXIS or unset on 2,853
        // of vanilla's 2,924 rigid emitters): Karl Franz on an emitter rolled 90 degrees lies on his side. RELATIVE_TO_TERRAIN_NORMAL
        // stands him on the flat ground, upright.
        [Test]
        public void A_rigid_model_turns_with_its_emitter()
        {
            const string Key = "linear,linear,-0.05,0.0,0.05,0.0,0";
            var rolled = $"<property name=\"EmitterOrientation\" quality=\"0\">ninety,0,0;0.0,90.0,{Key}|none,0,0;0.0,0.0,{Key}:x,0;1,1|y,0;1,1|z,0;0,0</property>";
            const string OnTerrain = "<property name=\"ParticleRelativeSpawnOrientation\" quality=\"0\" value=\"RELATIVE_TO_TERRAIN_NORMAL\"/>";
            static (int Wide, int High) Extent(Xna.Color[] frame)
            {
                var drawn = Enumerable.Range(0, frame.Length).Where(i => frame[i].A > 0).ToList();
                return (drawn.Max(i => i % 64) - drawn.Min(i => i % 64), drawn.Max(i => i / 64) - drawn.Min(i => i / 64));
            }
            var upright = Extent(DrawEffect(ShowRigidModel().Item1, DepthStencilState.Default));
            var lying = Extent(DrawEffect(ShowRigidModel(properties: rolled).Item1, DepthStencilState.Default));
            var standing = Extent(DrawEffect(ShowRigidModel(properties: rolled + OnTerrain).Item1, DepthStencilState.Default));
            Assert.Multiple(() =>
            {
                Assert.That(upright.High, Is.GreaterThan(upright.Wide), "upright");
                Assert.That(lying.Wide, Is.GreaterThan(lying.High), "rolled");
                Assert.That(standing.High, Is.GreaterThan(standing.Wide), "on the ground");
            });
        }

        // The engine draws effects reading depth without writing it, so a model that does not write its own draws its far side
        // over its near one. It draws the same under the engine's state as under a plain depth test.
        [Test]
        public void A_rigid_model_writes_its_own_depth()
        {
            var (runner, _) = ShowRigidModel();
            var plain = DrawEffect(runner, DepthStencilState.Default);
            var engines = DrawEffect(runner, DepthStencilState.DepthRead);
            Assert.That(plain.Zip(engines).Count(x => x.First != x.Second), Is.Zero);
        }

        // The engine draws effects in its glow pass as well as its colour pass, so what lies over 1 blooms.
        [Test]
        public void The_engine_draws_effects_in_its_glow_pass()
        {
            var (runner, _, _) = OpenWithAGuideModelOffered();
            var engine = runner.GetRequiredServiceInCurrentEditorScope<RenderEngineComponent>();
            var device = runner.ServiceProvider.GetRequiredService<IWpfGame>().GraphicsDevice;
            var drawn = new List<RenderingTechnique>();
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Color, DepthFormat.Depth24);
            device.SetRenderTarget(target);
            engine.AddRenderItem(RenderBuckedId.Transparent, new Recorder(drawn));
            engine.Draw(new Xna.GameTime());
            device.SetRenderTarget(null);
            Assert.That(drawn, Is.EqualTo(new[] { RenderingTechnique.Normal, RenderingTechnique.Emissive }));
        }

        sealed class Recorder(List<RenderingTechnique> drawn) : IRenderItem
        {
            public void Draw(GraphicsDevice device, CommonShaderParameters parameters, RenderingTechnique technique) => drawn.Add(technique);
        }

        // The glow pass is light, added whatever its alpha: a premultiplied additive particle draws its glow with none, and a
        // bloom weighed by alpha dropped it; with alpha, a bloom hid the background behind it.
        [Test]
        public void The_glow_is_added_whatever_its_alpha()
        {
            var (runner, _, _) = OpenWithAGuideModelOffered();
            var engine = runner.GetRequiredServiceInCurrentEditorScope<RenderEngineComponent>();
            var device = runner.ServiceProvider.GetRequiredService<IWpfGame>().GraphicsDevice;
            double Frame(Xna.Color? glow)
            {
                using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Color, DepthFormat.Depth24);
                device.SetRenderTarget(target);
                if (glow is { } colour)
                    engine.AddRenderItem(RenderBuckedId.Transparent, new Glow(colour));
                engine.Draw(new Xna.GameTime());
                engine.Update(new Xna.GameTime());
                device.SetRenderTarget(null);
                var pixels = new Xna.Color[64 * 64];
                target.GetData(pixels);
                return pixels.Average(p => p.R + p.G + p.B);
            }
            var (plain, unweighted, weighted) = (Frame(null), Frame(new Xna.Color(230, 230, 230, 0)), Frame(new Xna.Color(230, 230, 230, 255)));
            Assert.Multiple(() =>
            {
                Assert.That(unweighted, Is.GreaterThan(plain + 30), "the glow brightens the picture");
                Assert.That(weighted, Is.EqualTo(unweighted).Within(1), "and its alpha changes nothing");
            });
        }

        // Fills the glow pass with one colour, as a particle's glow covering the frame would.
        sealed class Glow(Xna.Color colour) : IRenderItem
        {
            public void Draw(GraphicsDevice device, CommonShaderParameters parameters, RenderingTechnique technique)
            {
                if (technique == RenderingTechnique.Emissive)
                    device.Clear(colour);
            }
        }

        // Karl Franz's model on one continuous_rigid particle, half a second in.
        static (AssetEditorTestRunner, VfxPreviewComponent) ShowRigidModel(string chains = "", string properties = "")
        {
            var (runner, _, preview) = OpenWithAGuideModelOffered();
            const string Constant = "x_min,0,0;0.0,{0},linear,linear,-0.05,0.0,0.05,0.0,1|x_max,0,0;0.0,{0},linear,linear,-0.05,0.0,0.05,0.0,1:x,0;0,1";
            var emitter = "\r\n\t\t<emitter type=\"continuous_rigid\" id=\"rigid\">"
                + "<property name=\"InfiniteLife\" quality=\"0\" value=\"true\"/>"
                + "<property name=\"MaximumParticles\" quality=\"0\" value=\"1\"/>" + properties
                + "<property name=\"EmissionRateCurve\" quality=\"0\">" + string.Format(Constant, "60.0") + "</property>"
                + "<property name=\"ParticleLifeTime\" quality=\"0\">" + string.Format(Constant, "100.0") + "</property>"
                + "<property_rigid_data name=\"RigidData\" quality=\"0\"><rigid_entry><ModelFile value=\"" + KarlFranz + "\"/><PivotPoint x=\"0.50\" y=\"0.50\" z=\"0.50\"/><AnimFile value=\"\"/><AnimSpeed value=\"1.00\"/></rigid_entry></property_rigid_data>"
                + "\r\n\t\t</emitter>";
            preview.Show("rigid", Fixtures.Utf16(Fixtures.Effect(emitters: emitter, chains: chains, stacks: "", references: "")), 0);
            preview.SetTime(0.5f);
            return (runner, preview);
        }

        // The preview's effect drawn from 4 m into a 64x64 frame, both faces, under the depth state the caller leaves.
        static Xna.Color[] DrawEffect(AssetEditorTestRunner runner, DepthStencilState depth)
        {
            var item = runner.GetRequiredServiceInCurrentEditorScope<SceneManager>().RootNode.Children.OfType<VfxEffectNode>().Single().RenderItem;
            var device = runner.ServiceProvider.GetRequiredService<IWpfGame>().GraphicsDevice;
            var eye = new Vector3(0, 0, 4);
            var camera = new CommonShaderParameters(Xna.Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up),
                Xna.Matrix.CreatePerspectiveFieldOfView(Xna.MathHelper.PiOver4, 1, 0.1f, 100), eye, Vector3.Zero, 0, 0, 0, 1, [], Vector3.One);
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents);
            device.SetRenderTarget(target);
            device.Clear(Xna.Color.Transparent);
            device.DepthStencilState = depth;
            device.RasterizerState = RasterizerState.CullNone;
            item.Draw(device, camera, RenderingTechnique.Normal);
            device.SetRenderTarget(null);
            var pixels = new Xna.Color[64 * 64];
            target.GetData(pixels);
            return pixels;
        }

        // The Add list offers what the tier shown lacks. At Medium, C (written for Medium alone) is there already: offering it
        // let "This tier only" write a second C.
        [Test]
        public void The_add_list_leaves_out_what_the_shown_tier_has()
        {
            var runner = new AssetEditorTestRunner();
            var source = runner.CreateEmptyPackFile("source", false);
            var lib = Fixtures.Effect(emitters: "\r\n\t\t<emitter type=\"continuous_mesh\" id=\"x\">\r\n\t\t\t<property name=\"C\" quality=\"0\" value=\"1\"/>\r\n\t\t</emitter>");
            runner.PackFileService.AddFilesToPack(source, [
                new NewPackFileEntry("vfx", PackFile.CreateFromBytes("fx.xml", Fixtures.Utf16(Fixtures.Default))),
                new NewPackFileEntry("vfx", PackFile.CreateFromBytes("lib.xml", Fixtures.Utf16(lib)))]);
            source.IsCaPackFile = true;   // the vocabulary is read from CA's packs, where another file has C at Ultra
            runner.CreateOutputPack();
            var editor = Open(runner);
            editor.SelectedNode = editor.Tree[1].Children[0];
            Assert.That(editor.PropertyNameChoices, Does.Contain("C"));
            editor.Tier = 2;
            Assert.That(editor.PropertyNameChoices, Does.Not.Contain("C"));
        }

        const string KarlFranz = @"variantmeshes\wh_variantmodels\hu1\emp\emp_karl_franz\emp_karl_franz.rigid_model_v2";

        // vfx\fx.xml open in the editor, with Karl Franz's model as what the pack browser will hand back.
        static (AssetEditorTestRunner, VfxEditorViewModel, VfxPreviewComponent) OpenWithAGuideModelOffered()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), false);
            var source = runner.CreateEmptyPackFile("source", false);
            runner.PackFileService.AddFilesToPack(source, [new NewPackFileEntry("vfx", PackFile.CreateFromBytes("fx.xml", Fixtures.Utf16(Fixtures.Default)))]);
            runner.CreateOutputPack();
            var model = runner.PackFileService.FindFile(KarlFranz)!;
            runner.Dialogs.Setup(x => x.DisplayBrowseDialog(It.IsAny<List<string>>())).Returns(new BrowseDialogResultFile(true, model));
            var editor = Open(runner);
            return (runner, editor, runner.GetRequiredServiceInCurrentEditorScope<VfxPreviewComponent>());
        }

        // Opens the tab the way the app does and waits for the vocabulary, whose arrival rebuilds the rows and the status
        // line. The wait pumps the dispatcher: the arrival is posted back to this thread when it has a context.
        static VfxEditorViewModel Open(AssetEditorTestRunner runner, string? path = "vfx\\fx.xml")
        {
            var command = runner.CommandFactory.Create<OpenEditorCommand>();
            var editor = (VfxEditorViewModel)(path == null ? command.Execute(EditorEnums.Vfx_Editor) : command.Execute(path, EditorEnums.Vfx_Editor));
            var frame = new System.Windows.Threading.DispatcherFrame();
            editor.VocabularyLoaded.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            return editor;
        }

        static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                yield return child;
                foreach (var descendant in Descendants(child))
                    yield return descendant;
            }
        }
    }
}
