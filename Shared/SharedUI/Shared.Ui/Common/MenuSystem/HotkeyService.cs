using Shared.Core.Events;
using Shared.Core.Settings;

namespace Shared.Ui.Common.MenuSystem
{
    /// <summary>A hotkey the settings window lists. Group and Name are localisation keys; the settings file keeps a change by Id.</summary>
    public record HotkeyDefinition(string Id, string Group, string Name, Hotkey? Default);

    /// <summary>Published once the settings window has saved new hotkeys.</summary>
    public record HotkeysChangedEvent;

    /// <summary>Every editor's hotkeys: the defaults, with the settings window's changes over them.</summary>
    public class HotkeyService(ApplicationSettingsService settings, IGlobalEventHub globalEventHub, IEnumerable<HotkeyDefinition> definitions)
    {
        public IReadOnlyList<HotkeyDefinition> Definitions { get; } = definitions.ToList();

        /// <summary>Null when the hotkey was cleared. A change the file holds that no longer parses falls back to the default.</summary>
        public Hotkey? Get(string id)
        {
            var definition = Definitions.FirstOrDefault(x => x.Id == id);
            if (settings.CurrentSettings.Hotkeys.TryGetValue(id, out var text))
                return text == "" ? null : Hotkey.Parse(text) ?? definition?.Default;
            return definition?.Default;
        }

        /// <summary>The file keeps only the hotkeys that differ from their defaults; the open editors are told. The caller saves the
        /// settings file.</summary>
        public void Set(IReadOnlyDictionary<string, Hotkey?> hotkeys)
        {
            var changes = settings.CurrentSettings.Hotkeys;
            foreach (var definition in Definitions)
            {
                if (hotkeys.TryGetValue(definition.Id, out var hotkey) == false)
                    continue;
                if (hotkey == definition.Default)
                    changes.Remove(definition.Id);
                else
                    changes[definition.Id] = hotkey?.ToString() ?? "";
            }
            globalEventHub.PublishGlobalEvent(new HotkeysChangedEvent());
        }
    }
}
