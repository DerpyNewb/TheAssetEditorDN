// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Shared.Ui.Editors.BoneMapping;

namespace CommonControls.Editors.BoneMapping.View
{
    /// <summary>
    /// Interaction logic for BoneMappingView.xaml
    /// </summary>
    public partial class BoneMappingView : UserControl
    {
        public BoneMappingView()
        {
            InitializeComponent();
        }

        // A click on the target bone that is already selected changes no selection, so the tree raises nothing and the
        // mapping is made here. Mapping two mesh bones onto one target (both eyes onto head) needed another target in between.
        void TargetBoneTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not BoneMappingViewModel viewModel || e.OriginalSource is not DependencyObject clicked)
                return;
            if (FindAncestor<ToggleButton>(clicked) != null)    // the expander arrow
                return;
            if (FindAncestor<TreeViewItem>(clicked)?.DataContext is AnimatedBone bone && ReferenceEquals(bone, viewModel.ParentModelBones.SelectedItem))
                viewModel.MapSelectedMeshBoneTo(bone);
        }

        static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
        {
            while (node != null && node is not T)
                node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            return node as T;
        }
    }
}
