using Editors.AnimationMeta.SuperView.Visualisation.Rules;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace Test.AnimationMeta
{
    // LHAND_POSE / RHAND_POSE are keys: the pose set at a key's time holds until that hand's next key,
    // and only the bones under the hand move.
    public class HandPoseRuleTests
    {
        const int Root = 0, HandLeft = 1, FingerLeft = 2, FingerLeftTip = 3, HandRight = 4, FingerRight = 5;

        static readonly Quaternion s_grip = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathHelper.PiOver2);
        static readonly Quaternion s_flat = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathHelper.PiOver2);

        GameSkeleton _skeleton = null!;

        [SetUp]
        public void Setup() => _skeleton = CreateSkeleton();

        [Test]
        public void Key_PosesEveryFingerBoneOfItsHandOnly()
        {
            var rule = new HandPoseRule(_skeleton, "hand_left", [new HandPoseRule.Key(0, PoseAll(s_grip), 1)]);

            var frame = Apply(rule, 0);

            Assert.That(AngleBetween(Rotation(frame, FingerLeft), s_grip), Is.LessThan(1e-3));
            Assert.That(AngleBetween(Rotation(frame, FingerLeftTip), s_grip), Is.LessThan(1e-3));
            Assert.That(AngleBetween(Rotation(frame, HandLeft), Quaternion.Identity), Is.LessThan(1e-3), "the hand itself belongs to the animation");
            Assert.That(AngleBetween(Rotation(frame, FingerRight), Quaternion.Identity), Is.LessThan(1e-3), "the other hand's fingers");
        }

        [Test]
        public void Keys_HoldUntilTheNextKey()
        {
            var rule = new HandPoseRule(_skeleton, "hand_left", [new HandPoseRule.Key(0.5f, PoseAll(s_flat), 1), new HandPoseRule.Key(0, PoseAll(s_grip), 1)]);

            Assert.That(AngleBetween(Rotation(Apply(rule, 0.3f), FingerLeft), s_grip), Is.LessThan(1e-3));
            Assert.That(AngleBetween(Rotation(Apply(rule, 0.5f), FingerLeft), s_flat), Is.LessThan(1e-3));
            Assert.That(AngleBetween(Rotation(Apply(rule, 2f), FingerLeft), s_flat), Is.LessThan(1e-3));
        }

        [Test]
        public void BeforeTheFirstKey_AndOnAReleaseKey_TheAnimationKeepsTheFingers()
        {
            var rule = new HandPoseRule(_skeleton, "hand_left", [new HandPoseRule.Key(0.4f, PoseAll(s_grip), 1), new HandPoseRule.Key(0.8f, null, 1)]);

            Assert.That(AngleBetween(Rotation(Apply(rule, 0.1f), FingerLeft), Quaternion.Identity), Is.LessThan(1e-3));
            Assert.That(AngleBetween(Rotation(Apply(rule, 0.9f), FingerLeft), Quaternion.Identity), Is.LessThan(1e-3));
        }

        [Test]
        public void SameTime_TheLaterKeyWins()
        {
            // The builder lists persistent keys first, so the animation's own meta overrides them.
            var rule = new HandPoseRule(_skeleton, "hand_left", [new HandPoseRule.Key(0, PoseAll(s_grip), 1), new HandPoseRule.Key(0, PoseAll(s_flat), 1)]);

            Assert.That(AngleBetween(Rotation(Apply(rule, 0), FingerLeft), s_flat), Is.LessThan(1e-3));
        }

        [Test]
        public void Weight_BlendsFromTheAnimationTowardThePose()
        {
            var rule = new HandPoseRule(_skeleton, "hand_left", [new HandPoseRule.Key(0, PoseAll(s_grip), 0.5f)]);

            var angle = 2 * MathF.Acos(Math.Clamp(Rotation(Apply(rule, 0), FingerLeft).W, -1, 1));
            Assert.That(angle, Is.EqualTo(MathHelper.PiOver4).Within(1e-4));
        }

        AnimationFrame Apply(HandPoseRule rule, float time)
        {
            var frame = _skeleton.ConvertToAnimationFrame();
            for (var i = 0; i < _skeleton.BoneCount; i++)
                rule.TransformFrameLocalSpace(frame, i, time);
            return frame;
        }

        static Quaternion Rotation(AnimationFrame frame, int bone) => frame.BoneTransforms[bone].Rotation;

        static float AngleBetween(Quaternion a, Quaternion b) => 2 * MathF.Acos(Math.Clamp(Math.Abs(Quaternion.Dot(a, b)), 0, 1));

        AnimationFrame PoseAll(Quaternion rotation)
        {
            // A slot animation poses the whole skeleton; the rule must take only the fingers from it.
            var pose = _skeleton.ConvertToAnimationFrame();
            foreach (var bone in pose.BoneTransforms)
                bone.Rotation = rotation;
            return pose;
        }

        static GameSkeleton CreateSkeleton()
        {
            var bones = new (string Name, int Parent)[]
            {
                ("root", AnimationFile.BoneIndexNoParent), ("hand_left", Root), ("finger_left_0", HandLeft), ("finger_left_1", FingerLeft),
                ("hand_right", Root), ("finger_right_0", HandRight),
            };
            var skeletonFile = new AnimationFile();
            skeletonFile.Header.SkeletonName = "test_skeleton";
            skeletonFile.Bones = bones.Select((b, i) => new AnimationFile.BoneInfo { Id = i, Name = b.Name, ParentId = b.Parent }).ToArray();

            var frame = new AnimationFile.Frame();
            foreach (var _ in bones)
            {
                frame.Transforms.Add(new RmvVector3(0, 1, 0));
                frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            }
            var animationPart = new AnimationFile.AnimationPart();
            animationPart.DynamicFrames.Add(frame);
            skeletonFile.AnimationParts.Add(animationPart);
            return new GameSkeleton(skeletonFile, null!);
        }
    }
}
