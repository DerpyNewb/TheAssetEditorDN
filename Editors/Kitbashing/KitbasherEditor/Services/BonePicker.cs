using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;

namespace Editors.KitbasherEditor.Services
{
    /// <summary>While pick mode is on, a click picks the nearest bone of the shown skeleton instead of selecting, so the mesh,
    /// face or vertex selection is kept.</summary>
    public class BonePicker
    {
        /// <summary>How far from a drawn bone, in pixels, a click still picks it.</summary>
        public const float MaxPixels = 15;

        private readonly SceneManager _sceneManager;
        private readonly ArcBallCamera _camera;
        private readonly IDeviceResolver _deviceResolver;
        private bool _isActive;

        public BonePicker(SceneManager sceneManager, ArcBallCamera camera, IDeviceResolver deviceResolver)
        {
            _sceneManager = sceneManager;
            _camera = camera;
            _deviceResolver = deviceResolver;
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value)
                    return;
                _isActive = value;
                ActiveChanged?.Invoke(value);
            }
        }

        public event Action<bool>? ActiveChanged;

        // A plain event rather than one on the event hub: the Skeleton panel subscribes while the hub is calling the scene
        // explorer's selection handler, and the hub's first subscriber to an event type adds to the dictionary it is walking.
        /// <summary>The index, in the model's skeleton, of a bone just picked.</summary>
        public event Action<int>? BonePicked;

        /// <summary>The selection component's click handler. True while pick mode is on, so the click selects nothing, even when it
        /// misses every bone: a near miss in vertex mode would otherwise drop the vertex selection.</summary>
        public bool OnClick(Vector2 mousePosition)
        {
            if (IsActive == false)
                return false;

            var skeletonNode = _sceneManager.GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel)?.SkeletonNode;
            if (skeletonNode?.Skeleton is not { } skeleton || skeletonNode.IsVisible == false)
                return true;

            var joints = new Vector3[skeleton.BoneCount];
            var parents = new int[skeleton.BoneCount];
            for (var i = 0; i < joints.Length; i++)
            {
                // Where SkeletonNode.Render draws the bone: posed by the animation shown, with no parent transform, since the
                // skeleton node is a child of the scene's root.
                joints[i] = (skeleton.GetAnimatedWorldTranform(i) * Matrix.CreateScale(skeletonNode.ScaleMult)).Translation;
                parents[i] = skeleton.GetParentBoneIndex(i);
            }

            var viewport = _deviceResolver.Device.Viewport;
            var bone = Nearest(mousePosition, _camera.ViewMatrix * _camera.ProjectionMatrix, viewport.Width, viewport.Height, joints, parents, MaxPixels);
            if (bone >= 0)
            {
                skeletonNode.SelectedBoneIndex = bone;
                BonePicked?.Invoke(bone);
            }
            return true;
        }

        /// <summary>
        /// The bone drawn nearest a click, or -1 when none is within <paramref name="maxPixels"/>. A bone is drawn as a point with a
        /// line to its parent, and the half of that line nearer a bone counts as that bone. Distances are measured on screen, so a
        /// bone is as easy to hit far from the camera as near it.
        /// </summary>
        public static int Nearest(Vector2 click, Matrix viewProjection, float width, float height, IReadOnlyList<Vector3> joints, IReadOnlyList<int> parents, float maxPixels)
        {
            var screen = new Vector2?[joints.Count];
            var depth = new float[joints.Count];
            for (var i = 0; i < joints.Count; i++)
            {
                var clip = Vector4.Transform(new Vector4(joints[i], 1), viewProjection);
                // Behind the camera the projection turns over, which would put such a bone somewhere on the screen.
                if (clip.W <= 0)
                    continue;
                screen[i] = new Vector2((clip.X / clip.W + 1) * 0.5f * width, (1 - clip.Y / clip.W) * 0.5f * height);
                depth[i] = clip.W;
            }

            var hits = new List<(int Bone, float Distance)>();
            for (var i = 0; i < joints.Count; i++)
            {
                if (screen[i] is not { } at)
                    continue;
                hits.Add((i, Vector2.Distance(click, at)));

                var parent = parents[i];
                if (parent < 0 || screen[parent] is not { } parentAt)
                    continue;
                var line = parentAt - at;
                var t = line.LengthSquared() == 0 ? 0 : Math.Clamp(Vector2.Dot(click - at, line) / line.LengthSquared(), 0, 1);
                hits.Add((t <= 0.5f ? i : parent, Vector2.Distance(click, at + t * line)));
            }

            hits.RemoveAll(x => x.Distance > maxPixels);
            if (hits.Count == 0)
                return -1;

            // Bones that draw over each other, as the left and right legs do seen from the side: the one nearer the camera wins.
            var nearest = hits.Min(x => x.Distance);
            return hits.Where(x => x.Distance <= nearest + 1).MinBy(x => depth[x.Bone]).Bone;
        }
    }
}
