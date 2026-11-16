using Editors.Ipc;
using Microsoft.Extensions.DependencyInjection;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.Ipc
{
    // The KeyFrame editor over the pipe: open it on Karl's idle, pick bones, select frames, read poses. Copy and paste
    // go through the Windows clipboard, so they are checked live rather than here.
    public class KeyframeRequestsTests
    {
        const string KarlRmv = @"variantmeshes\wh_variantmodels\hu1\emp\emp_karl_franz\emp_karl_franz.rigid_model_v2";
        const string Idle = @"animations\battle\humanoid01\2handed_hammer\stand\hu1_2hh_stand_idle_01.anim";

        IIpcRequestHandler _handler = null!;

        [SetUp]
        public void Load()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"), true);
            _handler = runner.ServiceProvider.GetRequiredService<IIpcRequestHandler>();
        }

        IpcResponse Send(IpcRequest request) => _handler.HandleAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        EditorRequests.KeyframeState Keyframe(string name, Action<IpcRequest>? set = null)
        {
            var request = new IpcRequest { Action = "keyframe", Name = name };
            set?.Invoke(request);
            var reply = Send(request);
            Assert.That(reply.Ok, Is.True, reply.Error);
            return (EditorRequests.KeyframeState)reply.Data!;
        }

        [Test]
        public void Open_LoadsTheMeshAndClip()
        {
            var state = Keyframe("open", x => (x.Path, x.Animation) = (KarlRmv, Idle));

            Assert.That(state.Frames, Is.GreaterThan(10));
            Assert.That(state.Rows, Is.EqualTo(new[] { "All bones" }));
        }

        [Test]
        public void BonesThenSelect_GiveARowAndAFrameRange()
        {
            Keyframe("open", x => (x.Path, x.Animation) = (KarlRmv, Idle));

            Keyframe("bones", x => x.Bones = ["hand_left"]);
            var state = Keyframe("select", x => (x.Rows, x.Frames) = ([1, 1], [2, 6]));

            Assert.That(state.Rows, Is.EqualTo(new[] { "All bones", "hand_left" }));
            Assert.That(state.Selection, Is.EqualTo(new[] { 1, 1, 2, 6 }));
            Assert.That((state.FrameA, state.FrameB), Is.EqualTo(("2", "6")));
        }

        [Test]
        public void Frame_MovesTheCurrentFrame_AndPoseReadsIt()
        {
            Keyframe("open", x => (x.Path, x.Animation) = (KarlRmv, Idle));

            var state = Keyframe("frame", x => x.Frame = 10);
            var pose = Send(new IpcRequest { Action = "keyframe", Name = "pose", Frame = 10, Bones = ["hand_left"] });

            Assert.That(state.Frame, Is.EqualTo(10));
            Assert.That(pose.Ok, Is.True, pose.Error);
            Assert.That(((Dictionary<string, float[]>)pose.Data!)["hand_left"], Has.Length.EqualTo(7));
        }

        [Test]
        public void ABoneNameTheSkeletonLacks_IsRefused()
        {
            Keyframe("open", x => (x.Path, x.Animation) = (KarlRmv, Idle));

            var reply = Send(new IpcRequest { Action = "keyframe", Name = "bones", Bones = ["no_such_bone"] });

            Assert.That(reply.Ok, Is.False);
            Assert.That(reply.Error, Does.Contain("no_such_bone"));
        }
    }
}
