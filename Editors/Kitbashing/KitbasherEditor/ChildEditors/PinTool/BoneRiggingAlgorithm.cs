using CommunityToolkit.Mvvm.ComponentModel;
using Editors.KitbasherEditor.ChildEditors.PinTool.Commands;
using Editors.KitbasherEditor.Core;
using GameWorld.Core.SceneNodes;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.Ui.Common;

namespace Editors.KitbasherEditor.ChildEditors.PinTool
{
    public record BoneChoice(int Index, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>The Pin tool's Bone mode: every vertex of the target meshes goes 100% to one bone of the model's skeleton.</summary>
    public partial class BoneRiggingAlgorithm : ObservableObject
    {
        private readonly IUiCommandFactory _commandFactory;
        private readonly IStandardDialogs _standardDialogs;
        private readonly KitbasherRootScene _rootScene;

        public FilterCollection<BoneChoice> Bones { get; } = new(null);

        public BoneRiggingAlgorithm(IUiCommandFactory commandFactory, IStandardDialogs standardDialogs, KitbasherRootScene rootScene)
        {
            _commandFactory = commandFactory;
            _standardDialogs = standardDialogs;
            _rootScene = rootScene;

            var skeleton = _rootScene.Skeleton;
            if (skeleton != null)
                Bones.UpdatePossibleValues(skeleton.BoneNames.Select((name, index) => new BoneChoice(index, name)));
        }

        /// <summary>Selects a bone picked in the 3D view. The filter is cleared first, since a list filtered to other names would
        /// not show it.</summary>
        public void Pick(int boneIndex)
        {
            Bones.Filter = "";
            Bones.SelectedItem = Bones.PossibleValues.FirstOrDefault(x => x.Index == boneIndex);
        }

        /// <summary>The bind, not yet run, or null after telling the user what is missing.</summary>
        public IAeUndoCommandCommand? CreateCommand(List<Rmv2MeshNode> meshesToAffect)
        {
            var skeleton = _rootScene.Skeleton;
            if (skeleton == null)
            {
                _standardDialogs.ShowDialogBox("The model has no skeleton to bind to", "Error");
                return null;
            }

            if (Bones.SelectedItem == null)
            {
                _standardDialogs.ShowDialogBox("No bone selected", "Error");
                return null;
            }

            return _commandFactory.CreateWithBuilder<BindMeshToBoneCommand>()
                .Configure(x => x.Configure(meshesToAffect, Bones.SelectedItem.Index, skeleton.SkeletonName))
                .Build();
        }
    }
}
