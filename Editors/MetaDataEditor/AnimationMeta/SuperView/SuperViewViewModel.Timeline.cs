using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimationMeta.Presentation;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView
{
    /// <summary>SuperView's meta tabs, by their index.</summary>
    public enum MetaTab { Persistent = 0, Clip = 1, Sound = 2 }

    /// <summary>A tag on SuperView's timeline strip. From and To are its start and end as fractions of the clip, clamped into
    /// 0 to 1, so a tag outside the clip sits at the nearer edge; Outside says its start was moved there. Tab is the one showing it.</summary>
    public sealed record TimelineTick(ParsedMetadataAttribute Tag, MetaTab Tab, float StartTime, double From, double To,
        bool IsWindow, bool Outside, bool IsSelected, string Tooltip);

    public partial class SuperViewViewModel
    {
        [ObservableProperty] IReadOnlyList<TimelineTick> _timelineTicks = [];
        [ObservableProperty] bool _hasTimeline;

        /// <summary>Where the playhead is, as a fraction of the clip.</summary>
        [ObservableProperty] double _playheadFraction;

        // A tag time is a float of seconds and the clip's length whole microseconds, so a tag at the last frame can come out a
        // hair past the end; that is still in the clip.
        const float EdgeSeconds = 1e-4f;

        // An add, delete, move, paste or undo rebuilds an editor's rows and tells the scene nothing; HasUnsavedChanges is
        // raised by every one of them and by every field edit, so the strip follows that.
        void InitTimeline()
        {
            float Now() => _asset.Data.Player.GetTimeUs() / 1_000_000f;
            foreach (var editor in new[] { PersistentMetaEditor, MetaEditor, SoundMetaEditor })
            {
                editor.Now = Now;
                editor.PropertyChanged += OnTimelineSourceChanged;
            }
            // The playback bar's readout moves on every frame the player shows, and with every jump made here.
            Player.SelectedAnimationCurrentTime.PropertyChanged += (_, _) => UpdatePlayhead();
        }

        void UpdatePlayhead()
        {
            var length = _asset.Data.Player.GetAnimationLengthUs();
            PlayheadFraction = length > 0 ? Math.Clamp(_asset.Data.Player.GetTimeUs() / (double)length, 0, 1) : 0;
        }

        /// <summary>Pauses the clip at a fraction of its length, as a click or drag on the strip does.</summary>
        public void ScrubTo(double fraction)
        {
            if (HasTimeline)
                MoveTo((long)Math.Round(Math.Clamp(fraction, 0, 1) * _asset.Data.Player.GetAnimationLengthUs()));
        }

        void MoveTo(long timeUs)
        {
            var player = _asset.Data.Player;
            player.Pause();
            player.SetTime(timeUs);
            // SetTime raises no frame event, so the playback bar's readout is moved with it.
            Player.SelectedAnimationCurrentTime.Value = timeUs / 1_000_000f;
            Player.SelectedAnimationCurrentFrame.Value = player.CurrentFrame;
            UpdatePlayhead();
        }

        void OnTimelineSourceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MetaDataEditorViewModel.HasUnsavedChanges))
                RebuildTimeline();
        }

        // The strip highlights the shown tab's pick. The scene is built again only when its highlighted pick moves to another tab's,
        // which rebuilds the strip too. A tick pressed in another tab builds it once, after its tag is picked.
        partial void OnSelectedTabControllerIndexChanged(int oldValue, int newValue)
        {
            if (_tickSwitchingTab)
                return;
            if (SceneEditor(oldValue) != SceneEditor(newValue))
                RecreateMetaDataInformation();
            else
                RebuildTimeline();
        }

        bool _tickSwitchingTab;

        MetaDataEditorViewModel EditorOf(MetaTab tab) => tab switch
        {
            MetaTab.Persistent => PersistentMetaEditor,
            MetaTab.Sound => SoundMetaEditor,
            _ => MetaEditor,
        };

        /// <summary>The editor whose picked tag the scene highlights and the handles move, with a tab shown: the Sound tab's on the Sound
        /// tab, the Animation tab's on the other two.</summary>
        MetaDataEditorViewModel SceneEditor(int tab) => tab == (int)MetaTab.Sound ? SoundMetaEditor : MetaEditor;

        void RebuildTimeline()
        {
            var length = _asset.Data.Player.GetAnimationLengthUs() / 1_000_000f;
            var shown = EditorOf((MetaTab)SelectedTabControllerIndex);
            HasTimeline = length > 0;
            TimelineTicks = length > 0 ? [.. Ticks(MetaTab.Persistent), .. Ticks(MetaTab.Clip), .. Ticks(MetaTab.Sound)] : [];
            UpdatePlayhead();

            IEnumerable<TimelineTick> Ticks(MetaTab tab)
            {
                var editor = EditorOf(tab);
                foreach (var tag in editor.ParsedFile?.Attributes ?? [])
                {
                    if (Times(tag) is not (var start, var end))
                        continue;
                    var from = Math.Clamp(start / length, 0, 1);
                    var to = end > start ? Math.Clamp(end / length, 0, 1) : from;
                    var outside = start < -EdgeSeconds || start > length + EdgeSeconds;
                    yield return new TimelineTick(tag, tab, start, from, to, end > start, outside,
                        editor == shown && tag == editor.SelectedAttribute, TickTooltip(tag, start, end, length, tab));
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

        static string TickTooltip(ParsedMetadataAttribute tag, float start, float end, float length, MetaTab tab)
        {
            var text = end > start
                ? FormattableString.Invariant($"{tag.DisplayName} runs from {start:0.00#} s to {end:0.00#} s.")
                : FormattableString.Invariant($"{tag.DisplayName} is at {start:0.00#} s.");
            if (start < -EdgeSeconds)
                text += " That's before the clip starts, so its tick sits at the left edge.";
            else if (start > length + EdgeSeconds)
                text += FormattableString.Invariant($" That's after the clip ends at {length:0.00#} s, so its tick sits at the right edge.");
            return tab switch
            {
                MetaTab.Persistent => text + " It's in the persistent meta.",
                MetaTab.Sound => text + " It's in the slot's .snd.meta.",
                _ => text,
            };
        }

        [RelayCommand]
        void SelectTick(TimelineTick? tick)
        {
            if (tick == null)
                return;

            // A tag outside the clip is gone to at the nearer end. The player stops there, as the bar's frame buttons stop it.
            MoveTo(Math.Clamp((long)Math.Round(tick.StartTime * 1_000_000d), 0, _asset.Data.Player.GetAnimationLengthUs()));

            // The tab first, so the rebuild the selection sets off highlights the tag in the tab that shows it. That rebuild is the only
            // one when the pick changes, so the tab switch skips its own then. Delete acts on every row marked selected, which the list
            // keeps in step with its own clicks but not with this one.
            var editor = EditorOf(tick.Tab);
            var row = editor.Tags.FirstOrDefault(x => x._input == tick.Tag);
            _tickSwitchingTab = editor.SelectedTag != row;
            SelectedTabControllerIndex = (int)tick.Tab;
            _tickSwitchingTab = false;
            foreach (var tag in editor.Tags)
                tag.IsSelected = tag == row;
            editor.SelectedTag = row;
        }
    }
}
