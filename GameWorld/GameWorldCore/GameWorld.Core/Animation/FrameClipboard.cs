using System.Collections.Generic;
using System.Linq;
using GameWorld.Core.Animation.Posing;
using GameWorld.Core.Commands.Bone.Clipboard;
using static GameWorld.Core.Animation.AnimationClip;

namespace GameWorld.Core.Animation
{
    /// <summary>
    /// Copies some bones over a run of frames, and pastes them into a clip from a given frame on, by bone name. The data is
    /// the old global clipboard's, so a copy made with either pastes with either, and between skeletons whose names match.
    /// </summary>
    public static class FrameClipboard
    {
        public sealed record Channels(bool Position, bool Rotation, bool Scale);

        /// <param name="Frames">Frames written.</param>
        /// <param name="Skipped">Copied frames that fell past the end of the clip.</param>
        /// <param name="Bones">The target skeleton's bones written, in the order the copy names them.</param>
        /// <param name="MissingBones">Copied bones the target skeleton has no bone of that name for.</param>
        public sealed record PasteResult(int Frames, int Skipped, IReadOnlyList<int> Bones, IReadOnlyList<string> MissingBones);

        /// <summary>The bones over frames <paramref name="firstFrame"/> to <paramref name="lastFrame"/>, both included, keyed from 0.</summary>
        public static BoneTransformClipboardData Copy(AnimationClip clip, GameSkeleton skeleton, IReadOnlyCollection<int> bones, int firstFrame, int lastFrame)
        {
            var data = new BoneTransformClipboardData { SkeletonName = skeleton.SkeletonName };
            for (var frameNr = firstFrame; frameNr <= lastFrame; frameNr++)
            {
                var source = clip.DynamicFrames[frameNr];
                var frame = new BoneTransformClipboardData.Frame();
                foreach (var bone in bones.Order())
                {
                    var name = skeleton.BoneNames[bone];
                    frame.BoneIdToPosition[name] = source.Position[bone];
                    frame.BoneIdToQuaternion[name] = source.Rotation[bone];
                    frame.BoneIdToScale[name] = source.Scale[bone];
                }
                data.Frames[frameNr - firstFrame] = frame;
            }
            return data;
        }

        /// <summary>
        /// Writes the copy into the clip, its first frame on <paramref name="atFrame"/>. Flipped, each copied bone lands on its
        /// left/right partner as the mirror of the copy: exact when every bone is pasted, and for one limb as exact as the body
        /// above it is symmetrical, since the limb is mirrored as if that body were mirrored too (as Blender's Paste Flipped).
        /// </summary>
        public static PasteResult Paste(AnimationClip clip, GameSkeleton skeleton, BoneTransformClipboardData data, int atFrame, Channels channels, bool flipped)
        {
            var names = data.Frames.Values.SelectMany(x => x.BoneIdToQuaternion.Keys.Concat(x.BoneIdToPosition.Keys).Concat(x.BoneIdToScale.Keys)).Distinct().ToList();
            var bones = names.Select(skeleton.GetBoneIndexByName).Where(x => x >= 0).ToList();
            var missing = names.Where(x => skeleton.GetBoneIndexByName(x) < 0).ToList();
            var pairs = flipped ? PoseMirror.Pairs(skeleton) : null;

            var written = 0;
            var skipped = 0;
            var offset = 0;
            foreach (var (_, copied) in data.Frames.OrderBy(x => x.Key))
            {
                var frameNr = atFrame + offset++;
                if (frameNr < 0 || frameNr >= clip.DynamicFrames.Count)
                {
                    skipped++;
                    continue;
                }

                var target = clip.DynamicFrames[frameNr];
                if (pairs == null)
                {
                    Write(target, skeleton, copied, channels);
                }
                else
                {
                    // The whole frame is mirrored with the copy in it, then only the copied bones' partners are taken from it.
                    var mirrored = target.Clone();
                    Write(mirrored, skeleton, copied, new Channels(true, true, true));
                    PoseMirror.Apply(mirrored, skeleton, MirrorMode.Flip);
                    foreach (var bone in bones)
                        Take(target, mirrored, pairs[bone], channels);
                }
                written++;
            }

            var result = pairs == null ? bones : bones.Select(x => pairs[x]).ToList();
            return new PasteResult(written, skipped, result, missing);
        }

        static void Write(KeyFrame target, GameSkeleton skeleton, BoneTransformClipboardData.Frame copied, Channels channels)
        {
            if (channels.Position)
                foreach (var (name, value) in copied.BoneIdToPosition)
                    if (skeleton.GetBoneIndexByName(name) is var bone and >= 0)
                        target.Position[bone] = value;
            if (channels.Rotation)
                foreach (var (name, value) in copied.BoneIdToQuaternion)
                    if (skeleton.GetBoneIndexByName(name) is var bone and >= 0)
                        target.Rotation[bone] = value;
            if (channels.Scale)
                foreach (var (name, value) in copied.BoneIdToScale)
                    if (skeleton.GetBoneIndexByName(name) is var bone and >= 0)
                        target.Scale[bone] = value;
        }

        static void Take(KeyFrame target, KeyFrame from, int bone, Channels channels)
        {
            if (channels.Position)
                target.Position[bone] = from.Position[bone];
            if (channels.Rotation)
                target.Rotation[bone] = from.Rotation[bone];
            if (channels.Scale)
                target.Scale[bone] = from.Scale[bone];
        }
    }
}
