using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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

        static void Watch(Func<Window?>? main = null)
        {
            BusyPopup.Watch(Dispatcher.CurrentDispatcher, main);
            var clock = Stopwatch.StartNew();
            while (!BusyPopup.Ready && clock.ElapsedMilliseconds < 10000)
                Answering(50);
            Answering(300);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint relation);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out RECT value, int size);

        // On screen, in pixels; a frame is what is drawn, without the invisible resize border a window's rectangle takes in.
        static (int Left, int Top, int Right, int Bottom) Bounds(IntPtr window, bool frame = false)
        {
            if (!frame || DwmGetWindowAttribute(window, 9, out var r, Marshal.SizeOf<RECT>()) != 0)
                GetWindowRect(window, out r);
            return (r.Left, r.Top, r.Right, r.Bottom);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, System.Text.StringBuilder name, int size);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        // WPF owns a window kept out of the taskbar by a hidden one of its own, which Windows keeps just under it.
        static IntPtr NextShown(IntPtr window)
        {
            var next = GetWindow(window, 2);
            while (next != IntPtr.Zero && !IsWindowVisible(next))
                next = GetWindow(next, 2);
            return next;
        }

        static string Describe(IntPtr from, IntPtr main)
        {
            var lines = new List<string>();
            var w = from;
            for (var i = 0; i < 8 && w != IntPtr.Zero; i++, w = GetWindow(w, 2))
            {
                var (c, n) = (new System.Text.StringBuilder(256), new System.Text.StringBuilder(256));
                GetClassName(w, c, 256);
                GetWindowText(w, n, 256);
                GetWindowThreadProcessId(w, out var pid);
                lines.Add($"{w} {c} '{n}' visible={IsWindowVisible(w)} ours={pid == Environment.ProcessId} main={w == main}");
            }
            return string.Join(" | ", lines);
        }

        static Window ShowMain()
        {
            var main = new Window { Title = "BusyPopupTests", Left = 200, Top = 150, Width = 700, Height = 500, ShowActivated = false };
            main.Show();
            return main;
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

        // Centred on the window as it is on screen, maximised too: a maximised window used to be centred on as it was before.
        [Test, Apartment(ApartmentState.STA)]
        public void OverTheMainWindow_NormalOrMaximised_ACoverDimsAllOfIt_AndThePopupIsCentredOnIt()
        {
            var main = ShowMain();
            var handle = new WindowInteropHelper(main).Handle;
            Watch(() => main);
            try
            {
                foreach (var state in new[] { WindowState.Normal, WindowState.Maximized })
                {
                    main.WindowState = state;
                    Answering(300);
                    using var opening = BusyWork.Doing("Opening a.pack");
                    Busy(800);
                    var window = Bounds(handle, frame: true);
                    var popup = Bounds(BusyPopup.PopupHandle);
                    Assert.That(Bounds(BusyPopup.CoverHandle), Is.EqualTo(window), $"{state}: the cover");
                    var off = (popup.Left + popup.Right - window.Left - window.Right, popup.Top + popup.Bottom - window.Top - window.Bottom);
                    Assert.That(Math.Abs(off.Item1) <= 2 && Math.Abs(off.Item2) <= 2, $"{state}: the popup {popup} is off the middle of {window} by {off} (twice over)");
                    Answering(600);
                }
            }
            finally
            {
                main.Close();
            }
        }

        // A click during a load lands on the cover and goes nowhere. The cover lies directly over the main window, not over every app,
        // so a window the user switches to still covers it.
        [Test, Apartment(ApartmentState.STA)]
        public void TheCover_LiesDirectlyOverTheMainWindow_ThePopupOverTheCover_AndBothGoWhenItAnswers()
        {
            var main = ShowMain();
            var handle = new WindowInteropHelper(main).Handle;
            Watch(() => main);
            try
            {
                using (BusyWork.Doing("Opening a.pack"))
                {
                    Busy(800);
                    Assert.That(NextShown(BusyPopup.CoverHandle), Is.EqualTo(handle), Describe(BusyPopup.CoverHandle, handle));
                    var underPopup = new List<IntPtr>();
                    for (var w = GetWindow(BusyPopup.PopupHandle, 2); w != IntPtr.Zero && underPopup.Count < 10000; w = GetWindow(w, 2))
                        underPopup.Add(w);
                    Assert.That(underPopup, Does.Contain(BusyPopup.CoverHandle));

                    // Brought to the front while still held (a click on its taskbar button), the window takes the cover and the
                    // popup back over it.
                    SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
                    Busy(400);
                    Assert.That(NextShown(BusyPopup.CoverHandle), Is.EqualTo(handle), Describe(BusyPopup.CoverHandle, handle));
                    Assert.That(NextShown(BusyPopup.PopupHandle), Is.EqualTo(BusyPopup.CoverHandle), Describe(BusyPopup.PopupHandle, handle));
                }

                Answering(600);
                Assert.That((IsWindowVisible(BusyPopup.CoverHandle), IsWindowVisible(BusyPopup.PopupHandle)), Is.EqualTo((false, false)));
            }
            finally
            {
                main.Close();
            }
        }

        // Seen in the app: a load started while the start panel was up dimmed the main window under the panel, and the popup, on
        // the main window's middle, was hidden behind it. A window of the app's own over the main window is covered too.
        [Test, Apartment(ApartmentState.STA)]
        public void AWindowOfTheAppOverTheMainWindow_IsCoveredToo_AndThePopupStaysOnTop()
        {
            var main = ShowMain();
            var handle = new WindowInteropHelper(main).Handle;
            var panel = new Window { Title = "panel", Owner = main, Left = 350, Top = 300, Width = 400, Height = 200, ShowActivated = false };
            panel.Show();
            var panelHandle = new WindowInteropHelper(panel).Handle;
            Watch(() => main);
            try
            {
                using (BusyWork.Doing("Opening a.pack"))
                {
                    Busy(800);
                    Assert.That(NextShown(BusyPopup.CoverHandle), Is.EqualTo(panelHandle), Describe(BusyPopup.CoverHandle, handle));
                    Assert.That(NextShown(BusyPopup.PopupHandle), Is.EqualTo(BusyPopup.CoverHandle), Describe(BusyPopup.PopupHandle, handle));
                }
            }
            finally
            {
                panel.Close();
                main.Close();
            }
        }

        [Test, Apartment(ApartmentState.STA)]
        public void WithTheMainWindowMinimised_NothingShows()
        {
            var main = ShowMain();
            main.WindowState = WindowState.Minimized;
            Watch(() => main);
            try
            {
                using (BusyWork.Doing("Opening a.pack"))
                    Busy(800);
                Assert.That((BusyPopup.Shown, IsWindowVisible(BusyPopup.CoverHandle)), Is.EqualTo(((string?)null, false)));
            }
            finally
            {
                main.Close();
            }
        }
    }
}
