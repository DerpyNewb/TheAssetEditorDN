#nullable enable
namespace Editors.Audio.SoundTriggers
{
    /// <summary>Decides which sound triggers a frame of animation playback passed over, so each one fires once per pass
    /// and a scrub or a loop does not fire the whole stretch it skipped.</summary>
    public static class SoundTriggerCrossing
    {
        /// <summary>How much more than one frame's playback a move can cover and still count as ordinary playback.</summary>
        public const double SlackSeconds = 0.05;

        /// <summary>The longest a frame is allowed to have lasted. A hitch or a long pause must not widen the window.</summary>
        public const double MaxFrameSeconds = 0.1;

        /// <summary>The clip time one playing frame covered. After a jump (a scrub, or a resume after scrubbing while paused) only the
        /// last frame before where it landed counts, so nothing in the skipped stretch fires.</summary>
        /// <param name="previous">Where the playhead was on the last frame; NaN when there was none, which counts as a jump.</param>
        /// <param name="elapsedSeconds">Real time since the last frame. It is capped, then scaled by speed.</param>
        /// <returns>A span from (exclusive) to (inclusive) in clip time. When Wrapped, the clip looped: the span runs from From
        /// to the end of the clip and again from 0 to To.</returns>
        public static (double From, double To, bool Wrapped) Step(double previous, double now, double length, double elapsedSeconds, double speed)
        {
            var frame = Math.Min(elapsedSeconds, MaxFrameSeconds) * speed;
            var allowed = frame + SlackSeconds;

            var ahead = now - previous;
            if (ahead >= 0 && ahead <= allowed)
                return (previous, now, false);
            if (now < previous && length - previous + now <= allowed)
                return (previous, now, true);

            // The nudge keeps a tag that sits exactly at the start of the last frame inside the open end of the span.
            return (now - frame - 1e-6, now, false);
        }

        /// <summary>Whether a tag at this clip time lies in the span. A tag outside the clip never does, even when a jump's span reaches back before zero.</summary>
        public static bool Crossed(double start, (double From, double To, bool Wrapped) step, double length)
        {
            if (start < 0 || start > length)
                return false;

            return step.Wrapped
                ? start > step.From || start <= step.To
                : start > step.From && start <= step.To;
        }
    }
}
