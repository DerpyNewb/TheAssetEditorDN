using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Shared.Ui.Common.MenuSystem
{
    /// <summary>One editor's hotkeys in the settings window. A row whose key another row in the group also has says which.</summary>
    public class HotkeyGroup
    {
        readonly string _clash;

        public string Name { get; }
        public IReadOnlyList<HotkeyRow> Rows { get; }

        /// <param name="clash">The warning, with {0} for the other rows' names.</param>
        public HotkeyGroup(string name, IReadOnlyList<HotkeyRow> rows, string clash)
        {
            Name = name;
            Rows = rows;
            _clash = clash;
            foreach (var row in rows)
                row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(HotkeyRow.Hotkey)) FindClashes(); };
            FindClashes();
        }

        void FindClashes()
        {
            foreach (var row in Rows)
            {
                var others = Rows.Where(x => x != row && x.Hotkey != null && x.Hotkey == row.Hotkey).Select(x => x.Name).ToList();
                row.Clash = others.Count == 0 ? "" : string.Format(_clash, string.Join(", ", others));
            }
        }
    }

    public partial class HotkeyRow(HotkeyDefinition definition, string name, Hotkey? hotkey) : ObservableObject
    {
        static readonly HashSet<Key> NotAHotkey = [Key.None, Key.Tab, Key.Escape, Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift,
            Key.LeftAlt, Key.RightAlt, Key.LWin, Key.RWin, Key.System, Key.ImeProcessed, Key.DeadCharProcessed];

        public HotkeyDefinition Definition => definition;
        public string Name => name;

        [ObservableProperty, NotifyPropertyChangedFor(nameof(Text), nameof(IsChanged), nameof(HasHotkey))] Hotkey? _hotkey = hotkey;
        [ObservableProperty] string _clash = "";

        public string Text => Hotkey?.ToString() ?? "";
        public bool IsChanged => Hotkey != definition.Default;
        public bool HasHotkey => Hotkey != null;

        [RelayCommand] void Reset() => Hotkey = definition.Default;
        [RelayCommand] void Clear() => Hotkey = null;

        /// <summary>The key pressed in the row's box, with the modifiers held. Tab and Esc keep their usual jobs, and a modifier on its
        /// own waits for the key it goes with.</summary>
        public bool Capture(Key key, ModifierKeys modifiers)
        {
            if (NotAHotkey.Contains(key))
                return false;
            Hotkey = new Hotkey(key, modifiers);
            return true;
        }
    }
}
