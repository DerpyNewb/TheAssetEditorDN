using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Shared.Core.Misc;

namespace Shared.Ui.Common
{
    /// <summary>A small window, on a thread of its own, that says what the app is busy with and for how long. Every load runs on the
    /// UI thread, which draws nothing while it works, so a long load looked like a hang. It shows once the UI thread has left a
    /// question unanswered for half a second while BusyWork names the work, or for a second when nothing does, and goes as soon as it
    /// answers: a dialog shown in the middle of a load keeps the UI thread answering, so the popup never covers one.</summary>
    public static class BusyPopup
    {
        public static readonly TimeSpan NamedDelay = TimeSpan.FromSeconds(0.5);
        public static readonly TimeSpan UnnamedDelay = TimeSpan.FromSeconds(1);
        public const string Unnamed = "Working";

        // Read on the UI thread each time it answers: the theme's colours, and the main window to centre on.
        record Look(Color Background, Color Border, Color Text, Color Dim, Color Bar, Color BarBack, Rect? Over);
        static readonly Look Fallback = new(Color.FromRgb(45, 45, 48), Color.FromRgb(52, 148, 225), Colors.White, Color.FromRgb(153, 153, 153),
            Color.FromRgb(52, 148, 225), Color.FromRgb(30, 30, 30), null);

        static Dispatcher? _ui;
        static long _askedAt;
        static volatile Look _look = Fallback;
        static volatile string? _shown;
        static volatile bool _ready;
        static Thread? _thread;

        /// <summary>What the popup says now, or null while it is hidden.</summary>
        public static string? Shown => _shown;

        /// <summary>Whether the popup can show yet: its window is made and shown once, off screen, when its thread starts.</summary>
        public static bool Ready => _ready;

        /// <summary>Watches the UI thread. Called again, it watches the new one: each STA test runs on a thread of its own.</summary>
        public static void Watch(Dispatcher ui)
        {
            _ui = ui;
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
            if (app == null)
                return Fallback;
            Color Colour(string key, Color fallback) => app.TryFindResource(key) is SolidColorBrush brush ? brush.Color : fallback;
            Rect? over = null;
            if (app.MainWindow is { IsVisible: true } main)
            {
                // ponytail: a maximised window is centred on as it was before, which is on its own screen but not its middle.
                var bounds = main.WindowState == WindowState.Normal ? new Rect(main.Left, main.Top, main.ActualWidth, main.ActualHeight) : main.RestoreBounds;
                over = bounds.IsEmpty ? null : bounds;
            }
            return new Look(Colour("Window.Static.Background", Fallback.Background), Colour("ABrush.AccentTone1.Border.Static", Fallback.Border),
                Colour("ABrush.Foreground.Static", Fallback.Text), Colour("ABrush.Foreground.Disabled", Fallback.Dim),
                Colour("ProgressBar.Progress", Fallback.Bar), Colour("ProgressBar.Background", Fallback.BarBack), over);
        }

        // The window, made on the popup's thread the first time it shows.
        sealed class View
        {
            Window? _window;
            Border? _frame;
            TextBlock? _what;
            TextBlock? _seconds;
            ProgressBar? _bar;
            Look? _painted;

            public void Show(string text, TimeSpan waited, Look look)
            {
                if (_window == null)
                    Build();
                if (look != _painted)
                    Paint(look);
                _what!.Text = text;
                _seconds!.Text = $"{waited.TotalSeconds:0} s";
                var area = look.Over ?? SystemParameters.WorkArea;
                _window!.Left = area.Left + (area.Width - _window.Width) / 2;
                _window.Top = area.Top + (area.Height - (_window.ActualHeight > 0 ? _window.ActualHeight : 90)) / 2;
                if (!_window.IsVisible)
                    _window.Show();
                _shown = text;
            }

            public void WarmUp()
            {
                Build();
                (_window!.Left, _window.Top) = (-32000, -32000);
                _window.Show();
                _window.Hide();
            }

            public void Hide()
            {
                if (_window is { IsVisible: true })
                    _window.Hide();
                _shown = null;
            }

            void Build()
            {
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
                    // ponytail: above every app while it shows, as it has no owner: an owner on the busy thread would tie its input to
                    // that thread. Hide it while another app has the focus if that ever grates.
                    Topmost = true,
                };
            }

            void Paint(Look look)
            {
                _painted = look;
                _frame!.Background = new SolidColorBrush(look.Background);
                _frame.BorderBrush = new SolidColorBrush(look.Border);
                _what!.Foreground = new SolidColorBrush(look.Text);
                _seconds!.Foreground = new SolidColorBrush(look.Dim);
                _bar!.Foreground = new SolidColorBrush(look.Bar);
                _bar.Background = new SolidColorBrush(look.BarBack);
            }
        }
    }
}
