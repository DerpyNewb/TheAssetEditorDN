using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CommonControls.Editors.BoneMapping.View;
using Shared.Core.Services;
using Shared.Ui.Common.ValueConverters;
using Shared.Ui.Editors.BoneMapping;

namespace Test.Editors.Shared.Core.BoneMapping
{
    [Apartment(ApartmentState.STA)]
    public class BoneMappingViewTests
    {
        AnimatedBone _sourceRoot = null!, _eyeLeft = null!, _eyeRight = null!;
        AnimatedBone _targetRoot = null!, _head = null!;
        BoneMappingViewModel _viewModel = null!;
        BoneMappingView _view = null!;
        Window _window = null!;

        [SetUp]
        public void Open()
        {
            _sourceRoot = new AnimatedBone(0, "root");
            _eyeLeft = new AnimatedBone(1, "eye_left");
            _eyeRight = new AnimatedBone(2, "eye_right");
            _sourceRoot.Children.Add(_eyeLeft);
            _sourceRoot.Children.Add(_eyeRight);
            foreach (var bone in new[] { _sourceRoot, _eyeLeft, _eyeRight })
                bone.IsUsedByCurrentModel.Value = true;

            _targetRoot = new AnimatedBone(0, "root");
            _head = new AnimatedBone(1, "head");
            _targetRoot.Children.Add(_head);
            foreach (var bone in new[] { _targetRoot, _head })     // as CreateFromSkeleton marks a target skeleton's bones
                bone.IsUsedByCurrentModel.Value = true;

            _viewModel = new BoneMappingViewModel(new LocalizationManager());
            _viewModel.Initialize(new RemappedAnimatedBoneConfiguration
            {
                MeshSkeletonName = "source",
                MeshBones = [_sourceRoot],
                ParnetModelSkeletonName = "target",
                ParentModelBones = [_targetRoot],
            });

            // The tree's item templates take their converter by StaticResource, which a template resolves against the app's
            // resources when the view is parsed, before it has a window.
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources["BoolToColour_white_gray"] = new BoolToColourConverter();
            _view = new BoneMappingView { DataContext = _viewModel };
            _window = new Window { Content = _view, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000, Width = 800, Height = 600 };
            _window.Show();
            _window.UpdateLayout();
        }

        [TearDown]
        public void Close() => _window.Close();

        [Test]
        public void ClearButtons_EmptyTheirOwnFilter()
        {
            _viewModel.MeshBones.Filter = "eye";
            _viewModel.ParentModelBones.Filter = "head";

            Click("ClearMeshBonesFilterButton");
            Assert.That(_viewModel.MeshBones.Filter, Is.Empty);
            Assert.That(_viewModel.ParentModelBones.Filter, Is.EqualTo("head"));

            Click("ClearParentModelBonesFilterButton");
            Assert.That(_viewModel.ParentModelBones.Filter, Is.Empty);
        }

        // Both eyes onto head: the second click lands on the target the first one left selected.
        [Test]
        public void ClickOnTheSelectedTarget_MapsTheNextMeshBoneToIt()
        {
            _viewModel.MeshBones.SelectedItem = _eyeLeft;
            var head = TargetItem(_head);
            head.IsSelected = true;
            Assert.That(_eyeLeft.MappedBoneName.Value, Is.EqualTo("head"));

            _viewModel.MeshBones.SelectedItem = _eyeRight;
            PressOn(FindChild<TextBlock>(head)!);

            Assert.That(_eyeRight.MappedBoneName.Value, Is.EqualTo("head"));
            Assert.That(_eyeRight.MappedBoneIndex.Value, Is.EqualTo(1));
        }

        [Test]
        public void ClickOnTheSelectedTargetsExpander_MapsNothing()
        {
            _viewModel.MeshBones.SelectedItem = _eyeLeft;
            var root = TargetItem(_targetRoot);
            root.IsSelected = true;

            _viewModel.MeshBones.SelectedItem = _eyeRight;
            PressOn(FindChild<ToggleButton>(root)!);

            Assert.That(_eyeRight.MappedBoneIndex.Value, Is.EqualTo(-1));
        }

        TreeViewItem TargetItem(AnimatedBone bone)
        {
            var tree = (TreeView)_view.FindName("TargetBoneTree");
            var root = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(_targetRoot);
            if (bone == _targetRoot)
                return root;
            root.UpdateLayout();
            return (TreeViewItem)root.ItemContainerGenerator.ContainerFromItem(bone);
        }

        // PreviewMouseLeftButtonDown is a direct event that WPF raises along the route of a PreviewMouseDown, so the press
        // starts there, as real input does.
        static void PressOn(UIElement element) => element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });

        static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                    return match;
                if (FindChild<T>(child) is T deeper)
                    return deeper;
            }
            return null;
        }

        void Click(string buttonName)
        {
            var button = (Button)_view.FindName(buttonName);
            Assert.That(button.IsEnabled, Is.True, buttonName);
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
    }
}
