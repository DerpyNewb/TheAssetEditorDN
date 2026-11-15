using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace GameWorld.Core.Test.Rendering
{
    // Between two keyframes the pose blends from the earlier to the later. The sampler rounds to the nearest keyframe, and in the
    // second half of each gap it used to extrapolate back from the later key along the NEXT gap instead, so the pose jumped at every
    // half frame. At 1x that is a shiver; at 0.5x and 0.25x the bar's Speed box made it a visible twitch.
    public class AnimationSamplerInterpolationTests
    {
        // One root bone whose x is the frame number squared, so a blend along the wrong gap lands somewhere else.
        static (GameSkeleton, AnimationClip) Clip()
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

            var clip = new AnimationClip();
            for (var i = 0; i < 5; i++)
                clip.DynamicFrames.Add(new AnimationClip.KeyFrame { Position = [new Vector3(i * i, 0, 0)], Rotation = [Quaternion.Identity], Scale = [Vector3.One] });
            clip.PlayTimeInSec = 2;
            return (new GameSkeleton(skeletonFile, null!), clip);
        }

        static float X(float t)
        {
            var (skeleton, clip) = Clip();
            return AnimationSampler.Sample(t, skeleton, clip).BoneTransforms[0].WorldTransform.Translation.X;
        }

        [Test]
        public void SixTenthsIntoAGap_IsSixTenthsOfTheWayToTheNextKey()
        {
            // Frame position 2.6 of 0..4: from x = 4 toward x = 9.
            Assert.That(X(2.6f / 4), Is.EqualTo(7f).Within(1e-4));
        }

        [Test]
        public void ThePoseNeverStepsBack_WhileTheKeysOnlyMoveForward()
        {
            var last = float.MinValue;
            for (var i = 0; i <= 400; i++)
            {
                var x = X(i / 400f);
                Assert.That(x, Is.GreaterThanOrEqualTo(last - 1e-4), $"stepped back at t = {i / 400f}");
                last = x;
            }
        }
    }
}
