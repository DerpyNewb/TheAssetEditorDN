using Editors.KitbasherEditor.Core.MenuBarViews;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.GameFormats.RigidModel;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor.UiCommands
{
    // Mirrors the selected meshes left to right through the model's centre, as one undo step, and moves each weight onto
    // the opposite bone, so a weapon held in the right hand becomes one held in the left.
    public class MirrorMeshCommand : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Mirror the selected meshes left to right and move their weights onto the opposite bones";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.AtleastOneObjectSelected;
        public Hotkey? HotKey { get; } = null;
        protected virtual bool SwapBones => true;

        private readonly SelectionManager _selectionManager;
        private readonly IUiCommandFactory _commandFactory;
        private readonly ISkeletonAnimationLookUpHelper _skeletons;
        private readonly IStandardDialogs _dialogs;

        public MirrorMeshCommand(SelectionManager selectionManager, IUiCommandFactory commandFactory, ISkeletonAnimationLookUpHelper skeletons, IStandardDialogs dialogs)
        {
            _selectionManager = selectionManager;
            _commandFactory = commandFactory;
            _skeletons = skeletons;
            _dialogs = dialogs;
        }

        public void Execute()
        {
            if (_selectionManager.GetState() is not ObjectSelectionState state)
                return;
            var meshes = state.SelectedObjects().OfType<Rmv2MeshNode>().ToList();
            if (meshes.Count == 0)
                return;

            var missingSkeletons = new HashSet<string>();
            _commandFactory.CreateWithBuilder<EditMeshesCommand>()
                .Configure(x => x.Configure(meshes, "Mirror", mesh =>
                {
                    Dictionary<int, int>? boneSwap = null;
                    if (SwapBones && mesh.Geometry.VertexFormat != UiVertexFormat.Static)
                    {
                        var skeleton = _skeletons.GetSkeletonFileFromName(mesh.Geometry.SkeletonName);
                        if (skeleton == null)
                            missingSkeletons.Add(mesh.Geometry.SkeletonName);
                        else
                            boneSwap = MeshEdits.LeftRightBoneSwap(skeleton.Bones.ToDictionary(b => b.Id, b => b.Name));
                    }

                    MeshEdits.Mirror(mesh.Geometry, boneSwap);
                    mesh.PivotPoint = new Vector3(-mesh.PivotPoint.X, mesh.PivotPoint.Y, mesh.PivotPoint.Z);
                }))
                .BuildAndExecute();

            if (missingSkeletons.Count != 0)
                _dialogs.ShowDialogBox($"Mirrored, but the bones were not swapped: no loaded pack holds the skeleton {string.Join(", ", missingSkeletons)}.", "Mirror");
        }
    }

    // The same mirror with every weight left on its own bone, for a part that should stay on the side it is rigged to.
    public class MirrorMeshKeepBonesCommand : MirrorMeshCommand
    {
        protected override bool SwapBones => false;

        public MirrorMeshKeepBonesCommand(SelectionManager selectionManager, IUiCommandFactory commandFactory, ISkeletonAnimationLookUpHelper skeletons, IStandardDialogs dialogs)
            : base(selectionManager, commandFactory, skeletons, dialogs)
        {
            ToolTip = "Mirror the selected meshes left to right and leave their weights on the bones they are on";
        }
    }
}
