using CommunityToolkit.Mvvm.Input;
using Editors.KitbasherEditor.Core;
using Editors.KitbasherEditor.Events;
using Editors.KitbasherEditor.Services;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Shared.Ui.BaseDialogs.MathViews;
using Shared.Ui.Common;

namespace Editors.KitbasherEditor.ChildEditors.SnapTool
{
    // Moves the selected meshes, as one undo step, so the centre of what is drawn lands on a bone or on a target taken from
    // selected vertices. A mesh with a pivot is drawn at its vertices plus the pivot, so the pivot counts. Bones are read in the
    // bind pose, the space Kitbash stores and moves vertices in: a mesh moved to where an animation has carried a bone would sit
    // off that bone once it is skinned to it.
    public partial class SnapToolViewModel : BaseComponent, IDisposable
    {
        private readonly KitbasherRootScene _rootScene;
        private readonly SelectionManager _selectionManager;
        private readonly CommandManager _commandManager;
        private readonly IUiCommandFactory _commandFactory;
        private readonly RenderEngineComponent _renderEngine;
        private readonly GizmoComponent _gizmo;
        private readonly IEventHub _eventHub;
        private readonly IScopedLogger _scopedLogger;
        private readonly BonePicker _bonePicker;

        public FilterCollection<string> Bones { get; } = new(null);

        Vector3? _target;
        public Vector3? Target { get => _target; private set { SetAndNotify(ref _target, value); NotifyPropertyChanged(nameof(TargetText)); } }
        public string TargetText => _target is { } t ? $"{t.X:0.###}, {t.Y:0.###}, {t.Z:0.###}" : "-";

        string _status = "";
        public string Status { get => _status; private set => SetAndNotify(ref _status, value); }

        public bool RotationSnap { get => _gizmo.RotationSnap; set { _gizmo.RotationSnap = value; NotifyPropertyChanged(); } }
        public DoubleViewModel RotationSnapStep { get; }

        public SnapToolViewModel(KitbasherRootScene rootScene, SelectionManager selectionManager, CommandManager commandManager, IUiCommandFactory commandFactory,
            RenderEngineComponent renderEngine, GizmoComponent gizmo, IEventHub eventHub, IScopedLogger scopedLogger, BonePicker bonePicker)
        {
            _rootScene = rootScene;
            _selectionManager = selectionManager;
            _commandManager = commandManager;
            _commandFactory = commandFactory;
            _renderEngine = renderEngine;
            _gizmo = gizmo;
            _eventHub = eventHub;
            _scopedLogger = scopedLogger;
            _bonePicker = bonePicker;

            RotationSnapStep = new DoubleViewModel(_gizmo.RotationSnapDegrees, v => { if (v > 0) _gizmo.RotationSnapDegrees = (float)v; });
            RefreshBones();
            _eventHub.Register<KitbasherSkeletonChangedEvent>(this, _ => RefreshBones());
            _bonePicker.BonePicked += OnBonePicked;
        }

        void RefreshBones() => Bones.UpdatePossibleValues(_rootScene.Skeleton?.BoneNames);

        // A bone picked in the 3D view. The filter is cleared first, since a list filtered to other names would not show it.
        void OnBonePicked(int boneIndex)
        {
            Bones.Filter = "";
            Bones.SelectedItem = _rootScene.Skeleton?.BoneNames[boneIndex];
        }

        [RelayCommand]
        public void MoveToBone()
        {
            var skeleton = _rootScene.Skeleton;
            var boneIndex = skeleton?.GetBoneIndexByName(Bones.SelectedItem ?? "") ?? -1;
            if (boneIndex < 0)
            {
                Status = "Pick a bone first.";
                return;
            }

            MoveSelectionTo(skeleton!.GetWorldTransform(boneIndex).Translation, Bones.SelectedItem!);
        }

        [RelayCommand]
        public void UseSelectedVerticesAsTarget()
        {
            if (_selectionManager.GetState() is not VertexSelectionState { SelectedVertices.Count: > 0 } state || state.RenderObject is not Rmv2MeshNode mesh)
            {
                Status = "Select vertices in vertex mode first.";
                return;
            }

            var sum = Vector3.Zero;
            foreach (var vertex in state.SelectedVertices)
                sum += mesh.Geometry.GetVertexById(vertex);
            Target = sum / state.SelectedVertices.Count + mesh.PivotPoint;
            Status = $"Target set from {state.SelectedVertices.Count} vertices.";
        }

        [RelayCommand]
        public void MoveToTarget()
        {
            if (_target is { } target)
                MoveSelectionTo(target, "the target");
            else
                Status = "Set a target from selected vertices first.";
        }

        [RelayCommand]
        public void ClearTarget()
        {
            Target = null;
            Status = "";
        }

        void MoveSelectionTo(Vector3 point, string what)
        {
            var state = _selectionManager.GetState() as ObjectSelectionState;
            var meshes = state?.SelectedObjects<Rmv2MeshNode>() ?? [];
            if (meshes.Count == 0)
            {
                Status = "Select meshes in object mode first.";
                return;
            }

            var centre = Vector3.Zero;
            foreach (var mesh in meshes)
                centre += mesh.Geometry.MeshCenter + mesh.PivotPoint;
            var offset = point - centre / meshes.Count;

            // The gizmo's own move, so undo and the transform box treat it as a drag.
            var move = TransformGizmoWrapper.CreateFromSelectionState(state!, _commandFactory, _scopedLogger)!;
            move.Start(_commandManager);
            move.GizmoTranslateEvent(offset, PivotType.ObjectCenter);
            move.Stop(_commandManager);
            Status = $"Moved {meshes.Count} mesh(es) to {what}.";
        }

        public override void Draw(GameTime gameTime)
        {
            if (_target is { } target)
                _renderEngine.AddRenderLines(LineHelper.AddLocator(target, 0.1f, Color.Yellow));

            var skeleton = _rootScene.Skeleton;
            var boneIndex = skeleton?.GetBoneIndexByName(Bones.SelectedItem ?? "") ?? -1;
            if (boneIndex >= 0)
                _renderEngine.AddRenderLines(LineHelper.AddLocator(skeleton!.GetWorldTransform(boneIndex).Translation, 0.1f, Color.Cyan));
        }

        public void Dispose()
        {
            _eventHub.UnRegister(this);
            _bonePicker.BonePicked -= OnBonePicked;
        }
    }
}
