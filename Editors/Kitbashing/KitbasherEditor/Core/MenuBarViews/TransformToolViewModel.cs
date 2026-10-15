using System.ComponentModel;
using GameWorld.Core.Commands;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Ui.BaseDialogs.MathViews;

namespace KitbasherEditor.ViewModels.MenuBarViews
{
    public class TransformToolViewModel : NotifyPropertyChangedImpl
    {
        public enum TransformMode
        {
            None,
            Rotate,
            Scale,
            Translate
        }

        TransformMode _activeMode = TransformMode.None;

        private readonly SelectionManager _selectionManager;
        private readonly CommandManager _commandExecutor;
        private readonly IUiCommandFactory _commandFactory;
        private readonly IScopedLogger _scopedLogger;

        // The box holds the edit's total and moves the model as it is typed. Each change undoes the last preview and applies the
        // total again, so an edit is one undo step; anything else that changes the model or the selection ends the edit.
        bool _previewOnTop;
        bool _previewing;

        bool _canTransform = false;
        public bool CanTransform { get { return _canTransform; } set { SetAndNotify(ref _canTransform, value); } }


        bool _isVisible = false;
        public bool IsVisible { get { return _isVisible; } set { SetAndNotify(ref _isVisible, value); } }

        string _text;
        public string Text { get { return _text; } set { SetAndNotify(ref _text, value); } }

        Vector3ViewModel _vector3 = new Vector3ViewModel(0, 0, 0);
        public Vector3ViewModel Vector3 { get { return _vector3; } set { SetAndNotify(ref _vector3, value); } }

        public NotifyAttr<DoubleViewModel> VertexMovementFalloff { get; set; }

        public NotifyAttr<bool> ShowVertexFalloff { get; set; } = new NotifyAttr<bool>(false);

        // Rotate and scale about the world origin rather than the selection's centre: a weapon hangs from its bone there, so
        // its grip stays in the hand. Changing it reshapes the edit already typed.
        bool _pivotAtOrigin;
        public bool PivotAtOrigin { get { return _pivotAtOrigin; } set { SetAndNotifyWhenChanged(ref _pivotAtOrigin, value, _ => Preview()); } }

        bool _showPivot;
        public bool ShowPivot { get { return _showPivot; } set { SetAndNotify(ref _showPivot, value); } }

        public TransformToolViewModel(SelectionManager selectionManager, CommandManager commandExecutor, IUiCommandFactory commandFactory, IEventHub eventHub, IScopedLogger scopedLogger)
        {
            VertexMovementFalloff = new NotifyAttr<DoubleViewModel>(new DoubleViewModel());
            VertexMovementFalloff.Value.PropertyChanged += VertexMovementFalloffChanged;
            _selectionManager = selectionManager;
            _commandExecutor = commandExecutor;
            _commandFactory = commandFactory;
            _scopedLogger = scopedLogger;
            _vector3.OnValueChanged += _ => Preview();
            eventHub.Register<SelectionChangedEvent>(this, Handle);
            // Another command ends the edit. An undo of the preview ends it through the selection, which that undo restores.
            eventHub.Register<CommandStackChangedEvent>(this, _ => EndEditUnlessPreviewing());
        }

        // The preview was weighted by the old falloff, so it comes off before the weights change and goes back on with the new ones.
        public void VertexMovementFalloffChanged(object sender, PropertyChangedEventArgs e)
        {
            Preview(() => _selectionManager.UpdateVertexSelectionFallof((float)VertexMovementFalloff.Value.Value));
        }

        private void SelectionChanged(ISelectionState state)
        {
            ShowVertexFalloff.Value = false;
            if (state.Mode == GeometrySelectionMode.Face)
            {
                CanTransform = false;
            }
            else
            {
                if (state is ObjectSelectionState objectSelectionState)
                    CanTransform = objectSelectionState.SelectionCount() != 0;
                else if (state is VertexSelectionState vertexSelectionState)
                {
                    CanTransform = vertexSelectionState.SelectionCount() != 0;
                    ShowVertexFalloff.Value = true;
                }
            }
        }

        public void SetMode(TransformMode mode)
        {
            _activeMode = mode;
            IsVisible = _activeMode != TransformMode.None;
            ShowPivot = _activeMode is TransformMode.Rotate or TransformMode.Scale;

            if (_activeMode == TransformMode.Rotate)
                Text = "Rotate:";
            else if (_activeMode == TransformMode.Scale)
                Text = "Scale:";
            else if (_activeMode == TransformMode.Translate)
                Text = "Translate:";

            EndEdit();
        }

        void Preview(Action? changeBeforeApply = null)
        {
            _previewing = true;
            try
            {
                if (_previewOnTop)
                    _commandExecutor.Undo(keepForRedo: false);
                changeBeforeApply?.Invoke();
                _previewOnTop = ApplyTransform();
            }
            finally
            {
                _previewing = false;
            }
        }

        void EndEditUnlessPreviewing()
        {
            if (!_previewing)
                EndEdit();
        }

        void EndEdit()
        {
            _previewOnTop = false;
            _vector3.DisableCallbacks = true;
            SetDefaultValue();
            _vector3.DisableCallbacks = false;
        }

        bool ApplyTransform()
        {
            var transform = TransformGizmoWrapper.CreateFromSelectionState(_selectionManager.GetState(), _commandFactory, _scopedLogger);
            if (transform == null || _activeMode == TransformMode.None)
                return false;

            // Nothing to undo for a box at rest, and a scale with a 0 in it flattens the model past what its undo can invert:
            // typing 0.5 passes through 0, so that step is skipped and the next one applies.
            var value = _vector3.GetAsVector3();
            if (_activeMode == TransformMode.Scale ? value == new Vector3(1) || value.X * value.Y * value.Z == 0 : value == new Vector3(0))
                return false;

            var pivot = _pivotAtOrigin ? PivotType.WorldOrigin : PivotType.ObjectCenter;

            if (_activeMode == TransformMode.Rotate)
            {
                transform.Start(_commandExecutor);
                transform.GizmoRotateEvent(
                    Matrix.CreateRotationX(MathHelper.ToRadians((float)_vector3.X.Value)) *
                    Matrix.CreateRotationY(MathHelper.ToRadians((float)_vector3.Y.Value)) *
                    Matrix.CreateRotationZ(MathHelper.ToRadians((float)_vector3.Z.Value)), pivot);
            }
            else if (_activeMode == TransformMode.Translate)
            {
                transform.Start(_commandExecutor);
                transform.GizmoTranslateEvent(new Vector3((float)_vector3.X.Value, (float)_vector3.Y.Value, (float)_vector3.Z.Value), pivot);
            }
            else if (_activeMode == TransformMode.Scale)
            {
                transform.Start(_commandExecutor);
                transform.GizmoScaleEvent(new Vector3((float)_vector3.X.Value - 1, (float)_vector3.Y.Value - 1, (float)_vector3.Z.Value - 1), pivot);    // -1 due to weirdness inside the function
            }

            transform.Stop(_commandExecutor);
            return true;
        }

        void SetDefaultValue()
        {
            if (_activeMode == TransformMode.Scale)
                _vector3.Set(1);
            else
                _vector3.Set(0);
        }

        void Handle(SelectionChangedEvent notification)
        {
            EndEditUnlessPreviewing();
            SelectionChanged(notification.NewState);
        }
    }
}
