using CommunityToolkit.Mvvm.Input;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.Presentation
{
    // "Set start to now" and "Set end to now". SuperView hands its two meta editors its player's clock; the standalone editor
    // has no player, leaves the clock null and hides the buttons.
    public partial class MetaDataEditorViewModel
    {
        Func<float>? _now;

        /// <summary>The player's time in seconds, or null where there is no player.</summary>
        public Func<float>? Now
        {
            get => _now;
            set
            {
                _now = value;
                OnPropertyChanged(nameof(HasPlayer));
                SetStartToNowCommand.NotifyCanExecuteChanged();
                SetEndToNowCommand.NotifyCanExecuteChanged();
            }
        }

        public bool HasPlayer => _now != null;

        // Only the two time bases carry Start Time and End Time; an older version or a tag that did not decode has neither.
        bool SelectedTagHasTimes() => _now != null && SelectedTag?._input is DecodedMetaEntryBase or DecodedMetaEntryBase_v2;

        [RelayCommand(CanExecute = nameof(SelectedTagHasTimes))] void SetStartToNow() => SetToNow(nameof(DecodedMetaEntryBase.StartTime));
        [RelayCommand(CanExecute = nameof(SelectedTagHasTimes))] void SetEndToNow() => SetToNow(nameof(DecodedMetaEntryBase.EndTime));

        // Written through the field's box, as if typed, so the box, the unsaved mark and SuperView all follow. _edits.Break()
        // on both sides makes the press an undo step of its own, never part of a drag or typed edit around it.
        void SetToNow(string property)
        {
            if (!SelectedTagHasTimes())
                return;
            var field = SelectedTag!.Variables.First(x => x.FieldName.Replace(" ", "") == property);
            _edits.Break();
            field.ValueAsString = _now!().ToString();
            _edits.Break();
        }
    }
}
