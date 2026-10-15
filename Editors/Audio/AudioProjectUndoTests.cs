using System.Text.Json;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Events;
using Editors.Audio.Shared.AudioProject.Models;
using Moq;
using Shared.Core.Events;

namespace Test.Audio
{
    // The Audio editor had no undo: a removed or pasted row was taken back only by reopening the project.
    public class AudioProjectUndoTests
    {
        static string Json(AudioProjectFile project) => JsonSerializer.Serialize(project);

        // The undo keeps the project as JSON, so the whole working project has to come back from it exactly.
        [Test]
        public void AWorkingProject_RoundTripsThroughItsSnapshot()
        {
            var project = AudioProjectFile.CreateNew("english(uk)", "derpy");
            project.SoundBanks[0].ActionEvents.Add(new ActionEvent(1, "play_derpy", [], default));

            Assert.That(Json(JsonSerializer.Deserialize<AudioProjectFile>(Json(project))!), Is.EqualTo(Json(project)));
        }

        [Test]
        public void UndoAndRedo_PutTheProjectBackAndForth_AndShowItAgain()
        {
            var state = new AudioEditorStateService();
            state.StoreAudioProject(AudioProjectFile.CreateNew("english(uk)", "derpy"));
            var events = new Mock<IEventHub>();
            var undo = new AudioProjectUndoService(state, events.Object);
            var before = Json(state.AudioProject);

            undo.BeforeChange();
            state.AudioProject.SoundBanks[0].ActionEvents.Add(new ActionEvent(1, "play_derpy", [], default));
            undo.BeforeChange();
            state.AudioProject.StateGroups.RemoveAt(0);
            var after = Json(state.AudioProject);

            Assert.That(undo.Undo() && undo.Undo(), Is.True);
            Assert.That(Json(state.AudioProject), Is.EqualTo(before));
            Assert.That(undo.Undo(), Is.False, "two changes, two steps");
            events.Verify(x => x.Publish(It.IsAny<AudioProjectLoadedEvent>()), Times.Exactly(2), "the views are rebuilt from the project put back");

            undo.Redo();
            undo.Redo();
            Assert.That(Json(state.AudioProject), Is.EqualTo(after));
        }

        // An edit removes the row, which is itself a change: the two calls make one step.
        [Test]
        public void AChangeInsideAChange_IsOneStep()
        {
            var state = new AudioEditorStateService();
            state.StoreAudioProject(AudioProjectFile.CreateNew("english(uk)", "derpy"));
            var undo = new AudioProjectUndoService(state, new Mock<IEventHub>().Object);
            var before = Json(state.AudioProject);

            undo.BeforeChange();
            undo.BeforeChange();
            state.AudioProject.StateGroups.RemoveAt(0);

            undo.Undo();
            Assert.That((Json(state.AudioProject), undo.Undo()), Is.EqualTo((before, false)));
        }
    }
}
