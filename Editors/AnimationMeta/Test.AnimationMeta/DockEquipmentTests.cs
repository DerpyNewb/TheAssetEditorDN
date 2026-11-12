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
    // DOCK_EQPT_* tags in SuperView: the prop is posed against the hand by the set's DOCK_EQUIPMENT_* animation.
    public class DockEquipmentTests
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

        static readonly Vector3 LeftGrip = new(-0.5f, 1, 0.1f);
        static readonly Vector3 RightGrip = new(0.5f, 1, 0.1f);
        const int Prop = 3;

        GameSkeleton _skeleton = null!;
        AnimationClip _main = null!;
        AnimationPlayer _player = null!;
        MetaDataBuilder _builder = null!;
        Fragment _fragment = null!;

        [TearDown]
        public void TearDown() => _builder.Dispose();

        [SetUp]
        public void Setup()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            _player = new AnimationPlayer();
            _skeleton = Skeleton(_player);
            _main = Clip(Vector3.Zero);
            _player.SetAnimation(_main, _skeleton);

            // Each hand's dock animation holds be_prop_0 10 cm in front of that hand.
            var pack = runner.CreateOutputPack();
            runner.PackFileService.AddFilesToPack(pack,
            [
                new NewPackFileEntry("animations/test", PackFile.CreateFromBytes("dock_left.anim", AnimationFile.ConvertToBytes(Clip(LeftGrip).ConvertToFileFormat(_skeleton)))),
                new NewPackFileEntry("animations/test", PackFile.CreateFromBytes("dock_right.anim", AnimationFile.ConvertToBytes(Clip(RightGrip).ConvertToFileFormat(_skeleton)))),
            ]);
            _fragment = new Fragment(
            [
                new() { SlotName = "DOCK_EQUIPMENT_LEFT_HAND", AnimationFile = "animations/test/dock_left.anim" },
                new() { SlotName = "DOCK_EQUIPMENT_RIGHT_HAND", AnimationFile = "animations/test/dock_right.anim" },
            ]);
            // The dock path uses only the pack file service.
            _builder = new MetaDataBuilder(null!, null!, null!, runner.PackFileService, null!);
        }

        Vector3 PropAt(float time, params DockEquipment[] tags)
        {
            _builder.Create(null, new ParsedMetadataFile { Version = 2, Attributes = [.. tags] }, null, new GroupNode("root"), new Provider(_skeleton), _player, _fragment);
            var frame = AnimationSampler.Sample(0, 0, _skeleton, _main, _player.AnimationRules, timeInSec: time);
            return frame.GetSkeletonAnimatedWorld(_skeleton, Prop).Translation;
        }

        static DockEquipment Right(float start = 0, int prop = 1) => new DockEquipmentRHand_v11 { PropBoneId = prop, StartTime = start };
        static DockEquipment Left(float start = 0) => new DockEquipmentLHand_v11 { PropBoneId = 1, StartTime = start };

        // Docked to both hands from the same moment, the prop goes to the right hand in either order. Vanilla does this
        // 165 times, nearly all from 0 to 0; in all nine where the animation's own keys hold the prop rigid to one hand,
        // that hand is the right one (Tamurkhan's and the Maneater's hammers: RHAND, then LHAND). Drawn last-wins, the
        // Chaos Dwarf gunner's gatling stood up in his left hand's grip while the game held it in his right.
        [TestCase(true)]
        [TestCase(false)]
        public void DockedToBothHandsAtOnce_TheRightHandHoldsIt(bool rightFirst)
        {
            var at = rightFirst ? PropAt(0.5f, Right(), Left()) : PropAt(0.5f, Left(), Right());
            Assert.That(Vector3.Distance(at, RightGrip), Is.LessThan(1e-4), $"prop at {at}");
        }

        [Test]
        public void DockedToTheLeftHandAlone_TheLeftHandHoldsIt()
        {
            var at = PropAt(0.5f, Left());
            Assert.That(Vector3.Distance(at, LeftGrip), Is.LessThan(1e-4), $"prop at {at}");
        }

        // Another prop going to the right hand at the same moment leaves this one in the left.
        [Test]
        public void AnotherPropToTheRightHand_LeavesThisOneInTheLeft()
        {
            var at = PropAt(0.5f, Left(), Right(prop: 2));
            Assert.That(Vector3.Distance(at, LeftGrip), Is.LessThan(1e-4), $"prop at {at}");
        }

        // A left-hand dock that starts later is a hand-over, not a tie: it still takes the prop when it starts.
        [Test]
        public void HandedToTheLeftHandLater_TheLeftHandTakesItThen()
        {
            var at = PropAt(0.6f, Right(), Left(start: 0.5f));
            Assert.That(Vector3.Distance(at, LeftGrip), Is.LessThan(1e-4), $"prop at {at}");
        }

        // animroot, the two hands, and be_prop_0 at the origin.
        static GameSkeleton Skeleton(AnimationPlayer player)
        {
            var bones = new (string Name, int Parent, Vector3 At)[]
            {
                ("animroot", -1, Vector3.Zero), ("hand_left", 0, new Vector3(-0.5f, 1, 0)), ("hand_right", 0, new Vector3(0.5f, 1, 0)), ("be_prop_0", 0, Vector3.Zero),
            };
            var file = new AnimationFile();
            file.Header.SkeletonName = "test_skeleton";
            file.Bones = bones.Select((b, i) => new AnimationFile.BoneInfo { Id = i, Name = b.Name, ParentId = b.Parent }).ToArray();
            var frame = new AnimationFile.Frame();
            foreach (var b in bones)
            {
                frame.Transforms.Add(new RmvVector3(b.At.X, b.At.Y, b.At.Z));
                frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            }
            var part = new AnimationFile.AnimationPart();
            part.DynamicFrames.Add(frame);
            file.AnimationParts.Add(part);
            return new GameSkeleton(file, player);
        }

        // The skeleton's pose with be_prop_0 at `prop`, two frames over a second.
        static AnimationClip Clip(Vector3 prop)
        {
            var clip = new AnimationClip();
            for (var i = 0; i < 2; i++)
                clip.DynamicFrames.Add(new AnimationClip.KeyFrame
                {
                    Position = [Vector3.Zero, new Vector3(-0.5f, 1, 0), new Vector3(0.5f, 1, 0), prop],
                    Rotation = [Quaternion.Identity, Quaternion.Identity, Quaternion.Identity, Quaternion.Identity],
                    Scale = [Vector3.One, Vector3.One, Vector3.One, Vector3.One],
                });
            clip.PlayTimeInSec = 1;
            return clip;
        }
    }
}
