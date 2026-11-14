using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Editors.AnimationMeta.SuperView
{
    // The strip is also the clip's slider. A press away from a tick scrubs at once; a press on a tick selects its tag on
    // release, unless the mouse moves far enough to count as a drag first, which turns it into a scrub too.
    public partial class MetaTimelineView : UserControl
    {
        Point? _pressOnTick;
        bool _scrubbing;
        SuperViewViewModel? _viewModel;

        public MetaTimelineView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => Watch(DataContext as SuperViewViewModel);
            Ticks.SizeChanged += (_, _) => PlacePlayhead();
        }

        void Watch(SuperViewViewModel? viewModel)
        {
            if (_viewModel != null)
                _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel = viewModel;
            if (_viewModel != null)
                _viewModel.PropertyChanged += OnViewModelChanged;
            PlacePlayhead();
        }

        void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SuperViewViewModel.PlayheadFraction))
                PlacePlayhead();
        }

        // Placed as MetaTimelinePanel places a tick's line, so the playhead covers a tick at the same time exactly.
        void PlacePlayhead()
        {
            var width = Ticks.ActualWidth;
            PlayheadShift.X = Math.Clamp((_viewModel?.PlayheadFraction ?? 0) * width - MetaTimelinePanel.Line / 2, 0, Math.Max(0, width - MetaTimelinePanel.Line));
        }

        /// <summary>Moves the clip to a point on the strip, in the strip's own pixels.</summary>
        public void Scrub(double x)
        {
            if (Ticks.ActualWidth > 0)
                _viewModel?.ScrubTo(x / Ticks.ActualWidth);
        }

        void OnPress(object sender, MouseButtonEventArgs e)
        {
            if (IsOnTick(e.OriginalSource as DependencyObject))
            {
                _pressOnTick = e.GetPosition(Ticks);
                return;
            }
            StartScrub(e.GetPosition(Ticks).X);
            e.Handled = true;
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            var x = e.GetPosition(Ticks).X;
            if (_scrubbing)
                Scrub(x);
            else if (_pressOnTick is { } start && e.LeftButton == MouseButtonState.Pressed
                     && Math.Abs(x - start.X) >= SystemParameters.MinimumHorizontalDragDistance)
                StartScrub(x);
        }

        void OnRelease(object sender, MouseButtonEventArgs e)
        {
            _pressOnTick = null;
            if (!_scrubbing)
                return;
            Strip.ReleaseMouseCapture();
            e.Handled = true;
        }

        // Taking the mouse from a pressed tick cancels its click. That tick's own lost capture bubbles here too, so only the
        // strip's ends a scrub.
        void OnLostCapture(object sender, MouseEventArgs e)
        {
            if (e.OriginalSource == Strip)
                _scrubbing = false;
        }

        void StartScrub(double x)
        {
            _pressOnTick = null;
            Strip.CaptureMouse();
            _scrubbing = true;
            Scrub(x);
        }

        static bool IsOnTick(DependencyObject? element)
        {
            for (; element != null; element = element is Visual ? VisualTreeHelper.GetParent(element) : null)
            {
                if (element is ButtonBase)
                    return true;
            }
            return false;
        }
    }
}
