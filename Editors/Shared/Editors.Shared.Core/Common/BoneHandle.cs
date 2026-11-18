using GameWorld.Core.Components;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Editors.Shared.Core.Common
{
    public enum BoneHandleMode { Off, Move, Rotate }

    /// <summary>
    /// A move or rotate handle on one bone. It drives the shared Gizmo widget at a point the editor gives, as the meta editor's tag
    /// handle does, rather than at the scene selection. A subclass says where it stands and what a drag does: Begin, then steps of
    /// MoveBy or TurnBy in the scene's axes, then End.
    /// </summary>
    public abstract class BoneHandle : BaseComponent, IDisposable
    {
        protected readonly ArcBallCamera _camera;
        protected readonly IDeviceResolver _deviceResolver;
        readonly IMouseComponent _mouse;
        readonly IKeyboardComponent _keyboard;
        readonly RenderEngineComponent _renderEngine;
        readonly IGraphicsResourceCreator _graphicsResourceCreator;
        Gizmo? _gizmo;

        protected BoneHandle(ArcBallCamera camera, IMouseComponent mouse, IKeyboardComponent keyboard, RenderEngineComponent renderEngine,
            IDeviceResolver deviceResolver, IGraphicsResourceCreator graphicsResourceCreator)
        {
            _camera = camera;
            _mouse = mouse;
            _keyboard = keyboard;
            _renderEngine = renderEngine;
            _deviceResolver = deviceResolver;
            _graphicsResourceCreator = graphicsResourceCreator;
            UpdateOrder = (int)ComponentUpdateOrderEnum.Gizmo;
            DrawOrder = (int)ComponentDrawOrderEnum.Gizmo;
        }

        BoneHandleMode _mode = BoneHandleMode.Move;
        public BoneHandleMode Mode
        {
            get => _mode;
            set
            {
                End();
                SetAndNotify(ref _mode, value);
                NotifyPropertyChanged(nameof(Moving));
                NotifyPropertyChanged(nameof(Rotating));
                if (_gizmo != null && value != BoneHandleMode.Off)
                    _gizmo.ActiveMode = value == BoneHandleMode.Rotate ? GizmoMode.Rotate : GizmoMode.Translate;
            }
        }

        // The panel's two toggle buttons; pressing the lit one again hides the handle.
        public bool Moving { get => Mode == BoneHandleMode.Move; set => Mode = value ? BoneHandleMode.Move : BoneHandleMode.Off; }
        public bool Rotating { get => Mode == BoneHandleMode.Rotate; set => Mode = value ? BoneHandleMode.Rotate : BoneHandleMode.Off; }

        /// <summary>Where the handle stands in the scene, or null when none is shown.</summary>
        public abstract Vector3? Where { get; }
        public abstract void Begin();
        /// <summary>A step of a move drag, in the scene's axes.</summary>
        public abstract void MoveBy(Vector3 step);
        /// <summary>A step of a turn drag, about the scene's axes.</summary>
        public abstract void TurnBy(Matrix turn);
        public abstract void End();

        /// <summary>A drag of the handle ended: one undo step.</summary>
        public event Action? DragEnded;

        public override void Initialize()
        {
            _gizmo = new Gizmo(_camera, _mouse, _deviceResolver.Device, _renderEngine, _graphicsResourceCreator) { ActivePivot = PivotType.ObjectCenter };
            _gizmo.ActiveMode = Mode == BoneHandleMode.Rotate ? GizmoMode.Rotate : GizmoMode.Translate;
            _gizmo.Selection.Add(new Target());
            _gizmo.StartEvent += Started;
            _gizmo.StopEvent += Stopped;
            _gizmo.TranslateEvent += (_, e) => MoveBy((Vector3)e.Value);
            _gizmo.RotateEvent += (_, e) => TurnBy((Matrix)e.Value);
        }

        public override void Update(GameTime gameTime)
        {
            var where = Where;
            if (_gizmo == null || where == null)
                return;
            _gizmo.Selection[0].Position = where.Value;
            _gizmo.Update(gameTime, !_keyboard.IsKeyDown(Keys.LeftAlt));
        }

        public override void Draw(GameTime gameTime)
        {
            if (Where != null)
                _gizmo?.Draw();
        }

        // A press while another part of the view holds the mouse, as the camera does through a middle-button drag, is left to it.
        void Started()
        {
            if (!_mouse.IsMouseOwner(this))
                return;
            _mouse.MouseOwner = this;
            Begin();
        }

        void Stopped()
        {
            End();
            DragEnded?.Invoke();
            if (_mouse.MouseOwner == this)
            {
                _mouse.MouseOwner = null;
                _mouse.ClearStates();
            }
        }

        public void Dispose() => _gizmo?.Dispose();

        class Target : ITransformable
        {
            public Vector3 Position { get; set; }
            public Vector3 Scale { get; set; } = Vector3.One;
            public Quaternion Orientation { get; set; } = Quaternion.Identity;
            public Vector3 GetObjectCentre() => Position;
        }
    }
}
