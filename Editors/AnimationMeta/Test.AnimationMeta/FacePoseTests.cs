using Editors.AnimationMeta.SuperView.Visualisation;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.RigidModel.Transforms;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // FACE_POSE keys in SuperView: the face takes its pose from the set's FACE_POSE_* slot that the key names.
    public class FacePoseTests
    {
        record Provider(GameSkeleton Skeleton) : ISkeletonProvider;

        class Fragment(List<AnimationBinEntryGenericFormat> entries) : IAnimationBinGenericFormat
        {
            public AnimationPackFileDatabase? PackFileReference => null;
            public string Name => "test";
            public string FullPath => "test";
            public string SkeletonName => "test_skeleton";
            public string MountBin => "";
            public List<AnimationBinEntryGenericFormat> Entries => entries;
        }

        // root, head under it, then the jaw and an ear under the head, and a body bone under the root.
        const int Jaw = 2, Ear = 3, Body = 4, Weapon = 5;
        static readonly Quaternion Open = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f);
        static readonly Quaternion Tilted = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.3f);

        AssetEditorTestRunner _runner = null!;
        GameSkeleton _skeleton = null!;
        AnimationClip _main = null!;
        AnimationPlayer _player = null!;
        MetaDataBuilder _builder = null!;

        [TearDown]
        public void TearDown() => _builder.Dispose();

        [SetUp]
        public void Setup()
        {
            _runner = new AssetEditorTestRunner();
            _runner.CreateCaContainer();
            _player = new AnimationPlayer();
            _skeleton = Skeleton(_player);
            _main = Clip(Quaternion.Identity, Quaternion.Identity);
            _player.SetAnimation(_main, _skeleton);
            _builder = new MetaDataBuilder(null!, null!, null!, _runner.PackFileService, null!);

            // Both poses hold the ear and the body tilted alike, as CA's face poses hold a whole skeleton; only the jaw differs.
            var pack = _runner.CreateOutputPack();
            _runner.PackFileService.AddFilesToPack(pack,
            [
                new NewPackFileEntry("animations/test", PackFile.CreateFromBytes("relax.anim", AnimationFile.ConvertToBytes(Clip(Quaternion.Identity, Tilted).ConvertToFileFormat(_skeleton)))),
                new NewPackFileEntry("animations/test", PackFile.CreateFromBytes("scream.anim", AnimationFile.ConvertToBytes(Clip(Open, Tilted).ConvertToFileFormat(_skeleton)))),
            ]);
        }

        static Fragment Set(params string[] slots) => new([.. slots.Select(x => new AnimationBinEntryGenericFormat
        {
            SlotName = x,
            AnimationFile = x == "FACE_POSE_RELAX" ? "animations/test/relax.anim" : "animations/test/scream.anim",
        })]);

        AnimationFrame PoseAt(float time, Fragment set, params ParsedMetadataAttribute[] tags)
        {
            _player.AnimationRules.Clear();
            _builder.Create(null, new ParsedMetadataFile { Version = 2, Attributes = [.. tags] }, null, new GroupNode("root"), new Provider(_skeleton), _player, set);
            return AnimationSampler.Sample(0, 0, _skeleton, _main, _player.AnimationRules, timeInSec: time);
        }

        static FacePose Key(float at, int pose) => new() { StartTime = at, EndTime = at, FacePoseId = pose, Weight = 1 };

        static bool Same(Quaternion a, Quaternion b) => Math.Abs(Quaternion.Dot(a, b)) > 0.9999f;

        [Test]
        public void Key_PosesTheBonesTheFacePosesDisagreeOn_FromItsTime()
        {
            var set = Set("FACE_POSE_RELAX", "FACE_POSE_ANGRY_SCREAM");
            var tag = Key(0.5f, 4);   // 4 = FACE_POSE_ANGRY_SCREAM

            Assert.That(Same(PoseAt(0.4f, set, tag).BoneTransforms[Jaw].Rotation, Quaternion.Identity), "jaw before the key");
            var after = PoseAt(0.6f, set, tag);
            Assert.That(Same(after.BoneTransforms[Jaw].Rotation, Open), "jaw after the key");
            Assert.That(Same(after.BoneTransforms[Ear].Rotation, Quaternion.Identity), "the ear, alike in both poses, keeps the animation");
            Assert.That(Same(after.BoneTransforms[Body].Rotation, Quaternion.Identity), "the body keeps the animation");
            Assert.That(Same(after.BoneTransforms[Weapon].Rotation, Quaternion.Identity), "a weapon bone is never the face, even where the poses disagree on it");
        }

        [Test]
        public void ReleaseKey_GivesTheFaceBackToTheAnimation()
        {
            var set = Set("FACE_POSE_RELAX", "FACE_POSE_ANGRY_SCREAM");
            Assert.That(Same(PoseAt(0.8f, set, Key(0.2f, 4), Key(0.6f, -1)).BoneTransforms[Jaw].Rotation, Quaternion.Identity));
        }

        [Test]
        public void ASetWithOneFacePose_HasNothingToCompare_AndPosesNothing()
        {
            var set = Set("FACE_POSE_ANGRY_SCREAM");
            Assert.That(Same(PoseAt(0.6f, set, Key(0.5f, 4)).BoneTransforms[Jaw].Rotation, Quaternion.Identity));
        }

        static GameSkeleton Skeleton(AnimationPlayer player)
        {
            var bones = new (string Name, int Parent)[] { ("root", -1), ("head", 0), ("jaw", 1), ("ear", 1), ("body", 0), ("be_prop_0", 0) };
            var file = new AnimationFile();
            file.Header.SkeletonName = "test_skeleton";
            file.Bones = bones.Select((b, i) => new AnimationFile.BoneInfo { Id = i, Name = b.Name, ParentId = b.Parent }).ToArray();
            var frame = new AnimationFile.Frame();
            foreach (var _ in bones)
            {
                frame.Transforms.Add(new RmvVector3(0, 0.1f, 0));
                frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            }
            var part = new AnimationFile.AnimationPart();
            part.DynamicFrames.Add(frame);
            file.AnimationParts.Add(part);
            return new GameSkeleton(file, player);
        }

        static AnimationClip Clip(Quaternion jaw, Quaternion earAndBody)
        {
            var clip = new AnimationClip();
            for (var i = 0; i < 2; i++)
                clip.DynamicFrames.Add(new AnimationClip.KeyFrame
                {
                    Position = [.. Enumerable.Repeat(new Vector3(0, 0.1f, 0), 6)],
                    Rotation = [Quaternion.Identity, Quaternion.Identity, jaw, earAndBody, earAndBody, jaw],
                    Scale = [.. Enumerable.Repeat(Vector3.One, 6)],
                });
            clip.PlayTimeInSec = 1;
            return clip;
        }
    }
}
