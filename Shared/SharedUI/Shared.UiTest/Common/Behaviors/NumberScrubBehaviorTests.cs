using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;
using Shared.Ui.Common.Behaviors;

namespace Shared.UiTest.Common.Behaviors
{
    // Photoshop's scrubby slider on a number box: press on it, drag sideways, and the number follows the mouse.
    [Apartment(ApartmentState.STA)]
    internal class NumberScrubBehaviorTests
    {
        static (TextBox Box, NumberScrubBehavior Scrub) Box(string text, ScrubMode mode = ScrubMode.Decimal)
        {
            var box = new TextBox { Text = text };
            var scrub = new NumberScrubBehavior { Mode = mode };
            Interaction.GetBehaviors(box).Add(scrub);
            return (box, scrub);
        }

        static string Drag(string text, double pixels, ScrubMode mode = ScrubMode.Decimal, ModifierKeys keys = ModifierKeys.None)
        {
            var (box, scrub) = Box(text, mode);
            if (scrub.Begin(0))
            {
                scrub.Move(pixels, keys);
                scrub.End();
            }
            return box.Text;
        }

        // A hundred pixels to the right adds the number's own size, to the left takes it away; a number under 1 moves as 1 does.
        [TestCase("1.65", 100, "3.3")]
        [TestCase("1.65", -50, "0.825")]
        [TestCase("129.6", 10, "142.56")]
        [TestCase("0", 100, "1")]
        [TestCase("-2", 100, "0")]
        public void Drag_MovesTheNumberByHowFarTheMouseGoes(string start, double pixels, string expected) =>
            Assert.That(Drag(start, pixels), Is.EqualTo(expected));

        // Shift drags ten times as far per pixel, Ctrl a tenth as far.
        [TestCase(ModifierKeys.Shift, "18.15")]
        [TestCase(ModifierKeys.Control, "1.815")]
        public void Drag_ShiftIsCoarse_CtrlIsFine(ModifierKeys keys, string expected) =>
            Assert.That(Drag("1.65", 100, keys: keys), Is.EqualTo(expected));

        // A whole number stays whole, and moves at least a step per 10 pixels.
        [TestCase("10", 24, "12")]
        [TestCase("0", 30, "3")]
        [TestCase("500", 10, "550")]
        public void Drag_AWholeNumberStaysWhole(string start, double pixels, string expected) =>
            Assert.That(Drag(start, pixels, ScrubMode.Integer), Is.EqualTo(expected));

        // Under 3 pixels the press is a click, not a drag: the number stays as it was.
        [Test]
        public void Drag_ACoupleOfPixelsIsAClick() => Assert.That(Drag("1.65", 2), Is.EqualTo("1.65"));

        // A text field and a read-only one do not scrub.
        [Test]
        public void Drag_OnlyAnEditableNumberScrubs()
        {
            var (readOnly, scrub) = Box("1.65");
            readOnly.IsReadOnly = true;
            Assert.Multiple(() =>
            {
                Assert.That(Drag("1.65", 100, ScrubMode.None), Is.EqualTo("1.65"));
                Assert.That(scrub.Begin(0), Is.False);
                Assert.That(Drag("Hello", 100), Is.EqualTo("Hello"));
            });
        }

        class Holder { public string Value { get; set; } = "1.65"; }

        // The number reaches what the box is bound to while the mouse is still down, so whatever shows it follows the drag.
        [Test]
        public void Drag_UpdatesTheBoundValueAsItGoes()
        {
            var holder = new Holder();
            var (box, scrub) = Box("");
            box.SetBinding(TextBox.TextProperty, new Binding(nameof(Holder.Value)) { Source = holder, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            scrub.Begin(0);
            scrub.Move(100, ModifierKeys.None);
            Assert.That(holder.Value, Is.EqualTo("3.3"));
        }

        // The number reads and writes in the user's own culture, as the box's binding parses it: a German comma stays a comma.
        [Test]
        public void Drag_KeepsTheUsersDecimalSeparator()
        {
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.That(Drag("1,65", 100), Is.EqualTo("3,3"));
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }
        }

        // Over a box it can scrub the mouse shows the left-right arrows; over a read-only box, the usual cursor.
        [TestCase(false, ScrubMode.Decimal, true)]
        [TestCase(true, ScrubMode.Decimal, false)]
        [TestCase(false, ScrubMode.None, false)]
        public void Hover_ShowsTheLeftRightArrowsWhereItScrubs(bool readOnly, ScrubMode mode, bool arrows)
        {
            var (box, _) = Box("1.65", mode);
            box.IsReadOnly = readOnly;
            box.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent });
            Assert.That(box.Cursor, arrows ? Is.EqualTo(Cursors.SizeWE) : Is.Null);
        }
    }
}
