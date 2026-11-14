using CommunityToolkit.Mvvm.ComponentModel;
using GameWorld.Core.SceneNodes;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;

namespace Editors.KitbasherEditor.ChildEditors.ReplaceTexturePaths
{
    public partial class ReplaceTexturePathsViewModel : ObservableObject
    {
        readonly CommandManager _commandManager;
        readonly IPackFileService _packFileService;
        readonly IStandardDialogs _standardDialogs;
        List<Rmv2MeshNode> _allMeshes = [];
        List<Rmv2MeshNode> _selectedMeshes = [];

        [ObservableProperty] public partial string Find { get; set; } = "";
        [ObservableProperty] public partial string Replace { get; set; } = "";
        [ObservableProperty] public partial bool MatchCase { get; set; }
        [ObservableProperty] public partial bool OnlySelected { get; set; }
        [ObservableProperty] public partial bool HasSelection { get; set; }
        [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApply))] public partial int ChangeCount { get; set; }
        public bool CanApply => ChangeCount != 0;

        public ReplaceTexturePathsViewModel(CommandManager commandManager, IPackFileService packFileService, IStandardDialogs standardDialogs)
        {
            _commandManager = commandManager;
            _packFileService = packFileService;
            _standardDialogs = standardDialogs;
        }

        /// <summary>All: every mesh in every LOD of the editable model, groups included. The selected objects count only when they are among them.</summary>
        public void Initialize(Rmv2ModelNode editableModel, IEnumerable<ISceneNode> selectedObjects)
        {
            _allMeshes = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(editableModel);
            var selected = selectedObjects.ToHashSet();
            _selectedMeshes = _allMeshes.Where(selected.Contains).ToList();
            HasSelection = _selectedMeshes.Count != 0;
            Recount();
        }

        List<Rmv2MeshNode> MeshesInScope => OnlySelected ? _selectedMeshes : _allMeshes;

        partial void OnFindChanged(string value) => Recount();
        partial void OnReplaceChanged(string value) => Recount();
        partial void OnMatchCaseChanged(bool value) => Recount();
        partial void OnOnlySelectedChanged(bool value) => Recount();

        void Recount() => ChangeCount = ReplaceTexturePathsCommand.ShaderChanges(MeshesInScope, Find, Replace, MatchCase).Count;

        /// <summary>Replaces as one undo step and warns about new paths no loaded pack holds. False when nothing would change.</summary>
        public bool Apply()
        {
            if (ChangeCount == 0)
                return false;

            var command = new ReplaceTexturePathsCommand();
            command.Configure(MeshesInScope, Find, Replace, MatchCase);
            _commandManager.ExecuteCommand(command);

            var missing = command.NewPaths.Where(x => _packFileService.FindFile(x) == null).ToList();
            if (missing.Count != 0)
                _standardDialogs.ShowDialogBox($"The paths were replaced, but these textures are not in any loaded pack, so the 3D view cannot show them until you add them:\n\n{string.Join("\n", missing)}", "Texture not found");
            return true;
        }
    }
}
