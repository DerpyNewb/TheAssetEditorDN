using Editors.KitbasherEditor.Core.MenuBarViews;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using Shared.Core.Events;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor.UiCommands
{
    // Turns the selected meshes' faces round, for a mesh that shows its inside, as one undo step.
    public class FlipFacesCommand : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Turn the selected meshes' faces round, for a mesh that shows its inside";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.AtleastOneObjectSelected;
        public Hotkey? HotKey { get; } = null;

        private readonly SelectionManager _selectionManager;
        private readonly IUiCommandFactory _commandFactory;

        public FlipFacesCommand(SelectionManager selectionManager, IUiCommandFactory commandFactory)
        {
            _selectionManager = selectionManager;
            _commandFactory = commandFactory;
        }

        public void Execute()
        {
            if (_selectionManager.GetState() is not ObjectSelectionState state)
                return;
            var meshes = state.SelectedObjects().OfType<Rmv2MeshNode>().ToList();
            if (meshes.Count != 0)
                _commandFactory.CreateWithBuilder<EditMeshesCommand>().Configure(x => x.Configure(meshes, "Flip faces", mesh => MeshEdits.FlipFaces(mesh.Geometry))).BuildAndExecute();
        }
    }
}
