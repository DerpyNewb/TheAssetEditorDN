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
    // LHAND_POSE and RHAND_POSE through SuperView's builder. Only version 10 was read, so the 26 vanilla tags at version 2
    // left the fingers as the animation had them.
    public class HandPoseBuilderTests
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

        const int FingerLeft = 2, FingerRight = 4;
        static readonly Quaternion s_grip = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathHelper.PiOver2);

        [TestCase(2)]
        [TestCase(10)]
        public void AHandPoseTag_PosesItsHandsFingers_FromItsStartTime(int version)
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var player = new AnimationPlayer();
            var skeleton = Skeleton(player);
            var main = Clip(Quaternion.Identity);
            player.SetAnimation(main, skeleton);

            // The set's grip slot turns every bone; the tag must take only the right hand's fingers from it.
            var pack = runner.CreateOutputPack();
            runner.PackFileService.AddFilesToPack(pack,
                [new NewPackFileEntry("animations/test", PackFile.CreateFromBytes("grip.anim", AnimationFile.ConvertToBytes(Clip(s_grip).ConvertToFileFormat(skeleton))))]);
            var set = new Fragment([new() { SlotName = "HAND_POSE_GRIP", AnimationFile = "animations/test/grip.anim" }]);

            ParsedMetadataAttribute tag = version == 2
                ? new RHandPose_v2 { HandPoseId = 3, Weight = 1, StartTime = 0.4f }
                : new RHandPose { HandPoseId = 3, Weight = 1, StartTime = 0.4f };
            (tag.Name, tag.Version) = ("RHAND_POSE", version);

            using var builder = new MetaDataBuilder(null!, null!, null!, runner.PackFileService, null!);
            builder.Create(null, new ParsedMetadataFile { Version = 2, Attributes = [tag] }, null, new GroupNode("root"), new Provider(skeleton), player, set);

            Quaternion Turn(int bone, float time)
            {
                var frame = AnimationSampler.Sample(0, 0, skeleton, main, player.AnimationRules, timeInSec: time);
                frame.GetSkeletonAnimatedWorld(skeleton, bone).Decompose(out _, out var turn, out _);
                return turn;
            }
            static float Degrees(Quaternion a, Quaternion b) => MathHelper.ToDegrees(2 * MathF.Acos(Math.Clamp(Math.Abs(Quaternion.Dot(a, b)), 0, 1)));

            Assert.That(Degrees(Turn(FingerRight, 0.2f), Quaternion.Identity), Is.LessThan(0.01), "before its start time");
            Assert.That(Degrees(Turn(FingerRight, 0.6f), s_grip), Is.LessThan(0.01), "after its start time");
            Assert.That(Degrees(Turn(FingerLeft, 0.6f), Quaternion.Identity), Is.LessThan(0.01), "the other hand");
        }

        // root, then each hand with one finger.
        static GameSkeleton Skeleton(AnimationPlayer player)
        {
            var bones = new (string Name, int Parent)[] { ("root", -1), ("hand_left", 0), ("finger_left_0", 1), ("hand_right", 0), ("finger_right_0", 3) };
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

        // Every bone turned by `turn`, two frames over a second.
        static AnimationClip Clip(Quaternion turn)
        {
            var clip = new AnimationClip();
            for (var i = 0; i < 2; i++)
                clip.DynamicFrames.Add(new AnimationClip.KeyFrame
                {
                    Position = [.. Enumerable.Repeat(new Vector3(0, 0.1f, 0), 5)],
                    Rotation = [.. Enumerable.Repeat(turn, 5)],
                    Scale = [.. Enumerable.Repeat(Vector3.One, 5)],
                });
            clip.PlayTimeInSec = 1;
            return clip;
        }
    }
}
