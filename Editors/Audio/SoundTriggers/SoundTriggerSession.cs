#nullable enable
namespace Editors.Audio.SoundTriggers
{
    /// <summary>One SOUND_TRIGGER tag of an animation's meta file. SoundEvent is the tag's key, as the tags table spells it; File names where it came from, for the status lines.</summary>
    public sealed record SoundTriggerTag(string SoundEvent, float StartTime, string File);

    /// <summary>The sound triggers of the animation on screen: resolves them once, fires the ones the playhead crosses, and keeps what a
    /// panel shows about them (the switch groups, and a line for every trigger that plays nothing here). It plays through delegates, so it needs no audio device.</summary>
    /// <param name="prepare">Called once for every source id the tags can play, ahead of time. Its result is not used: a WEM that fails to decode is the mixer's to report.</param>
    /// <param name="play">Called once for each source id a firing plays.</param>
    public sealed class SoundTriggerSession(SoundTriggerResolver resolver, Func<uint, bool> prepare, Action<uint> play, Random random)
    {
        List<(double Start, SoundNode Sound)> _playable = [];
        List<uint> _leaves = [];

        // NaN until the first frame, which makes that frame a jump.
        double _previous = double.NaN;
        // The last frame was a paused one, so the next playing frame starts from where the playhead was put.
        bool _paused;
        // Loop has fired the end of the pass, so the next playing frame is the start of the next one.
        bool _looped;

        /// <summary>Real seconds to start sounds ahead of their tag: positive starts them early, negative late. It makes up for a delay
        /// in real time, so it covers more of the clip at a faster speed: each frame turns it into clip time at that frame's speed.</summary>
        public double OffsetSeconds { get; set; }

        /// <summary>The switch case to play, by group id. Kept across Rebuilds; a Rebuild is what applies a change.</summary>
        public Dictionary<uint, uint> SwitchChoices { get; } = [];

        /// <summary>The switch groups the tags' sounds listen to, one per group id, from the first tag that has it.</summary>
        public IReadOnlyList<SwitchGroupSeen> Switches { get; private set; } = [];

        /// <summary>A summary line, then one line for each trigger that plays nothing here and why: silent in game, or not supported.</summary>
        public IReadOnlyList<string> StatusLines { get; private set; } = [];

        /// <summary>Every source id the current tags can play, each once.</summary>
        public IEnumerable<uint> Leaves => _leaves;

        /// <summary>Resolves the tags again, after a new clip, an edit or a changed switch choice. The playhead time is kept, so an edit
        /// while playing does not make the triggers just passed fire again.</summary>
        public void Rebuild(IReadOnlyList<SoundTriggerTag> tags, bool campaign)
        {
            var resolved = tags.Select(tag => (Tag: tag, Result: resolver.Resolve(tag.SoundEvent, campaign, SwitchChoices))).ToList();

            _playable = resolved
                .Where(x => x.Result.Sound != null)
                .Select(x => (Start: (double)x.Tag.StartTime, Sound: x.Result.Sound!))
                .ToList();
            _leaves = _playable.SelectMany(x => SoundNodes.Leaves(x.Sound)).Distinct().ToList();
            foreach (var id in _leaves)
                prepare(id);

            Switches = resolved.SelectMany(x => x.Result.Switches).DistinctBy(x => x.GroupId).ToList();

            // What SuperView does not play may sound in game, so it is counted apart from what is silent there.
            var silent = resolved.Where(x => x.Result.Silent != null).ToList();
            var unsupported = silent.Count(x => x.Result.Silent!.StartsWith(SoundTriggerResolver.NotSupported, StringComparison.Ordinal));
            var summary = resolved.Count == 0
                ? "0 triggers"
                : $"{resolved.Count} {(resolved.Count == 1 ? "trigger" : "triggers")}: {_playable.Count} {(_playable.Count == 1 ? "plays" : "play")}, {silent.Count - unsupported} silent in game"
                    + (unsupported == 0 ? "" : $", {unsupported} not supported");
            StatusLines = [summary, .. silent.Select(x => $"{x.Tag.SoundEvent} ({x.Tag.File}): {x.Result.Silent}")];
        }

        /// <summary>One frame. While playing, fires every trigger the playhead crossed since the last frame; otherwise (a paused frame step) only remembers where it is.
        /// Play from a paused frame includes the frame it starts on, so a tag at 0.0 fires after First Frame and Play.</summary>
        /// <param name="now">The playhead, in clip seconds.</param>
        /// <param name="elapsedSeconds">Real time since the last frame.</param>
        /// <param name="speed">Playback speed, 1 being normal.</param>
        /// <param name="length">The clip's length in seconds.</param>
        /// <param name="loop">Whether the clip starts again at its end. Without it, a due time is never wrapped to the other end.</param>
        public void Advance(double now, bool playing, double elapsedSeconds, double speed, double length, bool loop)
        {
            var (previous, paused, looped) = (_previous, _paused, _looped);
            _previous = now;
            _paused = !playing;
            // The player stops at the clip's end and reports the step that reached it as paused, but that step is the last of the
            // pass. A pause or a frame step by hand never lands on the end itself.
            var reachedTheEnd = !playing && !paused && !looped && now >= length;
            if (!playing && !reachedTheEnd)
                return;
            _looped = false;

            // After Loop, this frame is the second half of a wrap, from 0 to now: what was due before 0 fired with the first half.
            var step = looped ? (length, now, true) : SoundTriggerCrossing.Step(previous, now, length, elapsedSeconds, speed);
            if (paused && !looped && step == (previous, now, false))
                step = (previous - 1e-6, now, false);
            Fire(step, OffsetSeconds * speed, length, loop);
        }

        /// <summary>The player has started the clip again by itself: playback entered the last frame, short of the clip's end, and the
        /// player jumped back to frame 0. Fires every trigger due from the last frame to the clip's end, and makes the next playing frame
        /// the wrap's second half, from 0 to where it is. Advance would take the jump back as a scrub and fire none of what was skipped.</summary>
        public void Loop(double speed, double length)
        {
            if (!double.IsNaN(_previous))
                Fire((_paused ? _previous - 1e-6 : _previous, length, false), OffsetSeconds * speed, length, true);
            _previous = 0;
            _looped = true;
        }

        void Fire((double From, double To, bool Wrapped) step, double offset, double length, bool loop)
        {
            foreach (var (start, sound) in _playable)
            {
                if (!Fires(start, offset, step, length, loop))
                    continue;

                foreach (var id in SoundNodes.Pick(sound, random))
                    play(id);
            }
        }

        /// <summary>A tag that lies outside the clip never fires. Otherwise it fires when the time it is due, its start less the offset,
        /// lies in the step. When the clip loops, a due time past either end counts at the other end, however far past: a tag at 0.0
        /// started early is due just before the loop, and one at the clip's end started late is due just after it. When it plays once,
        /// a due time before 0 counts at the start and one past the end at the end.</summary>
        /// <param name="offset">The offset in clip seconds, at this frame's speed.</param>
        static bool Fires(double tagStart, double offset, (double From, double To, bool Wrapped) step, double length, bool loop)
        {
            if (tagStart < 0 || tagStart > length)
                return false;

            var due = tagStart - offset;
            var dueInClip = !loop ? Math.Clamp(due, 0, length)
                : due >= 0 && due <= length ? due
                : due - length * Math.Floor(due / length);
            if (SoundTriggerCrossing.Crossed(dueInClip, step, length))
                return true;

            // A step that covers 0 without wrapping starts a pass whose end never played: play from the first frame, or a jump to the
            // start. A tag due before 0 is due at the start of it.
            return due < 0 && !step.Wrapped && step.From < 0 && step.To >= 0;
        }
    }
}
