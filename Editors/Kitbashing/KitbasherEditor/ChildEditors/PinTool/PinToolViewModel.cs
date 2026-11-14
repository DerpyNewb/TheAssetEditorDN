using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.KitbasherEditor.Core;
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

    public partial class PinToolViewModel : ObservableObject
    {
        private readonly ILogger _logger;
        private readonly SelectionManager _selectionManager;
        private readonly IUiCommandFactory _commandFactory;
        private readonly IStandardDialogs _standardDialogs;

        [ObservableProperty] PinRiggingAlgorithm _pinMode;
        [ObservableProperty] SkinWrapAlgorithm _skinWrapMode;
        [ObservableProperty] BoneRiggingAlgorithm _boneMode;
        [ObservableProperty] RiggingMode[] _possibleRiggingModes = Enum.GetValues<RiggingMode>();
        [ObservableProperty] RiggingMode _selectedRiggingMode = RiggingMode.Pin;
        [ObservableProperty] ObservableCollection<Rmv2MeshNode> _affectedMeshCollection = [];

        public PinToolViewModel(SelectionManager selectionManager, IUiCommandFactory commandFactory, IStandardDialogs standardDialogs, IScopedLogger scopedLogger, KitbasherRootScene rootScene)
        {
            _logger = scopedLogger.ForContext<PinToolViewModel>();
            _selectionManager = selectionManager;
            _commandFactory = commandFactory;
            _standardDialogs = standardDialogs;

            _pinMode = new PinRiggingAlgorithm(_commandFactory, _standardDialogs, _selectionManager, scopedLogger);
            _skinWrapMode = new SkinWrapAlgorithm(_commandFactory, _standardDialogs, _selectionManager, scopedLogger);
            _boneMode = new BoneRiggingAlgorithm(_commandFactory, _standardDialogs, rootScene);
        }

        [RelayCommand] void ClearAffectedMeshCollection() => AffectedMeshCollection.Clear();
        [RelayCommand] void AddSelectionToAffectMeshCollection() => AddSelectionToList(AffectedMeshCollection);

        public const string PivotRefusal = "A selected mesh has a pivot point, which these tools cannot follow. Bake it into the mesh first, with Bake into mesh beside the pivot in the mesh's panel.";

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

            if (selectedObjects.Any(x => x.PivotPoint != Vector3.Zero))
            {
                _standardDialogs.ShowDialogBox(PivotRefusal, "error");
                return;
            }

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

            switch (SelectedRiggingMode)
            {
                case RiggingMode.Pin:
                    return PinMode.Execute(AffectedMeshCollection.ToList());
              
                case RiggingMode.SkinWrap:
                    return SkinWrapMode.Execute(AffectedMeshCollection.ToList());

                case RiggingMode.Bone:
                    return BoneMode.Execute(AffectedMeshCollection.ToList());
                default:
                    throw new NotImplementedException($"Unable to find an algorithm for selected mode '{SelectedRiggingMode}'");
            }
        }
    }
}
