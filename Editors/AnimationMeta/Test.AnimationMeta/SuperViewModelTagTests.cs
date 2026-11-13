using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.RigidModel.Transforms;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // Tags that change the main model itself in SuperView, built the way SuperView builds them: on its SceneObject.
    public class SuperViewModelTagTests
    {
        AssetEditorTestRunner _runner = null!;
        MetaDataBuilder _builder = null!;
        SceneObject _scene = null!;

        [SetUp]
        public void Setup()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _runner.CreateOutputPack();
            _builder = new MetaDataBuilder(null!, null!, null!, _runner.PackFileService, null!);

            var player = new AnimationPlayer();
            var skeleton = new GameSkeleton(SkeletonFile(), player);
            player.SetAnimation(new AnimationClip { PlayTimeInSec = 2, DynamicFrames = [Frame(), Frame()] }, skeleton);
            player.IsEnabled = true;
            var root = new GroupNode("root");
            var model = root.AddObject(new GroupNode("model"));
            _scene = new SceneObject("asset") { ParentNode = root, ModelNode = model, Skeleton = skeleton, Player = player };
        }

        [TearDown]
        public void TearDown() => _builder.Dispose();

        static AnimationClip.KeyFrame Frame() => new() { Position = [Vector3.Zero], Rotation = [Quaternion.Identity], Scale = [Vector3.One] };

        static AnimationFile SkeletonFile()
        {
            var file = new AnimationFile();
            file.Header.SkeletonName = "test_skeleton";
            file.Bones = [new AnimationFile.BoneInfo { Id = 0, Name = "root", ParentId = AnimationFile.BoneIndexNoParent }];
            var bind = new AnimationFile.Frame();
            bind.Transforms.Add(new RmvVector3(0, 0, 0));
            bind.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            var part = new AnimationFile.AnimationPart();
            part.DynamicFrames.Add(bind);
            file.AnimationParts.Add(part);
            return file;
        }

        static ParsedMetadataFile Meta(params ParsedMetadataAttribute[] tags) => new() { Version = 2, Attributes = [.. tags] };

        List<IMetaDataInstance> Build(ParsedMetadataFile? persistent, ParsedMetadataFile meta) =>
            _builder.Create(persistent, meta, null, _scene.MainNode, _scene, _scene.Player, null!);

        bool ModelShownAt(List<IMetaDataInstance> instances, float time)
        {
            foreach (var instance in instances)
                instance.Update(time);
            return _scene.ModelNode.IsVisible;
        }

        [Test]
        public void DisableModel_HidesTheModelBetweenStartAndEnd()
        {
            var instances = Build(null, Meta(new DisableModel { StartTime = 0.5f, EndTime = 1 }));

            Assert.That(ModelShownAt(instances, 0.4f), Is.True, "before the tag");
            Assert.That(ModelShownAt(instances, 0.75f), Is.False, "inside the tag");
            Assert.That(ModelShownAt(instances, 1.2f), Is.True, "after the tag");
        }

        // Vanilla's instants sit where a matched-combat victim is swallowed, and 0/0 is on the invisible engine stands.
        [TestCase(0.5f, 0.4f, true)]
        [TestCase(0.5f, 1.9f, false)]
        [TestCase(0f, 0f, false)]
        [TestCase(0f, 1.9f, false)]
        public void DisableModel_AnInstant_HidesTheModelFromThenToTheEnd(float at, float time, bool shown)
        {
            var instances = Build(null, Meta(new DisableModel { StartTime = at, EndTime = at }));
            Assert.That(ModelShownAt(instances, time), Is.EqualTo(shown));
        }

        [Test]
        public void DisableModel_FromThePersistentMeta_HidesTheModelToo()
        {
            var instances = Build(Meta(new DisableModel { StartTime = 0.5f, EndTime = 1 }), Meta());
            Assert.That(ModelShownAt(instances, 0.75f), Is.False);
        }

        [Test]
        public void DisableModel_KeepsAModelTheUserHid_AndCleanUpGivesBackTheirChoice()
        {
            var instances = Build(null, Meta(new DisableModel { StartTime = 0.5f, EndTime = 1 }));

            _scene.ShowMesh.Value = false;
            Assert.That(ModelShownAt(instances, 0.2f), Is.False, "hidden by the user outside the tag");

            _scene.ShowMesh.Value = true;
            ModelShownAt(instances, 0.75f);
            foreach (var instance in instances)
                instance.CleanUp();
            Assert.That(_scene.ModelNode.IsVisible, Is.True, "shown again once the tag is gone");
        }
    }
}
