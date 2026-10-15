using System;
using System.Collections.Generic;
using System.Linq;
using GameWorld.Core.Animation;
using Shared.Core.Events;

namespace GameWorld.Core.Commands.Bone
{
    /// <summary>
    /// Every frame between keyframes A and B set to the pose interpolated between them by how far along it is: the frame a third
    /// of the way from A to B gets the pose a third of the way. A and B are left as they are. One undo step for all of them.
    /// </summary>
    public class InterpolateAcrossFramesBoneCommand : IAeUndoCommandCommand
    {
        public string HintText => "interpolates every frame between 2 frames";
        public bool IsMutation => true;

        int _frameANr;
        int _frameBNr;
        GameSkeleton _skeleton = null!;
        List<int>? _bones;
        AnimationClip _animationBackup = null!;
        AnimationClip _target = null!;
        bool _interpolatePosition;
        bool _interpolateRotation;
        bool _interpolateScale;

        /// <param name="bones">The bones to interpolate; null for all of them.</param>
        public void Configure(AnimationClip animation, int frameANr, int frameBNr, GameSkeleton skeleton, List<int>? bones,
            bool interpolatePosition = true, bool interpolateRotation = true, bool interpolateScale = true)
        {
            _frameANr = frameANr;
            _frameBNr = frameBNr;
            _skeleton = skeleton;
            _bones = bones?.ToList();
            _animationBackup = animation.Clone();
            _target = animation;
            _interpolatePosition = interpolatePosition;
            _interpolateRotation = interpolateRotation;
            _interpolateScale = interpolateScale;
        }

        /// <summary>The frames strictly between A and B, whichever of the two comes first.</summary>
        public static IEnumerable<int> Between(int frameANr, int frameBNr) =>
            Enumerable.Range(Math.Min(frameANr, frameBNr) + 1, Math.Max(0, Math.Abs(frameBNr - frameANr) - 1));

        public void Execute()
        {
            foreach (var frameNr in Between(_frameANr, _frameBNr))
            {
                // From the backup, so each frame is sampled from A and B as they were, not from frames this loop already wrote.
                var t = (frameNr - _frameANr) / (float)(_frameBNr - _frameANr);
                var sampled = AnimationSampler.SampleBetween2Frames(_frameANr, _frameBNr, t, _skeleton, _animationBackup);
                var frame = _target.DynamicFrames[frameNr];
                foreach (var bone in _bones ?? Enumerable.Range(0, sampled.BoneTransforms.Count).ToList())
                {
                    if (_interpolatePosition) frame.Position[bone] = sampled.BoneTransforms[bone].Translation;
                    if (_interpolateRotation) frame.Rotation[bone] = sampled.BoneTransforms[bone].Rotation;
                    if (_interpolateScale) frame.Scale[bone] = sampled.BoneTransforms[bone].Scale;
                }
            }
        }

        public void Undo()
        {
            foreach (var frameNr in Between(_frameANr, _frameBNr))
                _target.DynamicFrames[frameNr] = _animationBackup.DynamicFrames[frameNr].Clone();
        }
    }
}
