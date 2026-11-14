using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimationMeta.Presentation;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView
{
    /// <summary>A tag on SuperView's timeline strip. From and To are its start and end as fractions of the clip, clamped into
    /// 0 to 1, so a tag outside the clip sits at the nearer edge; Outside says its start was moved there.</summary>
    public sealed record TimelineTick(ParsedMetadataAttribute Tag, bool Persistent, float StartTime, double From, double To,
        bool IsWindow, bool Outside, bool IsSelected, string Tooltip);

    public partial class SuperViewViewModel
    {
        [ObservableProperty] IReadOnlyList<TimelineTick> _timelineTicks = [];
        [ObservableProperty] bool _hasTimeline;

        // A tag time is a float of seconds and the clip's length whole microseconds, so a tag at the last frame can come out a
        // hair past the end; that is still in the clip.
        const float EdgeSeconds = 1e-4f;

        // An add, delete, move, paste or undo rebuilds an editor's rows and tells the scene nothing; HasUnsavedChanges is
        // raised by every one of them and by every field edit, so the strip follows that.
        void InitTimeline()
        {
            float Now() => _asset.Data.Player.GetTimeUs() / 1_000_000f;
            PersistentMetaEditor.Now = Now;
            MetaEditor.Now = Now;
            PersistentMetaEditor.PropertyChanged += OnTimelineSourceChanged;
            MetaEditor.PropertyChanged += OnTimelineSourceChanged;
        }

        void OnTimelineSourceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MetaDataEditorViewModel.HasUnsavedChanges))
                RebuildTimeline();
        }

        partial void OnSelectedTabControllerIndexChanged(int value) => RebuildTimeline();

        void RebuildTimeline()
        {
            var length = _asset.Data.Player.GetAnimationLengthUs() / 1_000_000f;
            var shown = SelectedTabControllerIndex == 0 ? PersistentMetaEditor : MetaEditor;
            HasTimeline = length > 0;
            TimelineTicks = length > 0 ? [.. Ticks(PersistentMetaEditor, true), .. Ticks(MetaEditor, false)] : [];

            IEnumerable<TimelineTick> Ticks(MetaDataEditorViewModel editor, bool persistent)
            {
                foreach (var tag in editor.ParsedFile?.Attributes ?? [])
                {
                    if (Times(tag) is not (var start, var end))
                        continue;
                    var from = Math.Clamp(start / length, 0, 1);
                    var to = end > start ? Math.Clamp(end / length, 0, 1) : from;
                    var outside = start < -EdgeSeconds || start > length + EdgeSeconds;
                    yield return new TimelineTick(tag, persistent, start, from, to, end > start, outside,
                        editor == shown && tag == editor.SelectedAttribute, TickTooltip(tag, start, end, length, persistent));
                }
            }
        }

        // Only the two time bases carry Start Time and End Time.
        static (float Start, float End)? Times(ParsedMetadataAttribute tag) => tag switch
        {
            DecodedMetaEntryBase x => (x.StartTime, x.EndTime),
            DecodedMetaEntryBase_v2 x => (x.StartTime, x.EndTime),
            _ => null,
        };

        static string TickTooltip(ParsedMetadataAttribute tag, float start, float end, float length, bool persistent)
        {
            var text = end > start
                ? FormattableString.Invariant($"{tag.DisplayName} runs from {start:0.00#} s to {end:0.00#} s.")
                : FormattableString.Invariant($"{tag.DisplayName} is at {start:0.00#} s.");
            if (start < -EdgeSeconds)
                text += " That's before the clip starts, so its tick sits at the left edge.";
            else if (start > length + EdgeSeconds)
                text += FormattableString.Invariant($" That's after the clip ends at {length:0.00#} s, so its tick sits at the right edge.");
            return persistent ? text + " It's in the persistent meta." : text;
        }

        [RelayCommand]
        void SelectTick(TimelineTick? tick)
        {
            if (tick == null)
                return;

            // A tag outside the clip is gone to at the nearer end. The player stops there, as the bar's frame buttons stop it.
            var player = _asset.Data.Player;
            var timeUs = Math.Clamp((long)Math.Round(tick.StartTime * 1_000_000d), 0, player.GetAnimationLengthUs());
            player.Pause();
            player.SetTime(timeUs);
            // SetTime raises no frame event, so the playback bar's readout is moved with it.
            Player.SelectedAnimationCurrentTime.Value = timeUs / 1_000_000f;
            Player.SelectedAnimationCurrentFrame.Value = player.CurrentFrame;

            // The tab first, so the rebuild the selection sets off highlights the tag in the tab that shows it. Delete acts on every
            // row marked selected, which the list keeps in step with its own clicks but not with this one.
            SelectedTabControllerIndex = tick.Persistent ? 0 : 1;
            var editor = tick.Persistent ? PersistentMetaEditor : MetaEditor;
            var row = editor.Tags.FirstOrDefault(x => x._input == tick.Tag);
            foreach (var tag in editor.Tags)
                tag.IsSelected = tag == row;
            editor.SelectedTag = row;
        }
    }
}
