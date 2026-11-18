using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Editors.AnimatioReTarget.Editor.BoneHandling
{
    public enum OffsetHandleMode { Off, Move, Rotate }

    /// <summary>
    /// A move or rotate handle on the selected bone of the Generated model. A drag writes the bone's Translation and Rotation
    /// offsets, so the numbers in the panel show it and the clip regenerates as it goes. It drives the shared Gizmo widget at the
    /// bone, as the meta editor's tag handle does, rather than at the scene selection.
    /// </summary>
    public class BoneOffsetHandle : BaseComponent, IDisposable
    {
        readonly ArcBallCamera _camera;
        readonly IMouseComponent _mouse;
        readonly IKeyboardComponent _keyboard;
        readonly RenderEngineComponent _renderEngine;
        readonly IDeviceResolver _deviceResolver;
        readonly IGraphicsResourceCreator _graphicsResourceCreator;
        readonly BoneManager _boneManager;

        Gizmo? _gizmo;
        SceneObject? _generated;
        Func<float> _scale = () => 1;

        // A drag is measured from where it began, so a frame that has not regenerated yet cannot skew it.
        SkeletonBoneNode_new? _dragged;
        Vector3 _startMove, _startAngles, _moved;
        Matrix _startTurn, _turned;

        public BoneOffsetHandle(ArcBallCamera camera, IMouseComponent mouse, IKeyboardComponent keyboard, RenderEngineComponent renderEngine,
            IDeviceResolver deviceResolver, IGraphicsResourceCreator graphicsResourceCreator, BoneManager boneManager)
        {
            _camera = camera;
            _mouse = mouse;
            _keyboard = keyboard;
            _renderEngine = renderEngine;
            _deviceResolver = deviceResolver;
            _graphicsResourceCreator = graphicsResourceCreator;
            _boneManager = boneManager;
            UpdateOrder = (int)ComponentUpdateOrderEnum.Gizmo;
            DrawOrder = (int)ComponentDrawOrderEnum.Gizmo;
        }

        OffsetHandleMode _mode = OffsetHandleMode.Move;
        public OffsetHandleMode Mode
        {
            get => _mode;
            set
            {
                End();
                SetAndNotify(ref _mode, value);
                NotifyPropertyChanged(nameof(Moving));
                NotifyPropertyChanged(nameof(Rotating));
                if (_gizmo != null && value != OffsetHandleMode.Off)
                    _gizmo.ActiveMode = value == OffsetHandleMode.Rotate ? GizmoMode.Rotate : GizmoMode.Translate;
            }
        }

        // The panel's two toggle buttons; pressing the lit one again hides the handle.
        public bool Moving { get => Mode == OffsetHandleMode.Move; set => Mode = value ? OffsetHandleMode.Move : OffsetHandleMode.Off; }
        public bool Rotating { get => Mode == OffsetHandleMode.Rotate; set => Mode = value ? OffsetHandleMode.Rotate : OffsetHandleMode.Off; }

        public void Follow(SceneObject generated, Func<float> skeletonScale) => (_generated, _scale) = (generated, skeletonScale);

        SkeletonBoneNode_new? Bone
        {
            get
            {
                var bone = _boneManager.SelectedBone;
                var skeleton = _generated?.Skeleton;
                // The remapper leaves the root's offsets alone, so a handle on it would move nothing.
                if (Mode == OffsetHandleMode.Off || bone == null || skeleton == null || _generated!.AnimationClip == null
                    || bone.BoneIndex >= skeleton.BoneCount || skeleton.GetParentBoneIndex(bone.BoneIndex) == -1)
                    return null;
                return bone;
            }
        }

        /// <summary>The bone as drawn now, in the scene.</summary>
        Matrix? Shown(SkeletonBoneNode_new? bone)
        {
            if (bone == null)
                return null;
            var frame = AnimationSampler.Sample(_generated!.Player.CurrentFrame, 0, _generated.Skeleton, _generated.AnimationClip);
            return frame.GetSkeletonAnimatedWorld(_generated.Skeleton, bone.BoneIndex) * _generated.MainNode.ModelMatrix;
        }

        /// <summary>Where the handle stands, or null when none is shown.</summary>
        public Vector3? Where => Shown(Bone)?.Translation;

        public void Begin()
        {
            _dragged = Bone;
            if (_dragged == null)
                return;
            Shown(_dragged)!.Value.Decompose(out _, out var turn, out _);
            _startTurn = Matrix.CreateFromQuaternion(turn);
            _startMove = Read(_dragged.TranslationOffset);
            _startAngles = Read(_dragged.RotationOffset);
            _moved = Vector3.Zero;
            _turned = Matrix.Identity;
        }

        /// <summary>A step of the drag in the scene. The offset is added after the skeleton scale is applied to the root, so it is
        /// written smaller by that scale for the bone to move by the full step.</summary>
        public void MoveBy(Vector3 step)
        {
            var scale = _scale();
            if (_dragged == null || scale == 0)
                return;
            _moved += step;
            Write(_dragged.TranslationOffset, _startMove + _moved / scale);
        }

        /// <summary>
        /// A turn of the drag about the scene's axes. The remapper applies the offset R before the bone's own world turn W, so for
        /// the drawn bone to turn by D the new offset is W D W^-1 R, with W as drawn when the drag began.
        /// </summary>
        public void TurnBy(Matrix turn)
        {
            if (_dragged == null)
                return;
            _turned *= turn;
            var offset = _startTurn * _turned * Matrix.Invert(_startTurn) * global::Shared.Core.Misc.MathUtil.CreateRotation(_startAngles);
            Write(_dragged.RotationOffset, AnglesOf(offset));
        }

        public void End() => _dragged = null;

        /// <summary>Degrees about X, then Y, then Z: the inverse of MathUtil.CreateRotation, which the remapper turns them with.</summary>
        public static Vector3 AnglesOf(Matrix m)
        {
            var y = MathF.Asin(Math.Clamp(-m.M13, -1, 1));
            float x, z;
            if (MathF.Abs(m.M13) < 0.99999f)
            {
                x = MathF.Atan2(m.M23, m.M33);
                z = MathF.Atan2(m.M12, m.M11);
            }
            else
            {
                // Y at +-90 degrees: X and Z turn about the same axis, so all of it goes into X.
                x = MathF.Atan2(-m.M32, m.M22);
                z = 0;
            }
            return new Vector3(MathHelper.ToDegrees(x), MathHelper.ToDegrees(y), MathHelper.ToDegrees(z));
        }

        static Vector3 Read(global::Shared.Ui.BaseDialogs.MathViews.Vector3ViewModel v) => new((float)v.X.Value, (float)v.Y.Value, (float)v.Z.Value);

        static void Write(global::Shared.Ui.BaseDialogs.MathViews.Vector3ViewModel v, Vector3 value)
        {
            v.X.Value = value.X;
            v.Y.Value = value.Y;
            v.Z.Value = value.Z;
        }

        public override void Initialize()
        {
            _gizmo = new Gizmo(_camera, _mouse, _deviceResolver.Device, _renderEngine, _graphicsResourceCreator) { ActivePivot = PivotType.ObjectCenter };
            _gizmo.ActiveMode = Mode == OffsetHandleMode.Rotate ? GizmoMode.Rotate : GizmoMode.Translate;
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

        /// <summary>A drag of the handle ended: one undo step.</summary>
        public event Action? DragEnded;

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
