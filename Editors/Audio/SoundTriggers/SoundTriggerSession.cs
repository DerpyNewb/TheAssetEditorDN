#nullable enable
namespace Editors.Audio.SoundTriggers
{
    /// <summary>One SOUND_TRIGGER tag of an animation's meta file. SoundEvent is the tag's key, as the tags table spells it; File names where it came from, for the status lines.</summary>
    public sealed record SoundTriggerTag(string SoundEvent, float StartTime, string File);

    /// <summary>The sound triggers of the animation on screen: resolves them once, fires the ones the playhead crosses, and keeps what a
    /// panel shows about them (the switch groups, and a line for every trigger that stays silent). It plays through delegates, so it needs no audio device.</summary>
    /// <param name="prepare">Called once for every source id the tags can play, ahead of time. Its result is not used: a WEM that fails to decode is the mixer's to report.</param>
    /// <param name="play">Called once for each source id a firing plays.</param>
    public sealed class SoundTriggerSession(SoundTriggerResolver resolver, Func<uint, bool> prepare, Action<uint> play, Random random)
    {
        List<(double Start, SoundNode Sound)> _playable = [];
        List<uint> _leaves = [];

        // NaN until the first frame, which makes that frame a jump.
        double _previous = double.NaN;

        /// <summary>Real seconds to start sounds ahead of their tag: positive starts them early, negative late. It makes up for a delay
        /// in real time, so it covers more of the clip at a faster speed: each frame turns it into clip time at that frame's speed.</summary>
        public double OffsetSeconds { get; set; }

        /// <summary>The switch case to play, by group id. Kept across Rebuilds; a Rebuild is what applies a change.</summary>
        public Dictionary<uint, uint> SwitchChoices { get; } = [];

        /// <summary>The switch groups the tags' sounds listen to, one per group id, from the first tag that has it.</summary>
        public IReadOnlyList<SwitchGroupSeen> Switches { get; private set; } = [];

        /// <summary>A summary line, then one line for each trigger that plays nothing in game and why.</summary>
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

            var silent = resolved.Where(x => x.Result.Silent != null).ToList();
            var summary = resolved.Count == 0
                ? "0 triggers"
                : $"{resolved.Count} {(resolved.Count == 1 ? "trigger" : "triggers")}: {_playable.Count} {(_playable.Count == 1 ? "plays" : "play")}, {silent.Count} silent in game";
            StatusLines = [summary, .. silent.Select(x => $"{x.Tag.SoundEvent} ({x.Tag.File}): {x.Result.Silent}")];
        }

        /// <summary>One frame. While playing, fires every trigger the playhead crossed since the last frame; otherwise (a paused frame step) only remembers where it is.</summary>
        /// <param name="now">The playhead, in clip seconds.</param>
        /// <param name="elapsedSeconds">Real time since the last frame.</param>
        /// <param name="speed">Playback speed, 1 being normal.</param>
        /// <param name="length">The clip's length in seconds.</param>
        public void Advance(double now, bool playing, double elapsedSeconds, double speed, double length)
        {
            var previous = _previous;
            _previous = now;
            if (!playing)
                return;

            var step = SoundTriggerCrossing.Step(previous, now, length, elapsedSeconds, speed);
            var offset = OffsetSeconds * speed;
            foreach (var (start, sound) in _playable)
            {
                if (!Fires(start, offset, step, length))
                    continue;

                foreach (var id in SoundNodes.Pick(sound, random))
                    play(id);
            }
        }

        /// <summary>A tag that lies outside the clip never fires. Otherwise it fires when the time it is due, its start less the offset,
        /// lies in the step. The clip loops, so a due time past either end counts at the other end: a tag at 0.0 started early is due
        /// just before the loop, and one at the clip's end started late is due just after it.</summary>
        /// <param name="offset">The offset in clip seconds, at this frame's speed.</param>
        static bool Fires(double tagStart, double offset, (double From, double To, bool Wrapped) step, double length)
        {
            if (tagStart < 0 || tagStart > length)
                return false;

            var due = tagStart - offset;
            var dueInClip = due < 0 ? due + length : due > length ? due - length : due;
            if (SoundTriggerCrossing.Crossed(dueInClip, step, length))
                return true;

            // Only a jump landing within a frame of the clip's start has a span that reaches back before zero, which is where an early tag at
            // 0.0 is due. The pass it belongs to never happened (play just started, or the playhead was moved), so it fires with the jump.
            return due < 0 && due > step.From;
        }
    }
}
