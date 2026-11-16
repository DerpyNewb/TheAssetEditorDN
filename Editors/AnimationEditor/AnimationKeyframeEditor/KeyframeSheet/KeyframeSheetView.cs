using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Shared.Ui.Common;

namespace Editors.AnimationVisualEditors.AnimationKeyframeEditor
{
    /// <summary>
    /// Draws the Keyframes strip and hands clicks and keys to <see cref="KeyframeSheetViewModel"/>, its DataContext. The ruler
    /// scrubs; a click or drag over the cells selects them, Shift extends; a row's name selects the whole row.
    /// </summary>
    public class KeyframeSheetView : FrameworkElement
    {
        const double StatusHeight = 20;

        enum Drag { None, Scrub, Cells, Rows }

        KeyframeSheetViewModel? _sheet;
        Drag _drag;

        public KeyframeSheetView()
        {
            Focusable = true;
            FocusVisualStyle = null;
            ClipToBounds = true;
            DataContextChanged += (_, e) => Attach(e.NewValue as KeyframeSheetViewModel);
            ContextMenu = new ContextMenu();
            Item("AnimationKeyframeEditor.Sheet.Copy", "Ctrl+C", () => _sheet?.Copy());
            Item("AnimationKeyframeEditor.Sheet.Paste", "Ctrl+V", () => _sheet?.Paste());
            Item("AnimationKeyframeEditor.Sheet.PasteFlipped", "Ctrl+Shift+V", () => _sheet?.PasteFlipped());
            ContextMenu.Items.Add(new Separator());
            Item("AnimationKeyframeEditor.Sheet.SelectAll", "Ctrl+A", () => _sheet?.SelectAll());
            Item("AnimationKeyframeEditor.Sheet.ClearSelection", "Esc", () => _sheet?.ClearSelection());
        }

        void Item(string key, string gesture, Action action)
        {
            var item = new MenuItem { Header = (string)new LocExtension(key).ProvideValue(null!), InputGestureText = gesture };
            item.Click += (_, _) => action();
            ContextMenu.Items.Add(item);
        }

        void Attach(KeyframeSheetViewModel? sheet)
        {
            if (_sheet != null)
            {
                _sheet.Changed -= InvalidateVisual;
                _sheet.PropertyChanged -= OnSheetPropertyChanged;
            }
            _sheet = sheet;
            if (_sheet != null)
            {
                _sheet.Changed += InvalidateVisual;
                _sheet.PropertyChanged += OnSheetPropertyChanged;
            }
            InvalidateVisual();
        }

        void OnSheetPropertyChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

        KeyframeSheetLayout Layout() => KeyframeSheetLayout.Fit(ActualWidth, _sheet?.FrameCount ?? 0);

        Brush Brush(string key, Color fallback) => TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

        protected override void OnRender(DrawingContext dc)
        {
            var width = ActualWidth;
            var height = ActualHeight;
            var background = Brush("ABrush.Tone2.Background.Static", Color.FromRgb(0x25, 0x25, 0x26));
            var header = Brush("ABrush.Tone3.Background.Static", Color.FromRgb(0x2d, 0x2d, 0x30));
            var stripe = Brush("ABrush.Tone4.Background.Static", Color.FromRgb(0x33, 0x33, 0x37));
            var text = Brush("ABrush.Foreground.Static", Colors.Gainsboro);
            var dim = Brush("ABrush.Foreground.Deeper", Colors.Gray);
            var accent = Brush("ABrush.AccentTone1.Background.Static", Color.FromRgb(0x00, 0x7a, 0xcc));
            // As in Blender: selected keys orange, the rest a dimmed grey. The theme's own two glyph colours were too alike.
            var selectedKey = Brushes.Orange;
            var key = dim.Clone();
            key.Opacity = 0.55;
            var line = new Pen(Brush("ABrush.Tone5.Border.Static", Color.FromRgb(0x3f, 0x3f, 0x46)), 1);

            dc.DrawRectangle(background, null, new Rect(0, 0, width, height));
            var sheet = _sheet;
            if (sheet == null || sheet.FrameCount == 0)
            {
                dc.DrawText(Text("Load an animation to see its frames here.", dim), new Point(8, 6));
                return;
            }

            var layout = Layout();
            var frames = sheet.FrameCount;
            var rows = sheet.Rows;
            var bottom = height - StatusHeight;
            var right = Math.Min(width, layout.FrameLeft(frames));

            // Rows, their names, and the selection under the keys.
            for (var row = 0; row < rows.Count; row++)
            {
                var top = layout.RowTop(row);
                if (top >= bottom)
                    break;
                if (row % 2 == 1)
                    dc.DrawRectangle(stripe, null, new Rect(KeyframeSheetLayout.HeaderWidth, top, right - KeyframeSheetLayout.HeaderWidth, KeyframeSheetLayout.RowHeight));
                dc.DrawRectangle(header, null, new Rect(0, top, KeyframeSheetLayout.HeaderWidth, KeyframeSheetLayout.RowHeight));
                dc.DrawText(Text(rows[row].Name, text, KeyframeSheetLayout.HeaderWidth - 10), new Point(6, top + 3));
            }
            if (sheet.Selection is { } s)
            {
                var top = layout.RowTop(s.FirstRow);
                var area = new Rect(layout.FrameLeft(s.FirstFrame), top, layout.FrameLeft(s.LastFrame + 1) - layout.FrameLeft(s.FirstFrame),
                    Math.Max(0, Math.Min(bottom, layout.RowTop(s.LastRow + 1)) - top));
                var fill = accent.Clone();
                fill.Opacity = 0.35;
                dc.DrawRectangle(fill, new Pen(accent, 1), area);
                var names = new Rect(0, top, KeyframeSheetLayout.HeaderWidth, area.Height);
                dc.DrawRectangle(fill, null, names);
            }

            // Every frame of a .anim is a key, so every cell has one.
            var half = Math.Min(4.5, layout.FrameWidth * 0.4);
            for (var row = 0; row < rows.Count && layout.RowTop(row) < bottom; row++)
            {
                var y = layout.RowTop(row) + KeyframeSheetLayout.RowHeight / 2;
                for (var frame = 0; frame < frames; frame++)
                    Diamond(dc, layout.FrameCentre(frame), y, half, sheet.IsSelected(row, frame) ? selectedKey : key);
            }

            // The ruler: a number every few frames, as many as fit.
            dc.DrawRectangle(header, null, new Rect(0, 0, width, KeyframeSheetLayout.RulerHeight));
            var step = 1;
            foreach (var candidate in new[] { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000 })
            {
                step = candidate;
                if (candidate * layout.FrameWidth >= 28)
                    break;
            }
            for (var frame = 0; frame < frames; frame += step)
            {
                var x = layout.FrameCentre(frame);
                dc.DrawLine(line, new Point(x, KeyframeSheetLayout.RulerHeight - 5), new Point(x, KeyframeSheetLayout.RulerHeight));
                var label = Text(frame.ToString(CultureInfo.InvariantCulture), dim);
                dc.DrawText(label, new Point(x - label.Width / 2, 3));
            }
            dc.DrawLine(line, new Point(0, KeyframeSheetLayout.RulerHeight), new Point(width, KeyframeSheetLayout.RulerHeight));
            dc.DrawLine(line, new Point(KeyframeSheetLayout.HeaderWidth, 0), new Point(KeyframeSheetLayout.HeaderWidth, bottom));

            // The current frame: a line through every row, its number in a box on the ruler.
            var current = sheet.CurrentFrame;
            var cx = layout.FrameCentre(current);
            dc.DrawLine(new Pen(accent, 2), new Point(cx, KeyframeSheetLayout.RulerHeight), new Point(cx, bottom));
            var number = Text(current.ToString(CultureInfo.InvariantCulture), text);
            var box = new Rect(cx - number.Width / 2 - 4, 2, number.Width + 8, KeyframeSheetLayout.RulerHeight - 4);
            dc.DrawRoundedRectangle(accent, null, box, 3, 3);
            dc.DrawText(number, new Point(box.Left + 4, 3));

            dc.DrawRectangle(header, null, new Rect(0, bottom, width, StatusHeight));
            dc.DrawText(Text(sheet.Status, text, width - 12), new Point(6, bottom + 3));
        }

        static void Diamond(DrawingContext dc, double x, double y, double half, Brush brush)
        {
            if (half < 1)
                return;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(x, y - half), true, true);
                g.LineTo(new Point(x + half, y), false, false);
                g.LineTo(new Point(x, y + half), false, false);
                g.LineTo(new Point(x - half, y), false, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(brush, null, geometry);
        }

        FormattedText Text(string value, Brush brush, double maxWidth = 0)
        {
            var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 12, brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            if (maxWidth > 0)
            {
                text.MaxTextWidth = maxWidth;
                text.MaxLineCount = 1;
                text.Trimming = TextTrimming.CharacterEllipsis;
            }
            return text;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Focus();
            if (_sheet == null || _sheet.FrameCount == 0)
                return;
            var p = e.GetPosition(this);
            var layout = Layout();
            var row = layout.RowAt(p.Y);
            var frame = layout.FrameAt(p.X, _sheet.FrameCount);
            var extend = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (p.Y >= ActualHeight - StatusHeight)
                return;
            if (row == KeyframeSheetLayout.Ruler)
            {
                _drag = Drag.Scrub;
                _sheet.CurrentFrame = Math.Max(0, frame);
            }
            else if (row >= _sheet.Rows.Count)
            {
                _sheet.ClearSelection();
                return;
            }
            else if (frame == KeyframeSheetLayout.Header)
            {
                _drag = Drag.Rows;
                _sheet.SelectRow(row, extend);
            }
            else
            {
                _drag = Drag.Cells;
                _sheet.Select(row, frame, extend);
            }
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drag == Drag.None || _sheet == null)
                return;
            var p = e.GetPosition(this);
            var layout = Layout();
            var row = Math.Max(0, layout.RowAt(p.Y));
            var frame = Math.Max(0, layout.FrameAt(Math.Max(p.X, KeyframeSheetLayout.HeaderWidth), _sheet.FrameCount));
            switch (_drag)
            {
                case Drag.Scrub:
                    if (frame != _sheet.CurrentFrame)
                        _sheet.CurrentFrame = frame;
                    break;
                case Drag.Cells:
                    _sheet.Select(row, frame, extend: true);
                    break;
                case Drag.Rows:
                    _sheet.SelectRow(row, extend: true);
                    break;
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            _drag = Drag.None;
            ReleaseMouseCapture();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e) => _drag = Drag.None;

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (_sheet == null)
                return;
            var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            e.Handled = true;
            switch (e.Key)
            {
                case Key.C when ctrl: _sheet.Copy(); break;
                case Key.V when ctrl && shift: _sheet.PasteFlipped(); break;
                case Key.V when ctrl: _sheet.Paste(); break;
                case Key.A when ctrl: _sheet.SelectAll(); break;
                case Key.Escape: _sheet.ClearSelection(); break;
                case Key.Left: _sheet.CurrentFrame -= 1; break;
                case Key.Right: _sheet.CurrentFrame += 1; break;
                case Key.Home: _sheet.CurrentFrame = 0; break;
                case Key.End: _sheet.CurrentFrame = _sheet.FrameCount - 1; break;
                default: e.Handled = false; break;
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            InvalidateVisual();
        }
    }
}
