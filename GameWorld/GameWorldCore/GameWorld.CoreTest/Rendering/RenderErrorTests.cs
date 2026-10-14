using System.Windows.Threading;
using GameWorld.Core.WpfWindow;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Services;

namespace GameWorld.CoreTest.Rendering
{
    // A frame that threw opened the error dialog from inside the frame. A frame can run inside a window message (a resize),
    // where Windows refuses to create a window: the dialog threw in turn, which took the whole app down, and the frame's own
    // error was never logged. The dialog now opens once the frame is over.
    public class RenderErrorTests
    {
        static readonly Exception Thrown = new InvalidOperationException("a frame went wrong");

        class ThrowingGame(IStandardDialogs dialogs) : WpfGame(null!, dialogs, null!, null!)
        {
            protected override void Update(GameTime gameTime) => throw Thrown;
            public void Frame() => Render(new GameTime());
        }

        [Test, Apartment(ApartmentState.STA)]
        public void AFrameThatThrows_ShowsTheErrorAfterTheFrame_NotInsideIt()
        {
            var dialogs = new Mock<IStandardDialogs>();
            var game = new ThrowingGame(dialogs.Object);

            game.Frame();
            dialogs.Verify(x => x.ShowExceptionWindow(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never, "the dialog opened inside the frame");

            // Run what the frame queued and stop there: once the dialog is done the game renders again, and this one has no device.
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            dialogs.Verify(x => x.ShowExceptionWindow(Thrown, It.IsAny<string>()), Times.Once);
        }
    }
}
