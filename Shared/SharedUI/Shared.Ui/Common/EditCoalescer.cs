using System;
using Shared.Ui.Common.Behaviors;

namespace Shared.Ui.Common
{
    /// <summary>Whether an edit carries on the undo step before it: the same value, in the same scrub box drag (ScrubGesture), or,
    /// out of a drag, under Window after the last edit to it, as a plain Slider dragged or a number typed key by key arrives.</summary>
    // ponytail: a plain Slider says nothing when its drag starts or ends, so a pause longer than Window mid-drag makes two steps;
    // hook its Thumb's DragStarted and DragCompleted to ScrubGesture if that ever matters.
    public sealed class EditCoalescer
    {
        public static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

        object? _key;
        int _gesture;
        DateTime _at;

        /// <summary>Notes an edit to key, now; true when it carries on the last one.</summary>
        public bool Continues(object key, DateTime? now = null)
        {
            var at = now ?? DateTime.UtcNow;
            var continues = Equals(key, _key) && _gesture == ScrubGesture.Id && (ScrubGesture.IsActive || at - _at < Window);
            (_key, _gesture, _at) = (key, ScrubGesture.Id, at);
            return continues;
        }

        /// <summary>The next edit starts a step of its own, whatever it is.</summary>
        public void Break() => _key = null;
    }
}
