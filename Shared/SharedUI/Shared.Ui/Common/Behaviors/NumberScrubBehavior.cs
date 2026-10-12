using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;

namespace Shared.Ui.Common.Behaviors
{
    public enum ScrubMode { None, Integer, Decimal }

    /// <summary>Photoshop's scrubby slider on a number box: over a box it can change, the mouse shows the left-right arrows; press and
    /// drag sideways and the number moves with the mouse, a hundred pixels by the number's own size (by 1 under 1, and a whole number
    /// at least a step per 10 pixels). Shift moves it ten times as far, Ctrl a tenth as far. A press that does not travel 3 pixels is
    /// a click, and puts the box into typing with its text selected; while it is being typed in, the mouse edits text as usual.</summary>
    public class NumberScrubBehavior : Behavior<TextBox>
    {
        const double ClickTravel = 3;

        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register(nameof(Mode), typeof(ScrubMode), typeof(NumberScrubBehavior), new PropertyMetadata(ScrubMode.Decimal));

        public ScrubMode Mode
        {
            get => (ScrubMode)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }

        double? _pressX;
        double _lastX;
        double _value;
        double _step;
        bool _dragging;

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.PreviewMouseLeftButtonDown += OnDown;
            AssociatedObject.PreviewMouseMove += OnMove;
            AssociatedObject.PreviewMouseLeftButtonUp += OnUp;
            AssociatedObject.LostMouseCapture += OnLostCapture;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.PreviewMouseLeftButtonDown -= OnDown;
            AssociatedObject.PreviewMouseMove -= OnMove;
            AssociatedObject.PreviewMouseLeftButtonUp -= OnUp;
            AssociatedObject.LostMouseCapture -= OnLostCapture;
            base.OnDetaching();
        }

        bool CanScrub(out double value)
        {
            value = 0;
            var box = AssociatedObject;
            return Mode != ScrubMode.None && box.IsEnabled && !box.IsReadOnly && !box.IsKeyboardFocusWithin
                && double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        /// <summary>The mouse goes down at x across the box; false when the box does not scrub, and the press is left to it.</summary>
        public bool Begin(double x)
        {
            if (!CanScrub(out _value))
                return false;
            _step = Math.Max(Mode == ScrubMode.Integer ? 0.1 : 0.01, Math.Abs(_value) * 0.01);
            (_pressX, _lastX, _dragging) = (x, x, false);
            return true;
        }

        /// <summary>The mouse is at x with these keys held.</summary>
        public void Move(double x, ModifierKeys keys)
        {
            if (_pressX is not { } press || (!_dragging && Math.Abs(x - press) < ClickTravel))
                return;
            _dragging = true;
            var scale = keys.HasFlag(ModifierKeys.Shift) ? 10 : keys.HasFlag(ModifierKeys.Control) ? 0.1 : 1;
            _value += (x - _lastX) * _step * scale;
            _lastX = x;
            AssociatedObject.Text = _value.ToString(Mode == ScrubMode.Integer ? "0" : "0.####", CultureInfo.CurrentCulture);
        }

        /// <summary>The mouse comes up: a click, not a drag, starts typing in the box.</summary>
        public void End()
        {
            if (_pressX != null && !_dragging)
            {
                AssociatedObject.Focus();
                AssociatedObject.SelectAll();
            }
            _pressX = null;
        }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (!Begin(e.GetPosition(AssociatedObject).X))
                return;
            AssociatedObject.CaptureMouse();
            e.Handled = true;
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            if (_pressX == null)
            {
                AssociatedObject.Cursor = CanScrub(out _) ? Cursors.SizeWE : null;
                return;
            }
            Move(e.GetPosition(AssociatedObject).X, Keyboard.Modifiers);
            e.Handled = true;
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (_pressX == null)
                return;
            e.Handled = true;
            // Before letting go: letting go raises LostMouseCapture, which forgets the press, and a click would not start typing.
            End();
            AssociatedObject.ReleaseMouseCapture();
        }

        // Another window took the mouse mid-drag: the number stays where it got to.
        void OnLostCapture(object sender, MouseEventArgs e) => _pressX = null;
    }
}
