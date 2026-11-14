using Editors.KitbasherEditor.ChildEditors.ReRiggingTool;
using GameWorld.Core.SceneNodes;
using Moq;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.Ui.Editors.BoneMapping;

namespace Test.KitbashEditor.ReRigging
{
    // An unmapped bone keeps its old index, which on the new skeleton is an unrelated bone or past its end, so OK is
    // refused while any bone the mesh uses has no target. A bone no vertex uses never blocks it.
    public class ReRiggingViewModelTests
    {
        AnimatedBone _root = null!, _eye = null!, _skirt = null!, _targetHead = null!;
        ReRiggingViewModel _viewModel = null!;

        [SetUp]
        public void Create()
        {
            _root = new AnimatedBone(0, "root") { IsUsedByCurrentModel = { Value = true } };
            _eye = new AnimatedBone(1, "eye_left") { IsUsedByCurrentModel = { Value = true } };
            _skirt = new AnimatedBone(2, "skirt_left_0");
            _root.Children.Add(_eye);
            _root.Children.Add(_skirt);

            var targetRoot = new AnimatedBone(0, "root");
            _targetHead = new AnimatedBone(1, "head");
            targetRoot.Children.Add(_targetHead);

            // Strict: refusing OK must not reach the command that remaps the meshes.
            _viewModel = new ReRiggingViewModel(new Mock<IUiCommandFactory>(MockBehavior.Strict).Object, new LocalizationManager());
            _viewModel.Initialize(new List<Rmv2MeshNode>(), new RemappedAnimatedBoneConfiguration
            {
                MeshSkeletonName = "source",
                MeshBones = [_root],
                ParnetModelSkeletonName = "target",
                ParentModelBones = [targetRoot],
            });
        }

        [Test]
        public void TheUsedBonesWithNoTarget_AreListed_AndOkIsRefused()
        {
            Assert.That(_viewModel.UnmappedUsedBones, Is.EqualTo(new[] { "root [0]", "eye_left [1]" }));
            Assert.That(_viewModel.UnmappedUsedBonesText.Value, Does.Contain("(2)").And.Contain("eye_left [1]"));
            Assert.That(_viewModel.IsMappingComplete.Value, Is.False);
            Assert.That(_viewModel.OnOkButton(), Is.False);

            _viewModel.MeshBones.SelectedItem = _root;
            _viewModel.AutoMapSelfAndChildrenByName();
            Assert.That(_viewModel.UnmappedUsedBones, Is.EqualTo(new[] { "eye_left [1]" }));
            Assert.That(_viewModel.OnOkButton(), Is.False);
        }

        [Test]
        public void OnceEveryUsedBoneHasATarget_TheMappingIsComplete_WithTheUnusedOnesLeftOver()
        {
            _viewModel.MeshBones.SelectedItem = _root;
            _viewModel.AutoMapSelfAndChildrenByName();
            _viewModel.MeshBones.SelectedItem = _eye;
            _viewModel.MapSelectedMeshBoneTo(_targetHead);

            Assert.That(_skirt.MappedBoneIndex.Value, Is.EqualTo(-1));
            Assert.That(_viewModel.UnmappedUsedBones, Is.Empty);
            Assert.That(_viewModel.IsMappingComplete.Value, Is.True);
            Assert.That(_viewModel.Validate(out var error), Is.True, error);
        }

        // The end-to-end tests map by writing to the bones directly, so Validate cannot trust the list from the last mapping change.
        [Test]
        public void Validate_ReadsTheBonesAgain()
        {
            _root.MappedBoneIndex.Value = 0;
            _eye.MappedBoneIndex.Value = 1;
            Assert.That(_viewModel.Validate(out var error), Is.True, error);
            Assert.That(_viewModel.UnmappedUsedBones, Is.Empty);
        }
    }
}
