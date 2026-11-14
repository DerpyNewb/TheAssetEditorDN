using Editors.KitbasherEditor.Core.MenuBarViews;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using Shared.Core.Events;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor.UiCommands
{
    // Rebuilds the selected meshes' normals from their faces, with edges sharper than MeshEdits.HardEdgeDegrees kept hard,
    // and their tangents and binormals from the uvs, as one undo step.
    public class RecalculateNormalsCommand : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Rebuild the selected meshes' normals from their faces, keeping sharp edges hard";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.AtleastOneObjectSelected;
        public Hotkey? HotKey { get; } = null;

        private readonly SelectionManager _selectionManager;
        private readonly IUiCommandFactory _commandFactory;

        public RecalculateNormalsCommand(SelectionManager selectionManager, IUiCommandFactory commandFactory)
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
                _commandFactory.CreateWithBuilder<EditMeshesCommand>().Configure(x => x.Configure(meshes, "Recalculate normals", mesh => MeshEdits.RecalculateNormals(mesh.Geometry))).BuildAndExecute();
        }
    }
}
