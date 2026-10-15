using Moq;
using Shared.Core.Events;
using Shared.Ui.Common;
using Shared.Ui.Common.Behaviors;
using Test.TestingUtility.TestUtility;

namespace Shared.UiTest.Common
{
    internal class ValueUndoTests
    {
        CommandManager _commands = null!;
        ValueUndo _undo = null!;
        float _value;
        readonly List<float> _set = [];

        [SetUp]
        public void Setup()
        {
            _commands = new CommandManager(new Mock<IEventHub>().Object, MockScopedLogger.Create());
            _undo = new ValueUndo(_commands);
            _value = 1;
            _set.Clear();
        }

        // What a panel's field does: it takes the value, and reports the change, which its undo sets back through the field.
        void Edit(float value, string key = "strength")
        {
            var old = _value;
            _value = value;
            _undo.Changed(key, old, value, v => { _set.Add(v); Edit(v, key); }, key);
        }

        static void Drag(Action steps)
        {
            ScrubGesture.Start();
            try { steps(); }
            finally { ScrubGesture.Stop(); }
        }

        [Test]
        public void AWholeDrag_UndoesInOneStep_ToWhereItStarted()
        {
            Drag(() => { Edit(2); Edit(3); Edit(4); });

            _commands.Undo();

            Assert.That((_value, _commands.CanUndo()), Is.EqualTo((1f, false)));
            Assert.That(_set, Is.EqualTo(new[] { 2f, 1f }), "the first edit set again as it goes on the stack, then back on undo; the undo's own set recorded nothing");
        }

        [Test]
        public void TwoDrags_AreTwoSteps()
        {
            Drag(() => { Edit(2); Edit(3); });
            Drag(() => { Edit(5); Edit(6); });

            _commands.Undo();
            Assert.That(_value, Is.EqualTo(3));
            _commands.Undo();
            Assert.That((_value, _commands.CanUndo()), Is.EqualTo((1f, false)));
        }

        [Test]
        public void TwoValuesInOneRun_AreTwoSteps()
        {
            Drag(() => { Edit(2, "a"); Edit(3, "b"); });

            _commands.Undo();
            _commands.Undo();
            Assert.That(_commands.CanUndo(), Is.False);
        }

        [Test]
        public void AnEditThatChangesNothing_IsNoStep()
        {
            Edit(1);
            Assert.That(_commands.CanUndo(), Is.False);
        }

        [Test]
        public void OutOfADrag_EditsUnderASecondApart_AreOneStep_AndOneLater_IsANewOne()
        {
            var coalescer = new EditCoalescer();
            var t = new DateTime(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

            Assert.That(coalescer.Continues("x", t), Is.False, "the first");
            Assert.That(coalescer.Continues("x", t.AddMilliseconds(500)), Is.True, "half a second on");
            Assert.That(coalescer.Continues("x", t.AddSeconds(2)), Is.False, "a pause");
            Assert.That(coalescer.Continues("y", t.AddSeconds(2.1)), Is.False, "another value");
        }
    }
}
