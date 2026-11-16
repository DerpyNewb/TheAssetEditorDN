using GameWorld.Core.Animation;
using GameWorld.Core.Animation.Posing;
using GameWorld.Core.Commands.Bone;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Selection;
using Microsoft.Xna.Framework;
using static GameWorld.Core.Test.Animation.Posing.TestSkeletons;

namespace GameWorld.Core.Test.Animation.Posing
{
    public class PinTests
    {
        static (AnimationClip Clip, BoneSelectionState State) Scene(GameSkeleton skeleton, int endBone)
        {
            var clip = new AnimationClip();
            clip.DynamicFrames.Add(BindFrame(skeleton));
            var state = new BoneSelectionState(null!) { CurrentAnimation = clip, Skeleton = skeleton, CurrentFrame = 0, EnableInverseKinematics = true, InverseKinematicsEndBoneIndex = endBone };
            state.BoneModifiedEvent += _ => { };
            return (clip, state);
        }

        static void Drag(BoneSelectionState state, int bone, Vector3 move)
        {
            var command = new TransformBoneCommand(null!);
            command.Configure([bone], state);
            command.ApplyTransformation(Matrix.CreateTranslation(Vector3.Zero), GizmoMode.Translate);
            command.ApplyTransformation(Matrix.CreateTranslation(move), GizmoMode.Translate);
            command.Execute();
        }

        [Test]
        public void Create_TakesTheGrandparentAsTheEndBone_AndTheCurrentPosition()
        {
            var skeleton = ArmAndLegs();
            var frame = BindFrame(skeleton);

            var pin = Pin.Create(frame, skeleton, FootL);

            Assert.That(pin.EndBone, Is.EqualTo(ThighL));
            Assert.That(Vector3.Distance(pin.Target, At(frame, skeleton, FootL)), Is.LessThan(1e-6f));
        }

        [Test]
        public void PinnedFoot_StaysPut_WhenThePelvisMoves()
        {
            var skeleton = ArmAndLegs();
            var frame = BindFrame(skeleton);
            var pins = new List<Pin> { Pin.Create(frame, skeleton, FootL), Pin.Create(frame, skeleton, FootR) };

            frame.Position[Pelvis] += new Vector3(0, -0.2f, 0);
            PinSolver.Resolve(frame, skeleton, pins, [Pelvis], null);

            foreach (var pin in pins)
                Assert.That(Vector3.Distance(At(frame, skeleton, pin.Bone), pin.Target), Is.LessThan(1e-3f), $"bone {pin.Bone}");
        }

        [Test]
        public void PinnedFeet_StayPut_WhenAnIkDragTurnsThePelvis()
        {
            var skeleton = ArmAndLegs();
            var (clip, state) = Scene(skeleton, Root);   // the drag's chain runs lower, upper, pelvis
            state.Pins.Add(Pin.Create(clip.DynamicFrames[0], skeleton, FootL));
            state.Pins.Add(Pin.Create(clip.DynamicFrames[0], skeleton, FootR));
            var pelvisBefore = clip.DynamicFrames[0].Rotation[Pelvis];

            Drag(state, Hand, new Vector3(-0.5f, -1.2f, 0.4f));

            var frame = clip.DynamicFrames[0];
            Assert.That(frame.Rotation[Pelvis], Is.Not.EqualTo(pelvisBefore), "the drag must turn the pelvis for this test to mean anything");
            foreach (var pin in state.Pins)
                Assert.That(Vector3.Distance(At(frame, skeleton, pin.Bone), pin.Target), Is.LessThan(1e-3f), $"bone {pin.Bone}");
        }

        // A pin made on one frame must not drag the foot back there while another frame is edited: each drag holds the pinned
        // bones where they are on the frame being edited when the drag starts.
        [Test]
        public void APin_HoldsTheBoneWhereItIsWhenTheDragStarts_OnAnyFrame()
        {
            var skeleton = ArmAndLegs();
            var (clip, state) = Scene(skeleton, Root);
            var second = BindFrame(skeleton);
            second.Position[Pelvis] += new Vector3(0.05f, -0.15f, 0.05f);
            clip.DynamicFrames.Add(second);
            state.Pins.Add(Pin.Create(clip.DynamicFrames[0], skeleton, FootL));
            state.CurrentFrame = 1;
            var footOnFrame1 = At(second, skeleton, FootL);

            Drag(state, Hand, new Vector3(-0.5f, -1.2f, 0.4f));

            Assert.That(Vector3.Distance(At(clip.DynamicFrames[1], skeleton, FootL), footOnFrame1), Is.LessThan(1e-3f));
        }

        [Test]
        public void TwoFootPins_DoNotTouchTheHips()
        {
            var skeleton = ArmAndLegs();
            var frame = BindFrame(skeleton);
            var pins = new List<Pin> { Pin.Create(frame, skeleton, FootL), Pin.Create(frame, skeleton, FootR) };
            frame.Position[Pelvis] += new Vector3(0.05f, -0.2f, 0);
            var pelvis = frame.Rotation[Pelvis];

            var turned = PinSolver.Resolve(frame, skeleton, pins, [Pelvis], null);

            Assert.That(frame.Rotation[Pelvis], Is.EqualTo(pelvis));
            Assert.That(turned, Is.EquivalentTo(new[] { ShinL, ThighL, ShinR, ThighR }));
        }

        [Test]
        public void PinOnTheDraggedBone_GivesWay()
        {
            var skeleton = ArmAndLegs();
            var (clip, state) = Scene(skeleton, Upper);
            var handBefore = At(clip.DynamicFrames[0], skeleton, Hand);
            state.Pins.Add(Pin.Create(clip.DynamicFrames[0], skeleton, Hand));
            var move = new Vector3(-0.5f, 0.8f, 0.3f);

            Drag(state, Hand, move);

            Assert.That(Vector3.Distance(At(clip.DynamicFrames[0], skeleton, Hand), handBefore + move), Is.LessThan(2e-4f));
        }

        [Test]
        public void PinChainOverlappingTheDrag_LeavesTheDraggedBonesAlone()
        {
            var skeleton = ArmAndLegs();
            var (withPin, pinnedState) = Scene(skeleton, Root);
            var (withoutPin, plainState) = Scene(skeleton, Root);
            pinnedState.Pins.Add(Pin.Create(withPin.DynamicFrames[0], skeleton, Other));   // its chain is the pelvis, which the drag turns
            var move = new Vector3(-0.5f, -1.2f, 0.4f);

            Drag(pinnedState, Hand, move);
            Drag(plainState, Hand, move);

            Assert.That(withPin.DynamicFrames[0].Rotation[Pelvis], Is.EqualTo(withoutPin.DynamicFrames[0].Rotation[Pelvis]));
        }

        [Test]
        public void HoldAcross_KeepsTheFootOnEveryFrame()
        {
            var skeleton = ArmAndLegs();
            var clip = new AnimationClip();
            for (var i = 0; i < 6; i++)
            {
                var frame = BindFrame(skeleton);
                frame.Position[Pelvis] += new Vector3(0.02f * MathF.Sin(i), -0.06f * i, 0.02f * i);   // a sway the leg can still reach
                clip.DynamicFrames.Add(frame);
            }
            var last = clip.DynamicFrames[5].Clone();
            var pin = Pin.Create(clip.DynamicFrames[0], skeleton, FootL);

            PinSolver.HoldAcross(clip, skeleton, pin, 0, 4, null);

            for (var i = 0; i <= 4; i++)
                Assert.That(Vector3.Distance(At(clip.DynamicFrames[i], skeleton, FootL), pin.Target), Is.LessThan(1e-3f), $"frame {i}");
            Assert.That(clip.DynamicFrames[5].Rotation, Is.EqualTo(last.Rotation), "frames outside A-B are untouched");
        }

        [Test]
        public void NoPins_FrameMatchesTheOldBehaviour()
        {
            var skeleton = ArmAndLegs();
            var (clip, state) = Scene(skeleton, Upper);
            var expected = BindFrame(skeleton);
            var target = At(expected, skeleton, Hand) + new Vector3(-0.5f, 0.8f, 0.3f);
            TransformBoneCommand.SolveInverseKinematics(expected, skeleton, Hand, Upper, target);

            Drag(state, Hand, new Vector3(-0.5f, 0.8f, 0.3f));

            Assert.That(clip.DynamicFrames[0].Rotation, Is.EqualTo(expected.Rotation));
        }
    }
}
