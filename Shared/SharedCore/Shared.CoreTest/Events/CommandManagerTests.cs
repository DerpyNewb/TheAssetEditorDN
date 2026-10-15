using Moq;
using Shared.Core.Events;
using Test.TestingUtility.TestUtility;

namespace Shared.CoreTest.Events
{
    // A step that threw stayed on the undo stack, so undo took back what it had never done: undoing a divide that failed before
    // removing its mesh put a second copy of the mesh in the scene.
    public class CommandManagerTests
    {
        class Step(Action execute, Action? undo = null) : IAeUndoCommandCommand
        {
            public string HintText => "step";
            public bool IsMutation => true;
            public void Execute() => execute();
            public void Undo() => (undo ?? (() => { }))();
        }

        static CommandManager Create() => new(new Mock<IEventHub>().Object, MockScopedLogger.Create());

        [Test]
        public void AStepThatThrows_IsNotUndone()
        {
            var commands = Create();
            var undone = 0;
            commands.ExecuteCommand(new Step(() => { }, () => undone++));

            commands.ExecuteCommand(new Step(() => throw new InvalidOperationException(), () => undone += 100));
            commands.Undo();

            Assert.That((undone, commands.CanUndo()), Is.EqualTo((1, false)), "the undo went to the step before it");
        }

        [Test]
        public void AStepThatThrows_LeavesRedoAlone()
        {
            var commands = Create();
            var redone = 0;
            commands.ExecuteCommand(new Step(() => redone++));
            commands.Undo();

            commands.ExecuteCommand(new Step(() => throw new InvalidOperationException()));
            commands.Redo();

            Assert.That(redone, Is.EqualTo(2), "nothing happened, so the undone step can still come back");
        }

        [Test]
        public void AnUndoThatThrows_CannotBeRedone()
        {
            var commands = Create();
            commands.ExecuteCommand(new Step(() => { }, () => throw new InvalidOperationException()));

            commands.Undo();

            Assert.That((commands.CanUndo(), commands.CanRedo()), Is.EqualTo((false, false)), "redoing over a half-undone step would apply it twice");
        }
    }
}
