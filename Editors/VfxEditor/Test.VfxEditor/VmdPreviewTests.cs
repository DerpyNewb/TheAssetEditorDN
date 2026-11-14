using Editors.Shared.Core.Common.BaseControl;
using Editors.VfxEditor.VariantMesh;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Commands;
using Shared.Core.ToolCreation;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.VfxEditor
{
    // The grid had no 3D view. Preview opens the file in SuperView: the model, its skeleton and its animation sets.
    class VmdPreviewTests
    {
        const string ThrotVmd = @"variantmeshes\variantmeshdefinitions\skv_throt.variantmeshdefinition";

        static (AssetEditorTestRunner Runner, VmdEditorViewModel Editor) Open()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            var editor = runner.CommandFactory.Create<OpenEditorCommand>().Execute<VmdEditorViewModel>(runner.PackFileService.FindFile(ThrotVmd)!, EditorEnums.VariantMesh_Editor);
            return (runner, editor);
        }

        [Test]
        public void Preview_OpensTheFileInSuperView_WithItsModelAndSkeleton()
        {
            var (runner, editor) = Open();

            editor.PreviewCommand.Execute(null);

            var opened = runner.ServiceProvider.GetRequiredService<IEditorManager>().GetAllEditors().Last();
            Assert.That(opened.GetType().Name, Is.EqualTo("SuperViewViewModel"));
            Assert.That(((IFileEditor)opened).CurrentFile, Is.SameAs(editor.CurrentFile));
            var scene = ((EditorHostBase)opened).SceneObjects[0].Data;
            Assert.That(scene.MeshName.Value, Is.EqualTo(editor.CurrentFile!.Name));
            Assert.That(scene.Skeleton, Is.Not.Null, "the model's skeleton did not load");
            Assert.That(editor.Status, Is.Empty);
        }

        [Test]
        public void Preview_WithUnsavedEdits_SaysSuperViewShowsTheSavedFile()
        {
            var (_, editor) = Open();
            editor.Rows[0].SlotName += "_renamed";
            Assert.That(editor.HasUnsavedChanges, Is.True);

            editor.PreviewCommand.Execute(null);

            Assert.That(editor.Status, Does.Contain("last saved"));
        }
    }
}
