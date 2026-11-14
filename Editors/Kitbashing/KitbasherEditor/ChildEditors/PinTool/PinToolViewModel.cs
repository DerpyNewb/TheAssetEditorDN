using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.KitbasherEditor.Commands;
using Editors.KitbasherEditor.Core;
using Editors.KitbasherEditor.Services;
using Editors.KitbasherEditor.UiCommands;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Shared.Core.Services;

namespace Editors.KitbasherEditor.ChildEditors.PinTool
{
    public enum RiggingMode
    { 
        Pin,
        SkinWrap,
        Bone
    }

    public partial class PinToolViewModel : ObservableObject, IDisposable
    {
        private readonly ILogger _logger;
        private readonly SelectionManager _selectionManager;
        private readonly IUiCommandFactory _commandFactory;
        private readonly IStandardDialogs _standardDialogs;
        private readonly BonePicker _bonePicker;

        [ObservableProperty] PinRiggingAlgorithm _pinMode;
        [ObservableProperty] SkinWrapAlgorithm _skinWrapMode;
        [ObservableProperty] BoneRiggingAlgorithm _boneMode;
        [ObservableProperty] RiggingMode[] _possibleRiggingModes = Enum.GetValues<RiggingMode>();
        [ObservableProperty] RiggingMode _selectedRiggingMode = RiggingMode.Pin;
        [ObservableProperty] ObservableCollection<Rmv2MeshNode> _affectedMeshCollection = [];

        public PinToolViewModel(SelectionManager selectionManager, IUiCommandFactory commandFactory, IStandardDialogs standardDialogs, IScopedLogger scopedLogger, KitbasherRootScene rootScene, BonePicker bonePicker)
        {
            _logger = scopedLogger.ForContext<PinToolViewModel>();
            _selectionManager = selectionManager;
            _commandFactory = commandFactory;
            _standardDialogs = standardDialogs;

            _pinMode = new PinRiggingAlgorithm(_commandFactory, _standardDialogs, _selectionManager, scopedLogger);
            _skinWrapMode = new SkinWrapAlgorithm(_commandFactory, _standardDialogs, _selectionManager, scopedLogger);
            _boneMode = new BoneRiggingAlgorithm(_commandFactory, _standardDialogs, rootScene);
            _bonePicker = bonePicker;
            _bonePicker.BonePicked += _boneMode.Pick;
        }

        /// <summary>Bone mode with these meshes listed, as the save check's fix for vertices with no weights opens it.</summary>
        public void ShowBind(IEnumerable<Rmv2MeshNode> meshes)
        {
            SelectedRiggingMode = RiggingMode.Bone;
            AffectedMeshCollection.Clear();
            foreach (var mesh in meshes)
                AffectedMeshCollection.Add(mesh);
        }

        [RelayCommand] void ClearAffectedMeshCollection() => AffectedMeshCollection.Clear();
        [RelayCommand] void AddSelectionToAffectMeshCollection() => AddSelectionToList(AffectedMeshCollection);

        void AddSelectionToList(ObservableCollection<Rmv2MeshNode> itemList)
        {
            var selectionState = _selectionManager.GetState<ObjectSelectionState>();
            if (selectionState == null)
            {
                _standardDialogs.ShowDialogBox("Please select objects", "Error");
                return;
            }

            var selectedObjects = selectionState.SelectedObjects()
                .OfType<Rmv2MeshNode>()
                .ToList();

            foreach (var item in itemList)
                selectedObjects.Add(item);

            var sortedObjects = selectedObjects
                .Distinct()
                .OrderByDescending(x => x.Name);

            itemList.Clear();
            foreach (var item in sortedObjects)
                itemList.Add(item);
        }

        public bool Apply()
        {
            if (AffectedMeshCollection.Count == 0)
            {
                _standardDialogs.ShowDialogBox("No meshes to be affected seleceted", "Error");
                return false;
            }

            _logger.Here().Information("Applying {Mode} to {Count} meshes", SelectedRiggingMode, AffectedMeshCollection.Count);

            var targets = AffectedMeshCollection.ToList();
            var step = SelectedRiggingMode switch
            {
                RiggingMode.Pin => PinMode.CreateCommand(targets),
                RiggingMode.SkinWrap => SkinWrapMode.CreateCommand(targets),
                RiggingMode.Bone => BoneMode.CreateCommand(targets),
                _ => throw new NotImplementedException($"Unable to find an algorithm for selected mode '{SelectedRiggingMode}'")
            };
            if (step == null)
                return false;

            // Pin and Skin Wrap ignore a mesh's pivot, so the targets' pivots (and Skin Wrap's sources') go into their vertices
            // first, in the same undo step. Bone mode bakes too, because the save check warns about a pivot on a rigged model.
            var baked = targets
                .Concat(SelectedRiggingMode == RiggingMode.SkinWrap ? SkinWrapMode.SourceMeshes : [])
                .Where(x => x.PivotPoint != Vector3.Zero)
                .Distinct()
                .ToList();
            var bake = BakePivotCommand.Create(_commandFactory, baked)?.Build();
            _commandFactory.CreateWithBuilder<CompoundCommand>()
                .Configure(x => x.Configure(bake == null ? [step] : [bake, step]))
                .BuildAndExecute();

            if (baked.Count != 0)
                _standardDialogs.ShowDialogBox($"The pivot points of these meshes were baked into their vertices first, so they still draw where they did: {string.Join(", ", baked.Select(x => x.Name))}. One Undo takes back both the bake and the {StepName(SelectedRiggingMode)}.", PivotsBakedTitle);
            return true;
        }

        public const string PivotsBakedTitle = "Pivots baked";

        static string StepName(RiggingMode mode) => mode switch { RiggingMode.Pin => "pin", RiggingMode.SkinWrap => "skin wrap", _ => "bind" };

        public void Dispose() => _bonePicker.BonePicked -= BoneMode.Pick;
    }
}
