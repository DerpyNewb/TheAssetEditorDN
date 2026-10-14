using System.Diagnostics;
using System.Windows.Threading;
using Shared.Core.Misc;
using Shared.Ui.Common;

namespace Shared.UiTest.Common
{
    // Every load runs on the UI thread, which draws nothing while it works: the popup, on a thread of its own, says what is loading
    // and for how long, so a long load does not look like a hang.
    internal class BusyPopupTests
    {
        [Test]
        public void TheWorkNamedLast_IsTheOneSaid_AndClosingItGivesBackTheOuter()
        {
            Assert.That(BusyWork.Current, Is.Null);
            using (BusyWork.Doing("Opening a.pack"))
            {
                using (BusyWork.Doing("Building the file cache"))
                    Assert.That(BusyWork.Current, Is.EqualTo("Building the file cache"));
                Assert.That(BusyWork.Current, Is.EqualTo("Opening a.pack"));
            }
            Assert.That(BusyWork.Current, Is.Null);
        }

        // An editor's name is closed once the app is idle, after the work opened inside it: each name goes on its own.
        [Test]
        public void ANameClosedOutOfOrder_TakesOnlyItself_AndClosingTwiceIsHarmless()
        {
            var outer = BusyWork.Doing("Opening a.rigid_model_v2");
            var inner = BusyWork.Doing("Loading a.rigid_model_v2");
            outer.Dispose();
            Assert.That(BusyWork.Current, Is.EqualTo("Loading a.rigid_model_v2"));
            outer.Dispose();
            Assert.That(BusyWork.Current, Is.EqualTo("Loading a.rigid_model_v2"));
            inner.Dispose();
            Assert.That(BusyWork.Current, Is.Null);
        }

        static void Busy(int milliseconds) => Thread.Sleep(milliseconds);

        static void Watch()
        {
            BusyPopup.Watch(Dispatcher.CurrentDispatcher);
            var clock = Stopwatch.StartNew();
            while (!BusyPopup.Ready && clock.ElapsedMilliseconds < 10000)
                Answering(50);
            Answering(300);
        }

        // The UI thread answering: what a modal dialog's loop and an idle app both do.
        static void Answering(int milliseconds)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < milliseconds)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Thread.Sleep(10);
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void WhileTheUiThreadIsBusy_ThePopupSaysWhatIsLoading_AndGoesWhenItAnswers()
        {
            Watch();

            // Named, it shows after half a second, not the second an unnamed freeze waits.
            using (BusyWork.Doing("Opening a.pack"))
            {
                Busy(800);
                Assert.That(BusyPopup.Shown, Is.EqualTo("Opening a.pack"));
            }
            Answering(600);

            Assert.That(BusyPopup.Shown, Is.Null);
        }

        // A short hitch with nothing named stays quiet; a longer one is still shown, since it is what looked like a hang.
        [Test, Apartment(ApartmentState.STA)]
        public void AnUnnamedFreeze_ShowsAfterASecond_AsWorking()
        {
            Watch();

            Busy(700);
            Assert.That(BusyPopup.Shown, Is.Null);
            Busy(800);
            Assert.That(BusyPopup.Shown, Is.EqualTo("Working"));

            Answering(600);
            Assert.That(BusyPopup.Shown, Is.Null);
        }

        // A dialog shown in the middle of a load keeps the UI thread answering: the popup must not cover it.
        [Test, Apartment(ApartmentState.STA)]
        public void NamedWork_WhileTheUiThreadAnswers_ShowsNothing()
        {
            Watch();

            using (BusyWork.Doing("Opening a.pack"))
                Answering(1200);

            Assert.That(BusyPopup.Shown, Is.Null);
        }
    }
}
