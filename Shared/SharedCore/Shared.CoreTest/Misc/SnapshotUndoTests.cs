using Shared.Core.Misc;

namespace Shared.CoreTest.Misc
{
    // Undo by whole snapshots: a checkpoint with nothing changed is no step, a change not yet checkpointed is what Undo takes
    // back, a new edit drops the redo steps, a rebase is not a step, and restore is told the snapshot the step was made in.
    public class SnapshotUndoTests
    {
        [Test]
        public void CheckpointUndoRedo()
        {
            var doc = "a";
            var madeIn = new List<string>();
            var history = new SnapshotUndo<string>(() => doc, (x, y) => x == y, (load, made) => { doc = load; madeIn.Add(made); });
            history.Reset();

            Assert.That(history.Checkpoint(), Is.False, "nothing changed");
            doc = "b";
            Assert.That(history.Checkpoint(), Is.True);
            doc = "c";
            Assert.That((history.Undo(), doc), Is.EqualTo((true, "b")), "the change not yet checkpointed is the one undone");
            Assert.That((history.Undo(), doc), Is.EqualTo((true, "a")));
            Assert.That((history.Undo(), doc, history.CanUndo), Is.EqualTo((false, "a", false)));
            Assert.That((history.Redo(), doc), Is.EqualTo((true, "b")));
            Assert.That(madeIn, Is.EqualTo(new[] { "c", "b", "b" }), "an undo is told the snapshot it leaves, a redo the one it reaches");

            doc = "d";
            history.Checkpoint();
            Assert.That((history.CanRedo, history.Redo(), doc), Is.EqualTo((false, false, "d")), "a new edit drops the redo steps");

            doc = "saved d";
            history.Rebase();
            Assert.That(history.Checkpoint(), Is.False, "a rebase is not a step");
            Assert.That((history.Undo(), doc), Is.EqualTo((true, "b")));
        }

        // A restore that sets off a checkpoint of its own, through a change event the editor checkpoints on, records nothing.
        [Test]
        public void ACheckpointDuringARestore_RecordsNothing()
        {
            var doc = "a";
            SnapshotUndo<string> history = null!;
            history = new(() => doc, (x, y) => x == y, (load, _) => { doc = load; history.Checkpoint(); });
            history.Reset();
            doc = "b";
            history.Checkpoint();

            Assert.That((history.Undo(), doc, history.CanRedo), Is.EqualTo((true, "a", true)));
            Assert.That((history.Redo(), doc, history.CanUndo), Is.EqualTo((true, "b", true)));
        }
    }
}
