using System.Windows.Input;
using Shared.Core.Events;
using Shared.Core.Settings;
using Shared.Ui.Common.MenuSystem;

namespace Shared.UiTest.Common
{
    // Settings, Hotkeys: the settings file keeps a hotkey as text, only where it differs from the default, and the window warns of
    // two hotkeys of one editor on one key.
    internal class HotkeyTests
    {
        class Hub : IGlobalEventHub
        {
            public List<object> Published { get; } = [];
            public void PublishGlobalEvent<T>(T e) => Published.Add(e!);
            public void Register<T>(object owner, Action<T> action) { }
            public void UnRegister(object owner) { }
        }

        static readonly HotkeyDefinition Undo = new("Test.Undo", "Group", "Undo", new Hotkey(Key.Z, ModifierKeys.Control));
        static readonly HotkeyDefinition Play = new("Test.Play", "Group", "Play", new Hotkey(Key.Space, ModifierKeys.None));

        [TestCase(Key.Z, ModifierKeys.Control, "Ctrl+Z")]
        [TestCase(Key.Z, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+Z")]
        [TestCase(Key.F10, ModifierKeys.Alt, "Alt+F10")]
        [TestCase(Key.Space, ModifierKeys.None, "Space")]
        [TestCase(Key.D1, ModifierKeys.None, "1")]
        [TestCase(Key.Add, ModifierKeys.None, "Add")]
        public void AHotkey_ReadsAsWpfNamesIt_AndParsesBack(Key key, ModifierKeys modifiers, string text)
        {
            var hotkey = new Hotkey(key, modifiers);
            Assert.That(hotkey.ToString(), Is.EqualTo(text));
            Assert.That(Hotkey.Parse(text), Is.EqualTo(hotkey));
        }

        [TestCase("")]
        [TestCase("Nonsense")]
        [TestCase("Ctrl+")]
        [TestCase("Sideways+Z")]
        public void TextThatNamesNoKey_ParsesToNull(string text) => Assert.That(Hotkey.Parse(text), Is.Null);

        [Test]
        public void TheFile_KeepsOnlyChanges_AndAClearedHotkeyIsNone()
        {
            var settings = new ApplicationSettingsService();
            var hub = new Hub();
            var hotkeys = new HotkeyService(settings, hub, [Undo, Play]);
            settings.CurrentSettings.Hotkeys["Other.Editor"] = "F5";

            Assert.That(hotkeys.Get(Undo.Id), Is.EqualTo(Undo.Default), "the default");

            hotkeys.Set(new Dictionary<string, Hotkey?> { [Undo.Id] = new Hotkey(Key.K, ModifierKeys.Control), [Play.Id] = null });
            Assert.That(settings.CurrentSettings.Hotkeys, Is.EquivalentTo(new Dictionary<string, string> { [Undo.Id] = "Ctrl+K", [Play.Id] = "", ["Other.Editor"] = "F5" }),
                "a hotkey this build does not know is kept");
            Assert.That(hotkeys.Get(Undo.Id), Is.EqualTo(new Hotkey(Key.K, ModifierKeys.Control)));
            Assert.That(hotkeys.Get(Play.Id), Is.Null, "cleared");
            Assert.That(hub.Published.OfType<HotkeysChangedEvent>().Count(), Is.EqualTo(1), "the open editors are told");

            hotkeys.Set(new Dictionary<string, Hotkey?> { [Undo.Id] = Undo.Default, [Play.Id] = Play.Default });
            Assert.That(settings.CurrentSettings.Hotkeys.Keys, Is.EquivalentTo(new[] { "Other.Editor" }), "back to the defaults, nothing kept");

            settings.CurrentSettings.Hotkeys[Undo.Id] = "Sideways+Z";
            Assert.That(hotkeys.Get(Undo.Id), Is.EqualTo(Undo.Default), "text that no longer parses");
        }

        [Test]
        public void ARow_TakesTheKeyPressed_ButNotTabEscOrALoneModifier()
        {
            var row = new HotkeyRow(Undo, "Undo", Undo.Default);

            Assert.That(row.Capture(Key.Tab, ModifierKeys.None), Is.False);
            Assert.That(row.Capture(Key.Escape, ModifierKeys.None), Is.False);
            Assert.That(row.Capture(Key.LeftCtrl, ModifierKeys.Control), Is.False);
            Assert.That(row.Hotkey, Is.EqualTo(Undo.Default));
            Assert.That(row.IsChanged, Is.False);

            Assert.That(row.Capture(Key.K, ModifierKeys.Control | ModifierKeys.Shift), Is.True);
            Assert.That(row.Text, Is.EqualTo("Ctrl+Shift+K"));
            Assert.That(row.IsChanged, Is.True);

            row.ClearCommand.Execute(null);
            Assert.That((row.Hotkey, row.HasHotkey, row.Text), Is.EqualTo(((Hotkey?)null, false, "")));
            row.ResetCommand.Execute(null);
            Assert.That(row.Hotkey, Is.EqualTo(Undo.Default));
        }

        [Test]
        public void TwoRowsOnOneKey_EachSaysWhichOther_UntilOneMoves()
        {
            var undo = new HotkeyRow(Undo, "Undo", Undo.Default);
            var play = new HotkeyRow(Play, "Play", Play.Default);
            _ = new HotkeyGroup("Group", [undo, play], "Also used by {0}");
            Assert.That((undo.Clash, play.Clash), Is.EqualTo(("", "")));

            play.Capture(Key.Z, ModifierKeys.Control);
            Assert.That((undo.Clash, play.Clash), Is.EqualTo(("Also used by Play", "Also used by Undo")));

            play.ClearCommand.Execute(null);
            undo.ClearCommand.Execute(null);
            Assert.That((undo.Clash, play.Clash), Is.EqualTo(("", "")), "two cleared rows do not clash");
        }
    }
}
