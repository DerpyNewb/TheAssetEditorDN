using GameWorld.Core.Commands;
using GameWorld.Core.SceneNodes;
using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.Ui.Editors.BoneMapping;

namespace Editors.KitbasherEditor.ChildEditors.ReRiggingTool
{
    public class ReRiggingViewModel : BoneMappingViewModel
    {
        private readonly IUiCommandFactory _commandFactory;
        private List<Rmv2MeshNode> _selectedMeshes;

        /// <summary>The bones the meshes' vertices use that have no target yet, as "name [index]". OK is refused until it is empty.</summary>
        public List<string> UnmappedUsedBones { get; } = [];
        public NotifyAttr<string> UnmappedUsedBonesText { get; } = new NotifyAttr<string>("");
        public NotifyAttr<bool> IsMappingComplete { get; } = new NotifyAttr<bool>(false);

        public ReRiggingViewModel(IUiCommandFactory commandFactory, LocalizationManager localizationManager) : base(localizationManager)
        {
            _commandFactory = commandFactory;
        }

        public void Initialize(List<Rmv2MeshNode> selectedMeshes, RemappedAnimatedBoneConfiguration config)
        {
            _selectedMeshes = selectedMeshes;
            Initialize(config);
            RefreshUnmappedUsedBones();
        }

        // An unmapped bone keeps its old index, which on the new skeleton is an unrelated bone or past its end.
        protected override bool CanApplyInvalidMapping => false;

        protected override void MappingUpdated() => RefreshUnmappedUsedBones();

        protected override void ApplyChanges()
        {
            var remapping = AnimatedBoneHelper.BuildRemappingList(_configuration.MeshBones.First());
            _commandFactory.CreateWithBuilder<RemapBoneIndexesCommand>().Configure(x => x.Configure(_selectedMeshes, remapping, _configuration.ParnetModelSkeletonName)).BuildAndExecute();
        }

        public override bool Validate(out string errorText)
        {
            RefreshUnmappedUsedBones();
            errorText = IsMappingComplete.Value ? "" : _localizationManager.Get("KitbasherTool.BoneMapping.ValidationIncompleteMapping") + "\n\n" + string.Join(", ", UnmappedUsedBones);
            return IsMappingComplete.Value;
        }

        void RefreshUnmappedUsedBones()
        {
            UnmappedUsedBones.Clear();
            foreach (var bone in Flatten(MeshBones.PossibleValues))
            {
                if (bone.IsUsedByCurrentModel.Value && bone.MappedBoneIndex.Value == -1)
                    UnmappedUsedBones.Add($"{bone.Name.Value} [{bone.BoneIndex.Value}]");
            }
            UnmappedUsedBonesText.Value = $"({UnmappedUsedBones.Count}) {string.Join(", ", UnmappedUsedBones)}";
            IsMappingComplete.Value = UnmappedUsedBones.Count == 0;
        }

        static IEnumerable<AnimatedBone> Flatten(IEnumerable<AnimatedBone> bones) => bones.SelectMany(x => Flatten(x.Children).Prepend(x));
    }
}
