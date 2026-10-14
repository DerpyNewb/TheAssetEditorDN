using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Shared.Core.Misc;

namespace Shared.Ui.Common
{
    /// <summary>A small window, on a thread of its own, that says what the app is busy with and for how long. Every load runs on the
    /// UI thread, which draws nothing while it works, so a long load looked like a hang. It shows once the UI thread has left a
    /// question unanswered for half a second while BusyWork names the work, or for a second when nothing does, and goes as soon as it
    /// answers: a dialog shown in the middle of a load keeps the UI thread answering, so the popup never covers one. Over the main
    /// window it sits in the middle of a dark cover the window's size, which takes the clicks made while the window cannot.</summary>
    public static class BusyPopup
    {
        public static readonly TimeSpan NamedDelay = TimeSpan.FromSeconds(0.5);
        public static readonly TimeSpan UnnamedDelay = TimeSpan.FromSeconds(1);
        public const string Unnamed = "Working";
        /// <summary>The cover's opacity over the main window, of 255.</summary>
        public const byte CoverAlpha = 115;

        // Read on the UI thread each time it answers: the theme's colours, and the main window's handle. Where that window is, the
        // popup's thread asks Windows itself, so it follows a window moved or maximised while the UI thread is held.
        record Look(Color Background, Color Border, Color Text, Color Dim, Color Bar, Color BarBack, IntPtr Main);
        static readonly Look Fallback = new(Color.FromRgb(45, 45, 48), Color.FromRgb(52, 148, 225), Colors.White, Color.FromRgb(153, 153, 153),
            Color.FromRgb(52, 148, 225), Color.FromRgb(30, 30, 30), IntPtr.Zero);

        static Dispatcher? _ui;
        static Func<Window?> _main = () => Application.Current?.MainWindow;
        static long _askedAt;
        static volatile Look _look = Fallback;
        static volatile string? _shown;
        static volatile bool _ready;
        static Thread? _thread;

        /// <summary>What the popup says now, or null while it is hidden.</summary>
        public static string? Shown => _shown;

        /// <summary>Whether the popup can show yet: its windows are made and shown once, off screen, when its thread starts.</summary>
        public static bool Ready => _ready;

        /// <summary>The popup's window and the cover's, once Ready.</summary>
        public static IntPtr PopupHandle { get; private set; }
        public static IntPtr CoverHandle { get; private set; }

        /// <summary>Watches the UI thread. Called again, it watches the new one: each STA test runs on a thread of its own.</summary>
        /// <param name="main">The window to cover, read on the UI thread; the application's main window when not given.</param>
        public static void Watch(Dispatcher ui, Func<Window?>? main = null)
        {
            _ui = ui;
            _main = main ?? (() => Application.Current?.MainWindow);
            _look = Fallback;
            Interlocked.Exchange(ref _askedAt, 0);
            if (_thread != null)
                return;
            _thread = new Thread(Run) { IsBackground = true, Name = "BusyPopup" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        static void Run()
        {
            var view = new View();
            // A first window on a new thread takes most of a second to make (theme, fonts, handle): made now, while the app starts,
            // and not when the first load is already that long.
            view.WarmUp();
            _ready = true;
            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal, (_, _) => Tick(view), Dispatcher.CurrentDispatcher);
            timer.Start();
            Dispatcher.Run();
        }

        // One question in flight at a time: the time it was asked, or 0 once the UI thread has answered it.
        static void Tick(View view)
        {
            var ui = _ui;
            if (ui == null || ui.HasShutdownStarted)
            {
                view.Hide();
                return;
            }
            var asked = Interlocked.Read(ref _askedAt);
            if (asked == 0)
            {
                Interlocked.Exchange(ref _askedAt, Stopwatch.GetTimestamp());
                ui.InvokeAsync(Answer, DispatcherPriority.Normal);
                view.Hide();
                return;
            }
            var waited = Stopwatch.GetElapsedTime(asked);
            var text = BusyWork.Current;
            if (waited < (text == null ? UnnamedDelay : NamedDelay))
                view.Hide();
            else
                view.Show(text ?? Unnamed, waited, _look);
        }

        static void Answer()
        {
            _look = Read();
            Interlocked.Exchange(ref _askedAt, 0);
        }

        static Look Read()
        {
            var app = Application.Current;
            Color Colour(string key, Color fallback) => app?.TryFindResource(key) is SolidColorBrush brush ? brush.Color : fallback;
            var main = _main() is { IsVisible: true } window ? new WindowInteropHelper(window).Handle : IntPtr.Zero;
            return new Look(Colour("Window.Static.Background", Fallback.Background), Colour("ABrush.AccentTone1.Border.Static", Fallback.Border),
                Colour("ABrush.Foreground.Static", Fallback.Text), Colour("ABrush.Foreground.Disabled", Fallback.Dim),
                Colour("ProgressBar.Progress", Fallback.Bar), Colour("ProgressBar.Background", Fallback.BarBack), main);
        }

        // The popup and its cover, made on the popup's thread when it starts.
        sealed class View
        {
            Window _window = null!;
            Window _cover = null!;
            Border _frame = null!;
            TextBlock _what = null!;
            TextBlock _seconds = null!;
            ProgressBar _bar = null!;
            Look? _painted;

            public void Show(string text, TimeSpan waited, Look look)
            {
                if (look != _painted)
                    Paint(look);
                _what.Text = text;
                _seconds.Text = $"{waited.TotalSeconds:0} s";
                if (look.Main != IntPtr.Zero && Native.IsIconic(look.Main))
                {
                    // Minimised: there is nothing of the app on screen to cover or to centre on.
                    Hide();
                    return;
                }
                // Held five seconds, the main window is hidden and Windows shows a ghost of it ("Not Responding") in its place.
                var ghost = look.Main == IntPtr.Zero ? IntPtr.Zero : Native.Ghost(look.Main);
                var target = ghost != IntPtr.Zero ? ghost : look.Main;
                if (target != IntPtr.Zero && Native.IsWindowVisible(target) && Native.Frame(target) is { } over)
                {
                    _window.Topmost = false;
                    if (!_cover.IsVisible)
                        _cover.Show();
                    Cover(Native.Highest(target, look.Main), over);
                    if (!_window.IsVisible)
                        _window.Show();
                    Centre(over);
                }
                else
                {
                    // No main window yet (the app is starting): the middle of the screen, above the other apps.
                    if (_cover.IsVisible)
                        _cover.Hide();
                    _window.Topmost = true;
                    var area = SystemParameters.WorkArea;
                    _window.Left = area.Left + (area.Width - _window.ActualWidth) / 2;
                    _window.Top = area.Top + (area.Height - _window.ActualHeight) / 2;
                    if (!_window.IsVisible)
                        _window.Show();
                }
                _shown = text;
            }

            // The cover goes into the z-order directly above the app's highest window: over the frozen window and any panel of the
            // app's on it, and under any app the user has put over them. The popup is owned by the cover, so it stays above it.
            void Cover(IntPtr highest, Native.RECT over)
            {
                var above = Native.GetWindow(highest, Native.GW_HWNDPREV);
                var flags = Native.SWP_NOACTIVATE;
                var after = IntPtr.Zero;
                if (above == CoverHandle)
                    flags |= Native.SWP_NOZORDER;
                else if (above != IntPtr.Zero && (Native.GetWindowLong(above, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) == 0)
                    after = above;
                Native.SetWindowPos(CoverHandle, after, over.Left, over.Top, over.Right - over.Left, over.Bottom - over.Top, flags);
            }

            void Centre(Native.RECT over)
            {
                Native.GetWindowRect(PopupHandle, out var popup);
                var (width, height) = (popup.Right - popup.Left, popup.Bottom - popup.Top);
                Native.SetWindowPos(PopupHandle, IntPtr.Zero, over.Left + (over.Right - over.Left - width) / 2, over.Top + (over.Bottom - over.Top - height) / 2,
                    0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            }

            public void WarmUp()
            {
                Build();
                foreach (var window in new[] { _cover, _window })
                {
                    (window.Left, window.Top) = (-32000, -32000);
                    window.Show();
                    // An owner has to have been shown before it can own.
                    if (window == _cover)
                        _window.Owner = _cover;
                }
                CoverHandle = new WindowInteropHelper(_cover).Handle;
                PopupHandle = new WindowInteropHelper(_window).Handle;
                _window.Hide();
                _cover.Hide();
            }

            public void Hide()
            {
                if (_window.IsVisible)
                    _window.Hide();
                if (_cover.IsVisible)
                    _cover.Hide();
                _shown = null;
            }

            void Build()
            {
                _cover = new Window
                {
                    Title = "AssetEditor",
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = new SolidColorBrush(Color.FromArgb(CoverAlpha, 0, 0, 0)),
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Cursor = Cursors.Wait,
                };
                _cover.SourceInitialized += (_, _) => Native.NoActivate(_cover);

                _what = new TextBlock { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
                _bar = new ProgressBar { IsIndeterminate = true, Height = 4, BorderThickness = new Thickness(0), Margin = new Thickness(0, 10, 0, 8) };
                _seconds = new TextBlock { FontSize = 11 };
                var panel = new StackPanel();
                panel.Children.Add(_what);
                panel.Children.Add(_bar);
                panel.Children.Add(_seconds);
                _frame = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 16, 12), Child = panel };
                _window = new Window
                {
                    Title = "AssetEditor",
                    Content = _frame,
                    Width = 360,
                    SizeToContent = SizeToContent.Height,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Cursor = Cursors.Wait,
                };
                _window.SourceInitialized += (_, _) => Native.NoActivate(_window);
            }

            void Paint(Look look)
            {
                _painted = look;
                _frame.Background = new SolidColorBrush(look.Background);
                _frame.BorderBrush = new SolidColorBrush(look.Border);
                _what.Foreground = new SolidColorBrush(look.Text);
                _seconds.Foreground = new SolidColorBrush(look.Dim);
                _bar.Foreground = new SolidColorBrush(look.Bar);
                _bar.Background = new SolidColorBrush(look.BarBack);
            }
        }

        // Windows' own view of the windows, in screen pixels. None of these sends the held UI thread a message, so none waits on it.
        static class Native
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct RECT { public int Left, Top, Right, Bottom; }

            public const uint GW_HWNDNEXT = 2, GW_HWNDPREV = 3;
            public const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
            public const int GWL_EXSTYLE = -20, WS_EX_TOPMOST = 0x8;
            const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, DWMWA_EXTENDED_FRAME_BOUNDS = 9;

            [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window, uint relation);
            [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr parent);
            [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
            [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rect);
            [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
            [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
            [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
            [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr window, int index);
            [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr window, int index, int value);
            [DllImport("user32.dll")] static extern IntPtr GhostWindowFromHungWindow(IntPtr window);
            [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out RECT value, int size);

            /// <summary>What is drawn of the window: its rectangle less the invisible resize border Windows 10 and 11 give it.</summary>
            public static RECT? Frame(IntPtr window)
            {
                if (DwmGetWindowAttribute(window, DWMWA_EXTENDED_FRAME_BOUNDS, out var frame, Marshal.SizeOf<RECT>()) == 0)
                    return frame;
                return GetWindowRect(window, out var rect) ? rect : null;
            }

            /// <summary>The highest window in the z-order that is the target, or shown by the main window's thread: a panel or dialog of
            /// the app's over the main window, such as the start panel.</summary>
            public static IntPtr Highest(IntPtr target, IntPtr main)
            {
                var thread = GetWindowThreadProcessId(main, out _);
                for (var window = GetTopWindow(IntPtr.Zero); window != IntPtr.Zero; window = GetWindow(window, GW_HWNDNEXT))
                {
                    if (window == target || (IsWindowVisible(window) && GetWindowThreadProcessId(window, out _) == thread))
                        return window;
                }
                return target;
            }

            public static IntPtr Ghost(IntPtr hung)
            {
                try
                {
                    return GhostWindowFromHungWindow(hung);
                }
                catch (EntryPointNotFoundException)
                {
                    return IntPtr.Zero;
                }
            }

            // Clicked, it takes the click without taking the focus from the main window, and it stays out of Alt+Tab.
            public static void NoActivate(Window window)
            {
                var handle = new WindowInteropHelper(window).Handle;
                SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            }
        }
    }
}
