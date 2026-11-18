using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;

namespace Editor.VisualSkeletonEditor.SkeletonEditor
{
    /// <summary>
    /// The skeleton editor's view: a click picks the bone drawn nearest it, and a move or rotate handle on the selected bone puts the
    /// bone where a drag takes it, in the skeleton itself. Its children go with it, as when its numbers are typed.
    /// </summary>
    public class SkeletonBoneHandle : BoneHandle
    {
        Func<SceneObject?> _scene = () => null;
        Func<int> _selected = () => -1;

        // A drag is measured from where it began.
        int _dragged = -1;
        bool _changed;
        Matrix _start, _turned;
        Vector3 _moved;

        public SkeletonBoneHandle(ArcBallCamera camera, IMouseComponent mouse, IKeyboardComponent keyboard, RenderEngineComponent renderEngine,
            IDeviceResolver deviceResolver, IGraphicsResourceCreator graphicsResourceCreator)
            : base(camera, mouse, keyboard, renderEngine, deviceResolver, graphicsResourceCreator)
        {
        }

        public void Follow(Func<SceneObject?> scene, Func<int> selectedBone) => (_scene, _selected) = (scene, selectedBone);

        /// <summary>Before the first change a drag makes, so the drag is one undo step and a click on the handle is none.</summary>
        public event Action? Changing;
        /// <summary>After each step of a drag, so the panel shows the bone's new numbers.</summary>
        public event Action? Changed;

        /// <summary>The skeleton, while it is drawn.</summary>
        GameSkeleton? Shown => _scene() is { Skeleton: { } skeleton, SkeletonSceneNode.IsVisible: true } ? skeleton : null;

        /// <summary>From the skeleton's space to the scene's, as SkeletonNode.Render draws it: the node's scale, then the scene object's
        /// offset, which flips Z for a tech skeleton.</summary>
        Matrix ToScene => Matrix.CreateScale(_scene()!.SkeletonSceneNode.ScaleMult) * _scene()!.Offset;

        Vector3 Drawn(GameSkeleton skeleton, int bone) => Vector3.Transform(skeleton.GetAnimatedWorldTranform(bone).Translation, ToScene);

        public override Vector3? Where
        {
            get
            {
                var bone = _selected();
                return Mode == BoneHandleMode.Off || Shown is not { } skeleton || bone < 0 || bone >= skeleton.BoneCount ? null : Drawn(skeleton, bone);
            }
        }

        /// <summary>The bone drawn nearest a click, or -1 when none is within the pixels a selection click allows.</summary>
        public int BoneAt(Vector2 click)
        {
            if (Shown is not { } skeleton)
                return -1;
            var joints = Enumerable.Range(0, skeleton.BoneCount).Select(i => Drawn(skeleton, i)).ToArray();
            var parents = Enumerable.Range(0, skeleton.BoneCount).Select(skeleton.GetParentBoneIndex).ToArray();
            var viewport = _deviceResolver.Device.Viewport;
            return IntersectionMath.NearestBone(click, _camera.ViewMatrix * _camera.ProjectionMatrix, viewport.Width, viewport.Height, joints, parents, SelectionComponent.BonePickPixels);
        }

        public override void Begin()
        {
            _dragged = Where == null ? -1 : _selected();
            if (_dragged < 0)
                return;
            _start = _scene()!.Skeleton.GetWorldTransform(_dragged);
            (_moved, _turned, _changed) = (Vector3.Zero, Matrix.Identity, false);
        }

        public override void MoveBy(Vector3 step)
        {
            if (_dragged < 0)
                return;
            _moved += step;
            var world = _start;
            world.Translation = Vector3.Transform(Vector3.Transform(_start.Translation, ToScene) + _moved, Matrix.Invert(ToScene));
            Set(world);
        }

        /// <summary>The bone turns about its own point, by the turn in the scene's axes, taken into the skeleton's through the scale and
        /// flip it is drawn with.</summary>
        public override void TurnBy(Matrix turn)
        {
            if (_dragged < 0)
                return;
            _turned *= turn;
            var toScene = ToScene;
            var at = Matrix.CreateTranslation(Vector3.Transform(_start.Translation, toScene));
            Set(_start * toScene * Matrix.Invert(at) * _turned * at * Matrix.Invert(toScene));
        }

        public override void End() => _dragged = -1;

        // The bone's own scale stays as it is: only its place and turn come from the world matrix.
        void Set(Matrix world)
        {
            if (!_changed)
            {
                _changed = true;
                Changing?.Invoke();
            }

            var skeleton = _scene()!.Skeleton;
            var parent = skeleton.GetParentBoneIndex(_dragged);
            var local = parent == -1 ? world : world * Matrix.Invert(skeleton.GetWorldTransform(parent));
            local.Decompose(out _, out var rotation, out var translation);
            skeleton.Translation[_dragged] = translation;
            skeleton.Rotation[_dragged] = Quaternion.Normalize(rotation);
            skeleton.RebuildSkeletonMatrix();
            Changed?.Invoke();
        }
    }
}
