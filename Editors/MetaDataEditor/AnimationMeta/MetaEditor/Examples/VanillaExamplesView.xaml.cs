using System.Windows;
using System.Windows.Controls;

namespace Editors.AnimationMeta.MetaEditor.Examples
{
    public partial class VanillaExamplesView : UserControl
    {
        // The button inserts a new tag in the New tag dialog and copies into the selected tag in the editor, so each host names it.
        public static readonly DependencyProperty UseLabelProperty = DependencyProperty.Register(nameof(UseLabel), typeof(string), typeof(VanillaExamplesView));
        public static readonly DependencyProperty UseToolTipProperty = DependencyProperty.Register(nameof(UseToolTip), typeof(string), typeof(VanillaExamplesView));

        public string UseLabel { get => (string)GetValue(UseLabelProperty); set => SetValue(UseLabelProperty, value); }
        public string UseToolTip { get => (string)GetValue(UseToolTipProperty); set => SetValue(UseToolTipProperty, value); }

        public VanillaExamplesView()
        {
            InitializeComponent();
        }
    }
}
