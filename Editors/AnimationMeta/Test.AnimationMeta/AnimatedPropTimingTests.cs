using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

namespace Test.AnimationMeta
{
    // An ANIMATED_PROP's animation starts at the tag's Start Time on the main animation's clock,
    // not when the prop was created, and plays once.
    public class AnimatedPropTimingTests
    {
        [TestCase(0.2f, 0f, Description = "before the tag starts: the first frame")]
        [TestCase(0.9f, 45f, Description = "half a second in")]
        [TestCase(3f, 90f, Description = "past the clip's end: the last frame")]
        public void PropPlays_FromItsStartTime_OnTheMainClock(float mainTimeInSec, float expectedDegrees)
        {
            var player = Player();
            var prop = new AnimatedPropInstance(new GroupNode("prop"), player, startTime: 0.4f);

            prop.Update(mainTimeInSec);

            var rotation = player.GetCurrentAnimationFrame().BoneTransforms[0].Rotation;
            Assert.That(MathHelper.ToDegrees(2 * MathF.Acos(Math.Clamp(Math.Abs(rotation.W), 0, 1))), Is.EqualTo(expectedDegrees).Within(0.5f));
        }

        // Drawn only inside Start-End; End at or before Start runs to the clip's end. Measured in game 2026-11-19.
        [TestCase(0.4f, 0.8f, 0.2f, false)]
        [TestCase(0.4f, 0.8f, 0.6f, true)]
        [TestCase(0.4f, 0.8f, 0.9f, false)]
        [TestCase(0.4f, 0f, 3f, true)]
        [TestCase(0.4f, 0.4f, 0.3f, false)]
        public void PropIsDrawn_OnlyWhileItsTagIsOn(float start, float end, float mainTimeInSec, bool expected)
        {
            var node = new GroupNode("prop");
            new AnimatedPropInstance(node, Player(), start, end).Update(mainTimeInSec);
            Assert.That(node.IsVisible, Is.EqualTo(expected));
        }

        // Override Prop n gives way to a weapon shown on be_prop_(n-1), and only to one that is shown.
        [TestCase(2, true, true, false)]
        [TestCase(2, false, true, true)]
        [TestCase(2, true, false, true, Description = "an empty slot has nothing to give way to")]
        [TestCase(1, true, true, true, Description = "be_prop_0 carries no weapon here")]
        [TestCase(0, true, true, true)]
        public void PropGivesWay_ToTheWeaponItsOverridePropNames(int overrideProp, bool weaponVisible, bool slotHasModel, bool expected)
        {
            var slot = new SlotNode("weapon", "be_prop_1") { IsVisible = weaponVisible };
            if (slotHasModel)
                slot.AddObject(new GroupNode("sword"));
            var model = new GroupNode("model");
            model.AddObject(slot);
            var scene = new SceneObject("asset") { ModelNode = model };

            var node = new GroupNode("prop");
            new AnimatedPropInstance(node, Player(), 0, 0, MetaDataBuilder.WeaponShown(scene, overrideProp)).Update(0.5f);

            Assert.That(node.IsVisible, Is.EqualTo(expected));
        }

        static AnimationPlayer Player()
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

            // One second, turning the bone 90 degrees about Z.
            var clip = new AnimationClip();
            foreach (var degrees in new[] { 0f, 90f })
                clip.DynamicFrames.Add(new AnimationClip.KeyFrame { Position = [Vector3.Zero], Rotation = [Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathHelper.ToRadians(degrees))], Scale = [Vector3.One] });
            clip.PlayTimeInSec = 1;

            var player = new AnimationPlayer();
            player.SetAnimation(clip, skeleton);
            return player;
        }
    }
}
