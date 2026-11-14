using System.Reflection;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.Rendering.RenderItems;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.RigidModel.Transforms;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // Tags SuperView draws on its asset, built the way SuperView builds them: on its SceneObject.
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

        static Alpha_v10 AlphaKey(float at, float alpha) => new() { StartTime = at, EndTime = at, DesiredAlpha = alpha };

        // Vanilla's deaths key 1 then 0 a moment later: the model is gone from the 0 key on.
        [TestCase(0.4f, true)]
        [TestCase(0.7f, true)]
        [TestCase(0.9f, false)]
        public void Alpha_AZeroKeyAfterAOneKey_HidesTheModelFromIt(float time, bool shown)
        {
            var instances = Build(null, Meta(AlphaKey(0.8f, 0), AlphaKey(0.5f, 1)));
            Assert.That(ModelShownAt(instances, time), Is.EqualTo(shown));
        }

        // A resurrection keys 0, 0, then 1. Between the two zeros the model is gone whichever way the game reads keys; after
        // the second it is fading in, or still gone, and the preview cannot tell, so it is drawn.
        [TestCase(0.3f, false)]
        [TestCase(0.6f, true)]
        [TestCase(0.9f, true)]
        public void Alpha_IsHiddenOnlyWhereItIsZeroUnderEitherReading(float time, bool shown)
        {
            var instances = Build(null, Meta(AlphaKey(0.2f, 0), AlphaKey(0.4f, 0), AlphaKey(0.8f, 1)));
            Assert.That(ModelShownAt(instances, time), Is.EqualTo(shown));
        }

        [Test]
        public void Alpha_AndDisableModel_BothHideTheModel()
        {
            var instances = Build(null, Meta(new DisableModel { StartTime = 0.1f, EndTime = 0.3f }, AlphaKey(0.8f, 0)));
            Assert.That(ModelShownAt(instances, 0.2f), Is.False, "the DISABLE_MODEL window");
            Assert.That(ModelShownAt(instances, 0.5f), Is.True, "between them");
            Assert.That(ModelShownAt(instances, 0.9f), Is.False, "after the zero key");
        }

        float ScaleAt(List<IMetaDataInstance> instances, float time)
        {
            foreach (var instance in instances)
                instance.Update(time);
            return Vector3.Transform(Vector3.UnitX, _scene.MainNode.ModelMatrix).X;
        }

        [Test]
        public void Rescale_DrawsTheWholeAssetAtItsScale_WhileTheTagIsOn()
        {
            var instances = Build(null, Meta(new ReScale { StartTime = 0.5f, EndTime = 1, Scale = 1.75f }));

            Assert.That(ScaleAt(instances, 0.4f), Is.EqualTo(1).Within(1e-5f), "before the tag");
            Assert.That(ScaleAt(instances, 0.75f), Is.EqualTo(1.75f).Within(1e-5f), "inside the tag");
            Assert.That(ScaleAt(instances, 1.2f), Is.EqualTo(1).Within(1e-5f), "after the tag");
        }

        // The scale is the target, not a factor: 909 of vanilla's 970 RESCALE tags say 1, which as a factor would do nothing.
        [Test]
        public void Rescale_IsTheScaleItself_NotAFactorOnTheLastFrame()
        {
            var instances = Build(null, Meta(new ReScale { StartTime = 0, EndTime = 0, Scale = 0.43f }));
            ScaleAt(instances, 0.5f);
            Assert.That(ScaleAt(instances, 0.6f), Is.EqualTo(0.43f).Within(1e-5f));

            foreach (var instance in instances)
                instance.CleanUp();
            Assert.That(_scene.MainNode.ModelMatrix, Is.EqualTo(_scene.Offset), "removing the tag puts the asset back");
        }

        static List<Vector3> Lines(SimpleDrawableNode node) =>
            [.. ((List<VertexPositionColor>)typeof(SimpleDrawableNode).GetField("_lineVertexList", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(node)!).Select(x => x.Position)];

        SimpleDrawableNode Drawn(string name) => _scene.MainNode.Children.OfType<SimpleDrawableNode>().Single(x => x.Name == name);

        static Vector4 Yaw90 => new(0, MathF.Sin(MathHelper.PiOver4), 0, MathF.Cos(MathHelper.PiOver4));

        [Test]
        public void CrewLocation_IsALocatorAtItsPosition_TurnedByItsOrientation_NamedByItsFilter()
        {
            Build(null, Meta(new CrewLocation_v10 { Filter = "crew2", Position = new Vector3(1, 0, -2), Orientation = Yaw90 }));

            // The X axis, turned 90 degrees about Y, points along -Z.
            var lines = Lines(Drawn("Crew:crew2"));
            Assert.That(lines, Has.Some.Matches<Vector3>(p => Vector3.Distance(p, new Vector3(1, 0, -2.3f)) < 1e-4f), "X axis end");
            Assert.That(lines, Has.Some.Matches<Vector3>(p => Vector3.Distance(p, new Vector3(1, 0.3f, -2)) < 1e-4f), "Y axis end");
        }

        [Test]
        public void CameraShakePos_IsALocatorAtItsPosition()
        {
            var at = new Vector3(1.5f, 0, 10.7f);
            Build(null, Meta(new CameraShakePos { StartTime = 0.4f, EndTime = 0.4f, Position = at }));
            Assert.That(Lines(Drawn("CameraShake")), Has.All.Matches<Vector3>(p => Math.Abs(Vector3.Distance(p, at) - 0.3f) < 1e-4f), "a circle around the position");
        }

        static List<string> Labels(SimpleDrawableNode node) =>
            [.. ((List<WorldTextRenderItem>)typeof(SimpleDrawableNode).GetField("_renderList", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(node)!)
                .Select(x => (string)typeof(WorldTextRenderItem).GetField("_text", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(x)!)];

        bool FiringAt(List<IMetaDataInstance> instances, string marker, float time)
        {
            foreach (var instance in instances)
                instance.Update(time);
            return Drawn(marker).Children.Single().IsVisible;
        }

        // FIRE_POS is an instant, the projectile's spawn: its marker flashes from then for a quarter second.
        [TestCase(0.45f, false)]
        [TestCase(0.5f, true)]
        [TestCase(0.7f, true)]
        [TestCase(0.8f, false)]
        public void FirePos_FlashesFromItsStartTime(float time, bool firing)
        {
            var instances = Build(null, Meta(new FirePos_v10 { StartTime = 0.5f, EndTime = 0.5f, Position = new Vector3(0, 2, 1) }));
            Assert.That(FiringAt(instances, "FirePos", time), Is.EqualTo(firing));
        }

        [Test]
        public void FirePos_TheFlashIsARingRoundItsPosition_AndTheLabelSaysWhen()
        {
            var at = new Vector3(0, 2, 1);
            Build(null, Meta(new FirePos_v10 { StartTime = 0.5f, EndTime = 0.5f, Position = at }));

            var flash = (SimpleDrawableNode)Drawn("FirePos").Children.Single();
            Assert.That(Lines(flash), Has.All.Matches<Vector3>(p => Vector3.Distance(p, at) > 0.5f && Vector3.Distance(p, at) < 0.7f), "wider than the marker's 0.3");
            Assert.That(Labels(Drawn("FirePos")), Is.EqualTo(new[] { "FirePos @0.50s" }));
        }

        // A tag with a window longer than the flash fires for the whole window.
        [TestCase(0.9f, true)]
        [TestCase(1.1f, false)]
        public void SplashAttack_WithAWindow_FiresForAllOfIt(float time, bool firing)
        {
            var tag = new SplashAttack_v10 { StartTime = 0.2f, EndTime = 1, StartPosition = Vector3.Zero, EndPosition = new Vector3(0, 0, 2) };
            var instances = Build(null, Meta(tag));
            Assert.That(FiringAt(instances, "SplashAttack_1", time), Is.EqualTo(firing));
            Assert.That(Labels(Drawn("SplashAttack_1")), Does.Contain("SplashAttack 0.20-1.00s"));
        }

        [Test]
        public void ImpactPos_TargetPos_AndCameraShake_FlashToo()
        {
            var instances = Build(null, Meta(
                new ImpactPosition_v10 { StartTime = 1, EndTime = 1 },
                new TargetPos_10 { StartTime = 1, EndTime = 1 },
                new CameraShakePos { StartTime = 1, EndTime = 1 }));
            foreach (var marker in new[] { "ImpactPos", "TargetPos", "CameraShake" })
            {
                Assert.That(FiringAt(instances, marker, 0.9f), Is.False, marker);
                Assert.That(FiringAt(instances, marker, 1.1f), Is.True, marker);
            }
        }

        [Test]
        public void CleanUp_TakesTheFlashWithTheMarker()
        {
            var instances = Build(null, Meta(new FirePos_v10 { StartTime = 0.5f, EndTime = 0.5f }));
            foreach (var instance in instances)
                instance.CleanUp();
            Assert.That(_scene.MainNode.Children.Select(x => x.Name), Is.EquivalentTo(new[] { "model" }));
        }

        [Test]
        public void CrewLocation_Version2_FromThePersistentMeta_IsDrawnToo()
        {
            Build(Meta(new CrewLocation_v2 { Filter = "crew1", Position = new Vector3(0, 0, -4) }), Meta());
            Assert.That(Lines(Drawn("Crew:crew1")), Has.Some.Matches<Vector3>(p => Vector3.Distance(p, new Vector3(0.3f, 0, -4)) < 1e-4f));
        }
    }
}
