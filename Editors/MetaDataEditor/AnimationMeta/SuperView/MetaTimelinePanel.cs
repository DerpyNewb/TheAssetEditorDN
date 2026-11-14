using System.Windows;
using System.Windows.Controls;

namespace Editors.AnimationMeta.SuperView
{
    /// <summary>Lays SuperView's timeline ticks out by time. Each child is a tick's button, whose mark is a 2 px line centred on
    /// the tag's start that runs on to its end; the button reaches <see cref="Pad"/> past the mark on each side, the margin its
    /// template keeps clear, so a thin tick is still easy to click. A mark at either edge is moved inside the strip.</summary>
    public class MetaTimelinePanel : Panel
    {
        public const double Pad = 3;
        public const double Line = 2;

        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (UIElement child in InternalChildren)
                child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            return new Size(0, 0);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var width = finalSize.Width;
            foreach (UIElement child in InternalChildren)
            {
                if ((child as FrameworkElement)?.DataContext is not TimelineTick tick)
                    continue;
                var x = Math.Clamp(tick.From * width - Line / 2, 0, Math.Max(0, width - Line));
                var markWidth = Math.Clamp(tick.To * width - x, Line, Math.Max(Line, width - x));
                child.Arrange(new Rect(x - Pad, 0, markWidth + 2 * Pad, finalSize.Height));
            }
            return finalSize;
        }
    }
}
