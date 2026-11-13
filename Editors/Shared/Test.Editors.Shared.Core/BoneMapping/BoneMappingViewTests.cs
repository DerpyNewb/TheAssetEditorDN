using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

        void Click(string buttonName)
        {
            var button = (Button)_view.FindName(buttonName);
            Assert.That(button.IsEnabled, Is.True, buttonName);
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
    }
}
