using System.IO;
using Editors.AnimationMeta.SuperView.Visualisation;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.DB;
using Shared.GameFormats.RigidModel.Transforms;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // SPLICE tags in SuperView: the named animation drives the bones its weights reach.
    public class SpliceTests
    {
        record Provider(GameSkeleton Skeleton) : ISkeletonProvider;

        static readonly (string Name, int Parent)[] s_bones =
            [("root", -1), ("spine", 0), ("clav_left", 1), ("upperleg_left", 0), ("neck", 1), ("head", 4), ("clav_right", 1), ("be_prop_0", 0)];
        // Bones 4 to 6 are the neck, the head and the right arm.
        const int Root = 0, Spine = 1, Arm = 2, Leg = 3, Prop = 7;
        static readonly Quaternion Bent = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1f);

        // The skeleton's battle_skeletons row, as vanilla's humanoid01 names its nodes.
        static readonly DbRow s_row = new(new Dictionary<string, object>
        {
            ["name"] = "test_skeleton", ["neck_node"] = "neck", ["spine_node"] = "spine", ["leftshoulder_node"] = "clav_left",
            ["rightshoulder_node"] = "clav_right", ["leftupleg_node"] = "upperleg_left", ["weapon1_node"] = "be_prop_0",
        }, 17, "test", "db/battle_skeletons_tables/test");

        GameSkeleton _skeleton = null!;
        AnimationClip _main = null!;
        AnimationPlayer _player = null!;
        MetaDataBuilder _builder = null!;

        [TearDown]
        public void TearDown() => _builder.Dispose();

        [SetUp]
        public void Setup()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            _player = new AnimationPlayer();
            _skeleton = Skeleton(_player);
            Play(Quaternion.Identity);

            // The spliced animation bends every bone.
            var pack = runner.CreateOutputPack();
            runner.PackFileService.AddFilesToPack(pack,
                [new NewPackFileEntry("animations/test", PackFile.CreateFromBytes("bend.anim", AnimationFile.ConvertToBytes(Clip(Bent).ConvertToFileFormat(_skeleton)))),
                 new NewPackFileEntry("animations/test", PackFile.CreateFromBytes("lift.anim", AnimationFile.ConvertToBytes(Clip(Quaternion.Identity, 1).ConvertToFileFormat(_skeleton))))]);
            _builder = new MetaDataBuilder(null!, null!, null!, runner.PackFileService, null!) { SkeletonRow = _ => s_row };
        }

        void Play(Quaternion rotation, float height = 0.5f)
        {
            _main = Clip(rotation, height);
            _player.SetAnimation(_main, _skeleton);
        }

        static Splice_v12 Tag(float start = 0.5f, float end = 1, int bone = Spine, float weight = 1) => new()
        {
            Animation = "animations/test/bend.anim", GenericBoneIndex = bone, GenericWeight = weight, StartTime = start, EndTime = end,
        };

        static Splice_v12 Whole(Action<Splice_v12> set)
        {
            var tag = Tag(0, 0, weight: 0);
            set(tag);
            return tag;
        }

        AnimationFrame PoseAt(float time, params ParsedMetadataAttribute[] tags)
        {
            _player.AnimationRules.Clear();
            _builder.Create(null, new ParsedMetadataFile { Version = 2, Attributes = [.. tags] }, null, new GroupNode("root"), new Provider(_skeleton), _player, null!);
            return AnimationSampler.Sample(0, 0, _skeleton, _main, _player.AnimationRules, timeInSec: time);
        }

        static float Angle(AnimationFrame frame, int bone) => 2 * MathF.Acos(Math.Min(1, Math.Abs(frame.BoneTransforms[bone].Rotation.W)));

        float[] Angles(params ParsedMetadataAttribute[] tags)
        {
            var frame = PoseAt(0.5f, tags);
            return [.. Enumerable.Range(0, s_bones.Length).Select(x => MathF.Round(Angle(frame, x), 3))];
        }

        [Test]
        public void Splice_DrivesItsBoneAndTheBonesUnderIt_InsideItsWindow()
        {
            var inside = PoseAt(0.75f, Tag());
            Assert.That(Angle(inside, Spine), Is.EqualTo(1).Within(1e-3f), "the generic bone");
            Assert.That(Angle(inside, Arm), Is.EqualTo(1).Within(1e-3f), "a bone under it");
            Assert.That(Angle(inside, Leg), Is.EqualTo(0).Within(1e-3f), "a bone beside it");
            Assert.That(Angle(inside, Root), Is.EqualTo(0).Within(1e-3f), "its parent");

            Assert.That(Angle(PoseAt(0.4f, Tag()), Spine), Is.EqualTo(0).Within(1e-3f), "before the window");
            Assert.That(Angle(PoseAt(1.2f, Tag()), Spine), Is.EqualTo(0).Within(1e-3f), "after the window");
        }

        [Test]
        public void Splice_BlendsByItsGenericWeight()
        {
            Assert.That(Angle(PoseAt(0.75f, Tag(weight: 0.5f)), Spine), Is.EqualTo(0.5f).Within(1e-3f));
        }

        [TestCase(0.35f, 0.25f, TestName = "Splice_FadesInOverItsBlendInBeforeItsStart")]
        [TestCase(1.1f, 0.75f, TestName = "Splice_FadesOutOverItsBlendOutAfterItsEnd")]
        public void Splice_FadesOverItsBlendTimes(float time, float strength)
        {
            var tag = Tag();
            (tag.BlendInTime, tag.BlendOutTime) = (0.2f, 0.4f);
            Assert.That(Angle(PoseAt(time, tag), Spine), Is.EqualTo(strength).Within(1e-3f));
        }

        // Neck, arms and legs are the row's nodes and everything under them; the torso is the spine's less those.
        [Test]
        public void BodyPartWeights_DriveTheBonesTheSkeletonsRowNames()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Angles(Whole(x => x.LeftArmWeight = 1)), Is.EqualTo(new[] { 0f, 0, 1, 0, 0, 0, 0, 0 }), "left arm");
                Assert.That(Angles(Whole(x => x.RightArmWeight = 1)), Is.EqualTo(new[] { 0f, 0, 0, 0, 0, 0, 1, 0 }), "right arm");
                Assert.That(Angles(Whole(x => x.NeckWeight = 0.5f)), Is.EqualTo(new[] { 0f, 0, 0, 0, 0.5f, 0.5f, 0, 0 }), "neck and head");
                Assert.That(Angles(Whole(x => x.TorsoWeight = 1)), Is.EqualTo(new[] { 0f, 1, 0, 0, 0, 0, 0, 0 }), "torso");
                Assert.That(Angles(Whole(x => x.LegsWeight = 1)), Is.EqualTo(new[] { 0f, 0, 0, 1, 0, 0, 0, 0 }), "legs");
                Assert.That(Angles(Whole(x => (x.GenericBoneIndex, x.GenericWeight, x.LeftArmWeight) = (Spine, 0.5f, 1))),
                    Is.EqualTo(new[] { 0f, 0.5f, 1, 0, 0.5f, 0.5f, 0.5f, 0 }), "the stronger of the generic bone and the arm");
            });
        }

        // Depth counts levels from the generic bone itself; 0 and -1 reach every bone under it.
        [TestCase(1, new[] { 1f, 0, 0, 0, 0, 0, 0, 0 })]
        [TestCase(2, new[] { 1f, 1, 0, 1, 0, 0, 0, 1 })]
        [TestCase(3, new[] { 1f, 1, 1, 1, 1, 0, 1, 1 })]
        [TestCase(0, new[] { 1f, 1, 1, 1, 1, 1, 1, 1 })]
        [TestCase(-1, new[] { 1f, 1, 1, 1, 1, 1, 1, 1 })]
        public void GenericBoneDepth_LimitsHowFarDownTheSpliceReaches(int depth, float[] angles)
        {
            Assert.That(Angles(Whole(x => (x.GenericBoneIndex, x.GenericWeight, x.GenericBoneDepth) = (Root, 1, depth))), Is.EqualTo(angles));
        }

        [Test]
        public void GenericBoneMinusOne_WithAWeightAndNoBodyPart_DrivesTheWholeSkeleton()
        {
            Assert.That(Angles(Whole(x => (x.GenericBoneIndex, x.GenericWeight) = (-1, 1))), Is.EqualTo(new[] { 1f, 1, 1, 1, 1, 1, 1, 1 }));
            Assert.That(Angles(Whole(x => (x.GenericBoneIndex, x.GenericWeight, x.LeftArmWeight) = (-1, 1, 1))), Is.EqualTo(new[] { 0f, 0, 1, 0, 0, 0, 0, 0 }), "with a body part");
        }

        [Test]
        public void WeaponPose_DrivesTheWeaponBonesFully()
        {
            Assert.That(Angle(PoseAt(0.75f, Tag(weight: 0.5f)), Prop), Is.EqualTo(0).Within(1e-3f), "without it");
            var tag = Tag(weight: 0.5f);
            tag.WeaponPose = "true";
            Assert.That(Angle(PoseAt(0.75f, tag), Prop), Is.EqualTo(1).Within(1e-3f), "with it");

            _builder.SkeletonRow = _ => null;
            Assert.That(Angle(PoseAt(0.75f, tag), Prop), Is.EqualTo(1).Within(1e-3f), "a be_prop bone, with no row");
        }

        [Test]
        public void BodyPartWeights_WithNoRowForTheSkeleton_AreNotDrawn_AndTheGenericBoneStillIs()
        {
            _builder.SkeletonRow = _ => null;
            Assert.That(Angles(Whole(x => (x.GenericBoneIndex, x.GenericWeight, x.LeftArmWeight) = (Leg, 1, 1))), Is.EqualTo(new[] { 0f, 0, 0, 1, 0, 0, 0, 0 }));
        }

        // The builder's own reader, against the game's db.pack from WH3_DATA; skipped without it.
        [Test]
        public void TheRowReader_FindsTheSkeletonsRowInTheGamesDb()
        {
            var data = Environment.GetEnvironmentVariable("WH3_DATA");
            Assume.That(data != null && Directory.Exists(data), "set WH3_DATA to the game's data folder");
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var loader = (IPackFileContainerLoader)runner.ServiceProvider.GetService(typeof(IPackFileContainerLoader))!;
            runner.PackFileService.AddContainer(loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(data!, "db.pack"), true));
            using var builder = new MetaDataBuilder(null!, null!, null!, runner.PackFileService, null!);

            Assert.Multiple(() =>
            {
                Assert.That(builder.SkeletonRow("HUMANOID01")?.Text("neck_node"), Is.EqualTo("neck_0"));
                Assert.That(builder.SkeletonRow("humanoid01")?.Text("leftshoulder_node"), Is.EqualTo("clav_left"));
                Assert.That(builder.SkeletonRow("centaur01")?.Text("torso_splice_node"), Is.EqualTo("spine_3"));
                Assert.That(builder.SkeletonRow("no_such_skeleton"), Is.Null);
            });
        }

        // An additive splice adds its turn from the bind pose (identity here) to the main animation's own turn.
        [Test]
        public void AnAdditiveSplice_AddsItsDifferenceFromTheBindPose()
        {
            Play(Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f));
            var tag = Tag();
            Assert.That(Angle(PoseAt(0.75f, tag), Spine), Is.EqualTo(1).Within(1e-3f), "not additive");
            tag.Additive = "true";
            Assert.That(Angle(PoseAt(0.75f, tag), Spine), Is.EqualTo(1.5f).Within(1e-3f), "additive");
        }

        // lift.anim raises each bone to 1 from the bind pose's 0.5, and the main animation holds them at 0.7.
        [Test]
        public void AnAdditiveSplice_AddsItsMoveFromTheBindPose()
        {
            Play(Quaternion.Identity, 0.7f);
            var tag = Tag();
            (tag.Animation, tag.Additive) = ("animations/test/lift.anim", "true");
            Assert.That(PoseAt(0.75f, tag).BoneTransforms[Spine].Translation.Y, Is.EqualTo(1.2f).Within(1e-4f));
        }

        // Its reference here is the spliced animation itself, so it adds nothing.
        [Test]
        public void AnAdditiveSplice_WithAReferenceAnimation_AddsItsDifferenceFromThatInstead()
        {
            Play(Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f));
            var tag = Tag();
            (tag.Additive, tag.ReferenceAnimation) = ("true", "animations/test/bend.anim");
            Assert.That(Angle(PoseAt(0.75f, tag), Spine), Is.EqualTo(0.5f).Within(1e-3f));

            tag.ReferenceAnimation = "animations/test/missing.anim";
            Assert.That(Angle(PoseAt(0.75f, tag), Spine), Is.EqualTo(0.5f).Within(1e-3f), "a missing reference draws nothing");
        }

        // A splice sets its bones afresh, so a TRANSFORM on one of them has to come after it.
        [Test]
        public void ATransform_TurnsABoneASpliceDrives()
        {
            var transform = new Transform_v10 { TargetNode = Spine, Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f).ToVector4() };
            var frame = PoseAt(0.75f, transform, Whole(x => (x.GenericBoneIndex, x.GenericWeight) = (Spine, 1)));
            // TRANSFORM turns the bone's matrix, not its rotation; the bind pose turns no bone, so the matrix's turn is the spine's.
            frame.BoneTransforms[Spine].WorldTransform.Decompose(out _, out var turn, out _);
            Assert.That(2 * MathF.Acos(Math.Min(1, Math.Abs(turn.W))), Is.EqualTo(1.5f).Within(1e-3f));
        }

        static GameSkeleton Skeleton(AnimationPlayer player)
        {
            var file = new AnimationFile();
            file.Header.SkeletonName = "test_skeleton";
            file.Bones = s_bones.Select((b, i) => new AnimationFile.BoneInfo { Id = i, Name = b.Name, ParentId = b.Parent }).ToArray();
            var frame = new AnimationFile.Frame();
            foreach (var _ in s_bones)
            {
                frame.Transforms.Add(new RmvVector3(0, 0.5f, 0));
                frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            }
            var part = new AnimationFile.AnimationPart();
            part.DynamicFrames.Add(frame);
            file.AnimationParts.Add(part);
            return new GameSkeleton(file, player);
        }

        static AnimationClip Clip(Quaternion rotation, float height = 0.5f)
        {
            var clip = new AnimationClip();
            for (var i = 0; i < 2; i++)
                clip.DynamicFrames.Add(new AnimationClip.KeyFrame
                {
                    Position = [.. Enumerable.Repeat(new Vector3(0, height, 0), s_bones.Length)],
                    Rotation = [.. Enumerable.Repeat(rotation, s_bones.Length)],
                    Scale = [.. Enumerable.Repeat(Vector3.One, s_bones.Length)],
                });
            clip.PlayTimeInSec = 2;
            return clip;
        }
    }
}
