using System;
using System.Collections.Generic;
using GameWorld.Core.Animation;
using GameWorld.Core.Animation.Posing;
using GameWorld.Core.Commands;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.Events;
using static GameWorld.Core.Animation.AnimationClip;

namespace GameWorld.Core.Commands.Bone
{
    public class TransformBoneCommand : IAeUndoCommandCommand
    {
        private static readonly ILogger s_logger = Logging.CreateStatic(typeof(TransformBoneCommand));
        List<int> _selectedBones;
        BoneSelectionState _boneSelectionState;
        int _currentFrame;
        KeyFrame _oldFrame;
        KeyFrame? _newFrame;
        public Matrix Transform { get; set; }

        public string HintText => "Bone Transform";

        public bool IsMutation => true;

        private Matrix _oldTransform = Matrix.Identity;
        Vector3? _ikGizmoStart;
        Vector3 _ikEffectorStart;

        ISelectionState _oldSelectionState;
        private SelectionManager _selectionManager;

        public TransformBoneCommand(SelectionManager selectionManager)
        {
            _selectionManager = selectionManager;
        }

        public void Configure(List<int> selectedBones, BoneSelectionState state)
        {
            _selectedBones = selectedBones;
            _boneSelectionState = state;
            _currentFrame = state.CurrentFrame;
            _oldFrame = _boneSelectionState.CurrentAnimation.DynamicFrames[_currentFrame].Clone();

            // A drag holds each pinned bone where it is on this frame now, not where it was on the frame it was pinned on.
            var pins = _boneSelectionState.Pins;
            for (var i = 0; i < pins.Count; i++)
            {
                if (pins[i].Bone < _oldFrame.Position.Count)
                    pins[i] = pins[i] with { Target = WorldTransform(_oldFrame, _boneSelectionState.Skeleton, pins[i].Bone).Translation };
            }
        }

        public void ApplyTransformation(Matrix newPosition, GizmoMode gizmoMode)
        {
            if (_selectedBones.Count == 0) return;

            if (_boneSelectionState.EnableInverseKinematics && gizmoMode == GizmoMode.Translate)
            {
                ApplyInverseKinematics(newPosition.Translation);
                return;
            }
            if (_oldTransform == Matrix.Identity)
            {
                _oldTransform = newPosition;
                return;
            }

            var matrixDelta = newPosition - _oldTransform;
            _oldTransform = newPosition;
            s_logger.Here().Debug($"Gizmo moved: {Transform.Translation}");
            //Console.WriteLine($" gizmo pivotPoint: {PivotPoint}");
            foreach (var selectedBone in _selectedBones)
            {
                var node = _boneSelectionState.RenderObject as Rmv2MeshNode;
                var animationPlayer = node.AnimationPlayer;
                var currentAnimFrame = animationPlayer.GetCurrentAnimationFrame();
                var currentBoneWorldTransform = currentAnimFrame.GetSkeletonAnimatedWorld(_boneSelectionState.Skeleton, selectedBone);
                currentBoneWorldTransform.Translation += matrixDelta.Translation;
                var newBoneTransform = GetSkeletonAnimatedBoneFromWorld(currentAnimFrame, _boneSelectionState.Skeleton, selectedBone, currentBoneWorldTransform);

                s_logger.Here().Debug($"Bone {selectedBone} position: {_boneSelectionState.CurrentAnimation.DynamicFrames[_currentFrame].Position[selectedBone]}");
                newBoneTransform.Decompose(out var scale, out var rot, out var trans);
                newPosition.Decompose(out var newScale, out var rot2, out var trans2);
                var modifiedTransform = _boneSelectionState.CurrentAnimation.DynamicFrames[_currentFrame].Clone();
                switch (gizmoMode)
                {
                    case GizmoMode.Translate:
                        modifiedTransform.Position[selectedBone] += trans;
                        break;
                    case GizmoMode.Rotate:
                        modifiedTransform.Rotation[selectedBone] *= rot2;
                        break;
                    case GizmoMode.NonUniformScale:
                    case GizmoMode.UniformScale:
                        modifiedTransform.Scale[selectedBone] = scale;
                        break;
                    default:
                        throw new InvalidOperationException("unknown gizmo mode");
                }

                _boneSelectionState.CurrentAnimation.DynamicFrames[_currentFrame] = modifiedTransform;
            }

            var held = ResolvePins(_boneSelectionState.CurrentAnimation.DynamicFrames[_currentFrame], _selectedBones);
            _boneSelectionState.TriggerModifiedBoneEvent([.. _selectedBones, .. held]);
        }

        public Matrix GetSkeletonAnimatedBoneFromWorld(AnimationFrame frame, GameSkeleton gameSkeleton, int boneIndex, Matrix objectInWorldTransform)
        {
            var output = objectInWorldTransform * Matrix.Invert(frame.GetSkeletonAnimatedWorld(gameSkeleton, boneIndex));
            return output;
        }

        // The gizmo and the frame need not share a space (a rider is placed on its mount), so the target is where the bone was
        // when the drag began, moved as far as the gizmo has moved since.
        void ApplyInverseKinematics(Vector3 gizmoPosition)
        {
            var effector = _selectedBones[0];
            var frame = _boneSelectionState.CurrentAnimation.DynamicFrames[_currentFrame].Clone();
            if (_ikGizmoStart == null)
            {
                _ikGizmoStart = gizmoPosition;
                _ikEffectorStart = WorldTransform(frame, _boneSelectionState.Skeleton, effector).Translation;
            }

            var chain = SolveInverseKinematics(frame, _boneSelectionState.Skeleton, effector, _boneSelectionState.InverseKinematicsEndBoneIndex,
                _ikEffectorStart + gizmoPosition - _ikGizmoStart.Value, _boneSelectionState.JointLimits);
            var held = ResolvePins(frame, [.. chain, effector]);
            _boneSelectionState.CurrentAnimation.DynamicFrames[_currentFrame] = frame;
            _boneSelectionState.TriggerModifiedBoneEvent([.. chain, .. held]);
        }

        /// <summary>Puts every pinned bone back after an edit; the bones the edit moved are never turned. Returns what it turned.</summary>
        IReadOnlyList<int> ResolvePins(KeyFrame frame, IReadOnlyCollection<int> draggedBones)
        {
            if (_boneSelectionState.Pins.Count == 0)
                return [];
            return PinSolver.Resolve(frame, _boneSelectionState.Skeleton, _boneSelectionState.Pins, draggedBones, _boneSelectionState.JointLimits);
        }

        /// <summary>
        /// The bones IK turns: the effector's parent, then each bone above it, up to and including the end bone. A bone with no
        /// parent is never in it, as turning it swings the whole model; an end bone that is not above the effector leaves the
        /// chain running up to that root.
        /// </summary>
        public static List<int> InverseKinematicsChain(GameSkeleton skeleton, int effector, int endBone)
        {
            var chain = new List<int>();
            for (var bone = skeleton.GetParentBoneIndex(effector); bone != -1 && skeleton.GetParentBoneIndex(bone) != -1; bone = skeleton.GetParentBoneIndex(bone))
            {
                chain.Add(bone);
                if (bone == endBone)
                    break;
            }
            return chain;
        }

        /// <summary>
        /// Cyclic coordinate descent: each bone of the chain, nearest the effector first, turned about its own origin so the
        /// effector points at the target, repeated until it is there. Only rotations change, so every bone keeps its length, and a
        /// target out of reach leaves the chain stretched toward it. With <paramref name="limits"/>, each turn is clamped into the
        /// joint's range, so a target the limits forbid is fallen short of. <paramref name="lockedBones"/> are left out of the
        /// chain. Returns the chain.
        /// </summary>
        public static List<int> SolveInverseKinematics(KeyFrame frame, GameSkeleton skeleton, int effector, int endBone, Vector3 target,
            JointLimits? limits = null, IReadOnlyCollection<int>? lockedBones = null, int iterations = 64, float tolerance = 0.0001f)
        {
            if (effector >= frame.Rotation.Count)
                return [];

            var chain = InverseKinematicsChain(skeleton, effector, endBone);
            if (lockedBones != null)
                chain.RemoveAll(lockedBones.Contains);
            for (var i = 0; i < iterations; i++)
            {
                foreach (var joint in chain)
                {
                    var jointPosition = WorldTransform(frame, skeleton, joint).Translation;
                    var toEffector = WorldTransform(frame, skeleton, effector).Translation - jointPosition;
                    var toTarget = target - jointPosition;
                    if (toEffector.LengthSquared() < 1e-12f || toTarget.LengthSquared() < 1e-12f)
                        continue;
                    toEffector.Normalize();
                    toTarget.Normalize();
                    var axis = Vector3.Cross(toEffector, toTarget);
                    if (axis.LengthSquared() < 1e-12f)
                        continue;
                    // Not acos of the dot: near 1 a float dot cannot tell apart angles under ~3e-4, which stalled the hand a mm short.
                    var angle = MathF.Atan2(axis.Length(), Vector3.Dot(toEffector, toTarget));
                    axis.Normalize();
                    var rotation = TurnInModelSpace(frame, skeleton, joint, Matrix.CreateFromAxisAngle(axis, angle));
                    frame.Rotation[joint] = limits == null ? rotation : limits.Clamp(skeleton, joint, rotation);
                }

                if (Vector3.Distance(WorldTransform(frame, skeleton, effector).Translation, target) < tolerance)
                    break;
            }
            return chain;
        }

        /// <summary>
        /// The bone's key rotation after turning it by <paramref name="turn"/>, a rotation in model space about the bone's own origin.
        /// The key is in the parent's space: local' = local * parent * turn * parent^-1.
        /// </summary>
        public static Quaternion TurnInModelSpace(KeyFrame frame, GameSkeleton skeleton, int bone, Matrix turn)
        {
            WorldTransform(frame, skeleton, skeleton.GetParentBoneIndex(bone)).Decompose(out _, out var parentRotation, out _);
            var parentMatrix = Matrix.CreateFromQuaternion(parentRotation);
            var rotation = Quaternion.CreateFromRotationMatrix(Matrix.CreateFromQuaternion(frame.Rotation[bone]) * parentMatrix * turn * Matrix.Transpose(parentMatrix));
            rotation.Normalize();
            return rotation;
        }

        /// <summary>A bone's model-space transform in a key frame: its local transform times each parent's, up to the root.</summary>
        public static Matrix WorldTransform(KeyFrame frame, GameSkeleton skeleton, int bone)
        {
            var world = Matrix.Identity;
            for (; bone != -1; bone = skeleton.GetParentBoneIndex(bone))
                world *= Matrix.CreateScale(frame.Scale[bone]) * Matrix.CreateFromQuaternion(frame.Rotation[bone]) * Matrix.CreateTranslation(frame.Position[bone]);
            return world;
        }

        public static void CompareKeyFrames(KeyFrame A, KeyFrame B)
        {
            for (var j = 0; j < A.Position.Count; j++)
            {
                var posDiff = A.Position[j] - B.Position[j];
                var rotDiff = A.Rotation[j].ToVector4() - B.Rotation[j].ToVector4();
                var scaleDiff = A.Scale[j] - B.Scale[j];
                if (posDiff != new Vector3(0) || rotDiff != new Vector4(0) || scaleDiff != new Vector3(0))
                    s_logger.Here().Debug($"Bone {j}: Position difference: {posDiff}, Rotation difference: {rotDiff}, Scale difference: {scaleDiff}");
            }
        }

        public void Undo()
        {
            if (_oldFrame == null) return;
            CompareKeyFrames(_oldFrame, _boneSelectionState.CurrentAnimation.DynamicFrames[_currentFrame]);
            _boneSelectionState.CurrentAnimation.DynamicFrames[_currentFrame] = _oldFrame.Clone();
            _boneSelectionState.TriggerModifiedBoneEvent(_selectedBones);
        }

        public void Execute()
        {
            _oldSelectionState = _boneSelectionState;
            // The frame as the drag left it, for redo.
            _newFrame = _boneSelectionState.CurrentAnimation.DynamicFrames[_currentFrame].Clone();
        }

        public void Redo()
        {
            if (_newFrame == null) return;
            _boneSelectionState.CurrentAnimation.DynamicFrames[_currentFrame] = _newFrame.Clone();
            _boneSelectionState.TriggerModifiedBoneEvent(_selectedBones);
        }
    }
}
