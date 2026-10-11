using Editors.VfxEditor.ViewModels;
using Editors.VfxEditor.Views;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.DependencyInjection;
using Shared.Core.ToolCreation;

namespace Editors.VfxEditor
{
    public class DependencyInjectionContainer : DependencyContainer
    {
        public override void Register(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<VfxEditorView>();
            serviceCollection.AddScoped<VfxEditorViewModel>();
            serviceCollection.AddScoped<IEditorInterface, VfxEditorViewModel>();
        }

        public override void RegisterTools(IEditorDatabase editorDatabase)
        {
            // vfx\*.xml only; every other .xml keeps the text editor. Both match a vfx file, so the AssetEditor asks which to use.
            EditorInfoBuilder
                .Create<VfxEditorViewModel, VfxEditorView>(EditorEnums.Vfx_Editor)
                .AddExtention(".xml", EditorPriorites.High)
                .ValidForFoldersContaining("vfx\\")
                .Build(editorDatabase);
        }
    }
}
