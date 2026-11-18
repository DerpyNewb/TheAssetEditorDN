using System.Windows.Controls;
using Editors.CampaignAnimationSetEditor.ViewModels;
using Shared.Ui.Common;

namespace Editors.CampaignAnimationSetEditor.Views
{
    public partial class CampaignAnimationSetEditorView : UserControl
    {
        public CampaignAnimationSetEditorView()
        {
            InitializeComponent();
            // A typed edit is one undo step, taken when the field is left.
            LostKeyboardFocus += (_, _) => (DataContext as CampaignAnimationSetEditorViewModel)?.Checkpoint();
            UndoRedoKeys.Attach(this, () => (DataContext as CampaignAnimationSetEditorViewModel)?.UndoAction(), () => (DataContext as CampaignAnimationSetEditorViewModel)?.RedoAction());
        }
    }
}
