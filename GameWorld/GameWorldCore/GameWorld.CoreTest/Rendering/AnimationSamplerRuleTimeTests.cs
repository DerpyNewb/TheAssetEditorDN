using GameWorld.Core.Animation;
using GameWorld.Core.Animation.AnimationChange;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace GameWorld.Core.Test.Rendering
{
    // Meta-tag rules (docking, hand poses) are timed by their tag's start, so the sampler must hand
    // them the playback time. It used to pass the clip's length, which every start time passes.
    public class AnimationSamplerRuleTimeTests
    {
        class RecordTime : ILocalSpaceAnimationRule, IWorldSpaceAnimationRule
        {
            public float? Local, World;
            public void TransformFrameLocalSpace(AnimationFrame frame, int boneId, float time) => Local = time;
            public void TransformFrameWorldSpace(AnimationFrame frame, float time) => World = time;
        }

        [TestCase(0f, 0f)]
        [TestCase(0.25f, 0.5f)]
        [TestCase(1f, 2f)]
        public void Rules_GetThePlaybackTime(float t, float expectedSeconds)
        {
            var skeletonFile = new AnimationFile();
            skeletonFile.Header.SkeletonName = "test_skeleton";
            skeletonFile.Bones = [new AnimationFile.BoneInfo { Id = 0, Name = "root", ParentId = AnimationFile.BoneIndexNoParent }];
            var bind = new AnimationFile.Frame();
            bind.Transforms.Add(new RmvVector3(0, 0, 0));
            bind.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            var part = new AnimationFile.AnimationPart();
            part.DynamicFrames.Add(bind);
            skeletonFile.AnimationParts.Add(part);
            var skeleton = new GameSkeleton(skeletonFile, null!);

            var clip = new AnimationClip();
            for (var i = 0; i < 5; i++)
                clip.DynamicFrames.Add(new AnimationClip.KeyFrame { Position = [Vector3.Zero], Rotation = [Quaternion.Identity], Scale = [Vector3.One] });
            clip.PlayTimeInSec = 2;

            var rule = new RecordTime();
            AnimationSampler.Sample(t, skeleton, clip, [rule]);

            Assert.That(rule.Local, Is.EqualTo(expectedSeconds).Within(1e-4));
            Assert.That(rule.World, Is.EqualTo(expectedSeconds).Within(1e-4));
        }
    }
}
