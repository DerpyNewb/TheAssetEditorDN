using CommunityToolkit.Mvvm.ComponentModel;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;

namespace Editors.KitbasherEditor.ChildEditors.ReplaceTexturePaths
{
    public partial class ReplaceTexturePathsViewModel : ObservableObject, IDisposable
    {
        readonly CommandManager _commandManager;
        readonly IPackFileService _packFileService;
        readonly IStandardDialogs _standardDialogs;
        readonly IEventHub _eventHub;
        Rmv2ModelNode? _editableModel;
        Func<IEnumerable<ISceneNode>> _selection = () => [];
        List<Rmv2MeshNode> _allMeshes = [];
        List<Rmv2MeshNode> _selectedMeshes = [];

        [ObservableProperty] public partial string Find { get; set; } = "";
        [ObservableProperty] public partial string Replace { get; set; } = "";
        [ObservableProperty] public partial bool MatchCase { get; set; }
        [ObservableProperty] public partial bool OnlySelected { get; set; }
        [ObservableProperty] public partial bool HasSelection { get; set; }
        [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApply))] public partial int ChangeCount { get; set; }
        public bool CanApply => ChangeCount != 0;

        public ReplaceTexturePathsViewModel(CommandManager commandManager, IPackFileService packFileService, IStandardDialogs standardDialogs, IEventHub eventHub)
        {
            _commandManager = commandManager;
            _packFileService = packFileService;
            _standardDialogs = standardDialogs;
            _eventHub = eventHub;
        }

        /// <summary>All: every mesh in every LOD of the editable model, groups included. The selected objects count only when they are among them.
        /// The window stays open while the model is worked on, so both are read again whenever the selection or the undo stack changes.</summary>
        public void Initialize(Rmv2ModelNode editableModel, Func<IEnumerable<ISceneNode>> selection)
        {
            _editableModel = editableModel;
            _selection = selection;
            _eventHub.Register<SelectionChangedEvent>(this, _ => Refresh());
            _eventHub.Register<CommandStackChangedEvent>(this, _ => Refresh());
            _eventHub.Register<CommandStackUndoEvent>(this, _ => Refresh());
            Refresh();
        }

        void Refresh()
        {
            _allMeshes = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(_editableModel!);
            var selected = _selection().ToHashSet();
            _selectedMeshes = _allMeshes.Where(selected.Contains).ToList();
            HasSelection = _selectedMeshes.Count != 0;
            if (!HasSelection)
                OnlySelected = false;
            Recount();
        }

        public void Dispose() => _eventHub.UnRegister(this);

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
