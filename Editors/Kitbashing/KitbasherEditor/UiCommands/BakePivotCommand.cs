using Editors.KitbasherEditor.Core.MenuBarViews;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.Events;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor.UiCommands
{
    // Moves each selected mesh's pivot into its vertices as one undo step, so the mesh draws where it did with a pivot of
    // 0, 0, 0. The Pin tool's Apply runs the same bake inside its own undo step.
    public class BakePivotCommand : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Bake the selected meshes' pivots into their vertices";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.AtleastOneObjectSelected;
        public Hotkey? HotKey { get; } = null;

        private readonly SelectionManager _selectionManager;
        private readonly IUiCommandFactory _commandFactory;

        public BakePivotCommand(SelectionManager selectionManager, IUiCommandFactory commandFactory)
        {
            _selectionManager = selectionManager;
            _commandFactory = commandFactory;
        }

        public void Execute()
        {
            if (_selectionManager.GetState() is ObjectSelectionState state)
                Bake(_commandFactory, state.SelectedObjects().OfType<Rmv2MeshNode>());
        }

        // Shared with the Bake into mesh button in a mesh's panel. Meshes with no pivot are left out of the undo step.
        public static void Bake(IUiCommandFactory commandFactory, IEnumerable<Rmv2MeshNode> meshes) => Create(commandFactory, meshes)?.BuildAndExecute();

        /// <summary>The bake, not yet run, so a tool can make it part of its own undo step. Null when no mesh has a pivot.</summary>
        public static CommandBuilder<EditMeshesCommand>? Create(IUiCommandFactory commandFactory, IEnumerable<Rmv2MeshNode> meshes)
        {
            var withPivot = meshes.Where(x => x.PivotPoint != Vector3.Zero).Distinct().ToList();
            if (withPivot.Count == 0)
                return null;

            return commandFactory.CreateWithBuilder<EditMeshesCommand>()
                .Configure(x => x.Configure(withPivot, "Bake pivot", mesh =>
                {
                    MeshEdits.BakePivot(mesh.Geometry, mesh.PivotPoint);
                    mesh.PivotPoint = Vector3.Zero;
                }));
        }
    }
}
