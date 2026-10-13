using AnimationEditor.Common.BaseControl;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.DependencyInjection;
using Shared.Core.ToolCreation;

namespace Editors.UnitSimulator
{
    public class DependencyInjectionContainer : DependencyContainer
    {
        public override void Register(IServiceCollection serviceCollection)
        {
            serviceCollection.AddScoped<UnitSimulatorViewModel>();
        }

        public override void RegisterTools(IEditorDatabase editorDatabase)
        {
            // A tool, not a file editor: it opens from the Tools menu and the parts are picked in it.
            EditorInfoBuilder
                .Create<UnitSimulatorViewModel, EditorHostView>(EditorEnums.UnitSimulator_Editor)
                .AddToToolbar("Unit Simulator")
                .Build(editorDatabase);
        }
    }
}
