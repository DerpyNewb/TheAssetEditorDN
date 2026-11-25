using Editors.AnimationMeta.Presentation;

namespace Editors.AnimationMeta.SuperView
{
    /// <summary>SuperView's meta tabs, by their index. MetaDataDocumentOwner lists the same files in the same order.</summary>
    public enum MetaTab { Persistent = 0, Clip = 1, Sound = 2 }

    public partial class SuperViewViewModel
    {
        // Set start to now and Set end to now read the player's time.
        void InitTimeline()
        {
            float Now() => _asset.Data.Player.GetTimeUs() / 1_000_000f;
            foreach (var editor in AllEditors)
                editor.Now = Now;
        }

        // The scene highlights the open tab's pick and the gizmo goes on it, so a tab switch builds the scene again when the two
        // tabs' picks differ. A marker clicked in another tab builds it once, after its tag is picked (OpenTag).
        partial void OnSelectedTabControllerIndexChanged(int oldValue, int newValue)
        {
            NotifyEditState();
            if (_tickSwitchingTab)
                return;
            if (EditorOf((MetaTab)oldValue).SelectedAttribute != EditorOf((MetaTab)newValue).SelectedAttribute)
                RecreateMetaDataInformation();
            ShowTimelineSelection();
            UpdateEditTarget();
        }

        bool _tickSwitchingTab;

        MetaDataEditorViewModel EditorOf(MetaTab tab) => tab switch
        {
            MetaTab.Persistent => PersistentMetaEditor,
            MetaTab.Sound => SoundMetaEditor,
            _ => MetaEditor,
        };

        /// <summary>The open tab's editor: the scene highlights its picked tag, the gizmo moves it, and the toolbar's undo is its.</summary>
        MetaDataEditorViewModel CurrentEditor => EditorOf((MetaTab)SelectedTabControllerIndex);
    }
}
