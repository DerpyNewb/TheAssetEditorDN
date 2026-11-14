using Editors.KitbasherEditor.Core.MenuBarViews;
using Editors.KitbasherEditor.Services;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor.UiCommands
{
    public class PickBoneModeCommand : ITransientKitbasherUiCommand
    {
        public string ToolTip { get; set; } = "Pick bones in the 3D view. While this is on, a click picks the bone of the skeleton nearest the cursor instead of selecting, and the Pin tool's Bone mode, the Snap window and the Skeleton panel select it. A drag still selects. Click again to turn it off.";
        public ActionEnabledRule EnabledRule => ActionEnabledRule.Always;
        public Hotkey? HotKey { get; } = null;

        private readonly BonePicker _bonePicker;

        public PickBoneModeCommand(BonePicker bonePicker)
        {
            _bonePicker = bonePicker;
        }

        public void Execute() => _bonePicker.IsActive = !_bonePicker.IsActive;
    }
}
