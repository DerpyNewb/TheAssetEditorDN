using Editors.AnimationMeta.SuperView.Visualisation;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.RigidModel.Transforms;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // SPLICE tags in SuperView: the named animation drives the generic bone and every bone under it.
    public class SpliceTests
    {
        record Provider(GameSkeleton Skeleton) : ISkeletonProvider;

        // root, spine under it, an arm under the spine, and a leg under the root.
        const int Root = 0, Spine = 1, Arm = 2, Leg = 3;
        static readonly Quaternion Bent = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1f);

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
            _main = Clip(Quaternion.Identity);
            _player.SetAnimation(_main, _skeleton);

            // The spliced animation bends every bone.
            var pack = runner.CreateOutputPack();
            runner.PackFileService.AddFilesToPack(pack,
                [new NewPackFileEntry("animations/test", PackFile.CreateFromBytes("bend.anim", AnimationFile.ConvertToBytes(Clip(Bent).ConvertToFileFormat(_skeleton))))]);
            _builder = new MetaDataBuilder(null!, null!, null!, runner.PackFileService, null!);
        }

        static Splice_v12 Tag(float start = 0.5f, float end = 1, float weight = 1, string additive = "false", float leftArm = 0) => new()
        {
            Animation = "animations/test/bend.anim", GenericBoneIndex = Spine, GenericWeight = weight,
            StartTime = start, EndTime = end, Additive = additive, LeftArmWeight = leftArm,
        };

        AnimationFrame PoseAt(float time, params ParsedMetadataAttribute[] tags)
        {
            _player.AnimationRules.Clear();
            _builder.Create(null, new ParsedMetadataFile { Version = 2, Attributes = [.. tags] }, null, new GroupNode("root"), new Provider(_skeleton), _player, null!);
            return AnimationSampler.Sample(0, 0, _skeleton, _main, _player.AnimationRules, timeInSec: time);
        }

        static float Angle(AnimationFrame frame, int bone) => 2 * MathF.Acos(Math.Min(1, Math.Abs(frame.BoneTransforms[bone].Rotation.W)));

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

        // Which bones the game counts as an arm, the neck, the torso or the legs is not documented, and what an additive
        // splice adds to is not known, so those are left to the animation rather than guessed.
        [Test]
        public void AnAdditiveSplice_OrOneWeightedByBodyPart_IsNotDrawn()
        {
            Assert.That(Angle(PoseAt(0.75f, Tag(additive: "true")), Spine), Is.EqualTo(0).Within(1e-3f), "additive");
            Assert.That(Angle(PoseAt(0.75f, Tag(leftArm: 0.9f)), Spine), Is.EqualTo(0).Within(1e-3f), "left arm weight");
        }

        static GameSkeleton Skeleton(AnimationPlayer player)
        {
            var bones = new (string Name, int Parent)[] { ("root", -1), ("spine", 0), ("arm", 1), ("leg", 0) };
            var file = new AnimationFile();
            file.Header.SkeletonName = "test_skeleton";
            file.Bones = bones.Select((b, i) => new AnimationFile.BoneInfo { Id = i, Name = b.Name, ParentId = b.Parent }).ToArray();
            var frame = new AnimationFile.Frame();
            foreach (var _ in bones)
            {
                frame.Transforms.Add(new RmvVector3(0, 0.5f, 0));
                frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            }
            var part = new AnimationFile.AnimationPart();
            part.DynamicFrames.Add(frame);
            file.AnimationParts.Add(part);
            return new GameSkeleton(file, player);
        }

        static AnimationClip Clip(Quaternion rotation)
        {
            var clip = new AnimationClip();
            for (var i = 0; i < 2; i++)
                clip.DynamicFrames.Add(new AnimationClip.KeyFrame
                {
                    Position = [.. Enumerable.Repeat(new Vector3(0, 0.5f, 0), 4)],
                    Rotation = [.. Enumerable.Repeat(rotation, 4)],
                    Scale = [.. Enumerable.Repeat(Vector3.One, 4)],
                });
            clip.PlayTimeInSec = 2;
            return clip;
        }
    }
}
