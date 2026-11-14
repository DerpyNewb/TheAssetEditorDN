using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.Shared.Core.Common;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.Ui.Common.Behaviors;

namespace Editors.AnimationMeta.SuperView
{
    /// <summary>One position field of the tag picked in the meta panel, as SuperView's handle moves it. The field is measured from
    /// a bone, or from the model's origin (-1), so the handle stands where the tag's marker is drawn, and a move in the scene is
    /// turned back into the bone's own space before it is written.</summary>
    public sealed class PositionHandle(ParsedMetadataAttribute tag, VectorAttributeViewModel box, int bone, float? at, Func<int, float?, Matrix> frameOf)
    {
        public string Property => box.Property.Name;

        // At is null for now; an untracked EFFECT is drawn where its bone was at its Start Time.
        Matrix Frame => frameOf(bone, at);
        public Vector3 World => Vector3.Transform((Vector3)box.Property.GetValue(tag)!, Frame);

        /// <summary>Moves the field by a step in the scene. It goes through the field's boxes, so they show it, the file is marked
        /// unsaved and the meta panel's undo records it.</summary>
        public void MoveBy(Vector3 worldDelta)
        {
            var frame = Frame;
            // A bone scaled to nothing, which is how CA hides a prop for part of a clip, has no inverse: the move would write NaN.
            if (Math.Abs(frame.Determinant()) < 1e-12f)
                return;
            box.Set(Vector3.Transform(World + worldDelta, Matrix.Invert(frame)));
        }

        /// <summary>A handle per position field SuperView draws a marker for; none for any other tag. BLOOD is drawn as an EFFECT
        /// but is left out: version 12 hides its fields behind `new` ones, so the field the box edits is not the one read here.</summary>
        public static List<PositionHandle> Of(MetaDataEntry? entry, Func<int, float?, Matrix> frameOf)
        {
            (string Property, int Bone, float? At)[] fields = entry?._input switch
            {
                FirePos_v10 or ImpactPosition_v10 or TargetPos_10 or CameraShakePos or CrewLocation_v10 or CrewLocation_v2 => [("Position", -1, null)],
                SplashAttack_v10 => [("StartPosition", -1, null), ("EndPosition", -1, null)],
                SoundTrigger_v10 x => [("Position", x.BoneIndex, null)],
                SoundTrigger_v4 x => [("Position", x.BoneIndex, null)],
                IEffectMeta x => [("Position", x.NodeIndex, x.Tracking ? null : x.EffectStartTime)],
                _ => [],
            };
            var boxes = entry?.Variables.OfType<VectorAttributeViewModel>().ToList() ?? [];
            return [.. fields
                .Select(f => (Field: f, Box: boxes.FirstOrDefault(x => x.Property.Name == f.Property)))
                .Where(x => x.Box != null)
                .Select(x => new PositionHandle(entry!._input, x.Box!, x.Field.Bone, x.Field.At, frameOf))];
        }
    }

    /// <summary>A translate handle on each position field of the tag picked in the meta panel. It drives the shared Gizmo widget
    /// at that field, as the BMD and CSC editors do, rather than at the scene selection.</summary>
    public class MetaTagGizmoComponent : BaseComponent, IDisposable
    {
        // SPLASH_ATTACK's two ends are the most positions one tag has; each gets a handle of its own.
        const int MostHandles = 2;

        readonly ArcBallCamera _camera;
        readonly IMouseComponent _mouse;
        readonly IKeyboardComponent _keyboard;
        readonly RenderEngineComponent _renderEngine;
        readonly IDeviceResolver _deviceResolver;
        readonly IGraphicsResourceCreator _graphicsResourceCreator;

        readonly List<Gizmo> _gizmos = [];
        Func<MetaDataEntry?> _picked = () => null;
        Func<int, float?, Matrix> _frameOf = (_, _) => Matrix.Identity;
        MetaDataEntry? _shown;
        int? _dragging;

        /// <summary>The handles shown, read again every frame, so a bone or Tracking typed into the panel moves them at once.</summary>
        public IReadOnlyList<PositionHandle> Handles { get; private set; } = [];

        public MetaTagGizmoComponent(ArcBallCamera camera, IMouseComponent mouse, IKeyboardComponent keyboard, RenderEngineComponent renderEngine,
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

        /// <summary>SuperView names the tag picked in its meta panel and where a bone is; every other editor leaves this unset, and
        /// the component does nothing there.</summary>
        public void Follow(Func<MetaDataEntry?> picked, Func<int, float?, Matrix> frameOf) => (_picked, _frameOf) = (picked, frameOf);

        /// <summary>Where a position measured from a bone stands in the scene at a time, or now when none is given: from the clip,
        /// as the markers are drawn, under the asset's own offset.</summary>
        public static Func<int, float?, Matrix> FrameOf(IMetaDataBuilder builder, SceneObject asset) =>
            (bone, at) => builder.BoneAt(bone, asset, asset.Player)(at ?? asset.Player.GetTimeUs() / 1_000_000f) * asset.MainNode.ModelMatrix;

        public override void Initialize()
        {
            for (var i = 0; i < MostHandles; i++)
            {
                var index = i;
                var gizmo = new Gizmo(_camera, _mouse, _deviceResolver.Device, _renderEngine, _graphicsResourceCreator) { ActivePivot = PivotType.ObjectCenter };
                gizmo.Selection.Add(new Target());
                gizmo.StartEvent += () => Started(index);
                gizmo.StopEvent += Stopped;
                gizmo.TranslateEvent += (_, e) => Dragged(index, (Vector3)e.Value);
                _gizmos.Add(gizmo);
            }
        }

        public override void Update(GameTime gameTime)
        {
            // Another tag, or the same row rebuilt by an undo: a drag does not carry over to it.
            var picked = _picked();
            if (picked != _shown)
                Stopped();
            _shown = picked;

            Handles = PositionHandle.Of(picked, _frameOf);
            var canMove = !_keyboard.IsKeyDown(Keys.LeftAlt);
            for (var i = 0; i < Handles.Count && i < _gizmos.Count; i++)
            {
                _gizmos[i].Selection[0].Position = Handles[i].World;
                _gizmos[i].Update(gameTime, canMove);
            }
        }

        public override void Draw(GameTime gameTime)
        {
            for (var i = 0; i < Handles.Count && i < _gizmos.Count; i++)
                _gizmos[i].Draw();
        }

        // Where two handles overlap, both widgets take the press and both report the drag; only the first one's counts. A press
        // while another part of the view holds the mouse, as the camera does through a middle-button drag, is left to it: the
        // mouse throws when one part takes it from another.
        void Started(int index)
        {
            if (_dragging != null || !_mouse.IsMouseOwner(this))
                return;
            _dragging = index;
            _mouse.MouseOwner = this;
            // The whole drag is one undo step, as a drag in a number box is (the meta panel's EditCoalescer).
            ScrubGesture.Start();
        }

        // Every widget reports a stop on any release of the button, dragged or not. With no drag under way this changes nothing,
        // as no number box can be dragged at the same time.
        void Stopped()
        {
            _dragging = null;
            ScrubGesture.Stop();
            if (_mouse.MouseOwner == this)
            {
                _mouse.MouseOwner = null;
                _mouse.ClearStates();
            }
        }

        void Dragged(int index, Vector3 delta)
        {
            if (_dragging == index)
                Handles[index].MoveBy(delta);
        }

        public void Dispose()
        {
            foreach (var gizmo in _gizmos)
                gizmo.Dispose();
        }

        class Target : ITransformable
        {
            public Vector3 Position { get; set; }
            public Vector3 Scale { get; set; } = Vector3.One;
            public Quaternion Orientation { get; set; } = Quaternion.Identity;
            public Vector3 GetObjectCentre() => Position;
        }
    }
}
