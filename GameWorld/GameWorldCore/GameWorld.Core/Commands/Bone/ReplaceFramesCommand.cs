using System;
using System.Collections.Generic;
using System.Linq;
using GameWorld.Core.Animation;
using Shared.Core.Events;

namespace GameWorld.Core.Commands.Bone
{
    /// <summary>
    /// One undo step for any edit to a set of frames: the frames are kept before and after the edit, so undo puts back the first
    /// set and redo the second, without running the edit again.
    /// </summary>
    public class ReplaceFramesCommand : IAeUndoCommandCommand
    {
        AnimationClip _clip = null!;
        int[] _frames = [];
        Action<AnimationClip> _edit = null!;
        AnimationClip.KeyFrame[] _before = [];
        AnimationClip.KeyFrame[] _after = [];

        public string HintText { get; private set; } = "Edit frames";
        public bool IsMutation => true;

        public void Configure(AnimationClip clip, IReadOnlyList<int> frames, Action<AnimationClip> edit, string hintText)
        {
            _clip = clip;
            _frames = frames.Distinct().ToArray();
            _edit = edit;
            HintText = hintText;
        }

        public void Execute()
        {
            _before = Snapshot();
            _edit(_clip);
            _after = Snapshot();
        }

        public void Undo() => Restore(_before);

        public void Redo() => Restore(_after);

        AnimationClip.KeyFrame[] Snapshot() => _frames.Select(x => _clip.DynamicFrames[x].Clone()).ToArray();

        void Restore(AnimationClip.KeyFrame[] frames)
        {
            for (var i = 0; i < _frames.Length; i++)
                _clip.DynamicFrames[_frames[i]] = frames[i].Clone();
        }
    }
}
