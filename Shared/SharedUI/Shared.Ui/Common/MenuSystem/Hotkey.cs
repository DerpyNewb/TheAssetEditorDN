using System.Windows.Input;

namespace Shared.Ui.Common.MenuSystem
{
    public record Hotkey
    {
        public ModifierKeys ModifierKeys { get; set; }
        public Key Key { get; set; }

        public Hotkey(Key key, ModifierKeys modifierKeys)
        {
            Key = key;
            ModifierKeys = modifierKeys;
        }

        public bool Matches(Key key, ModifierKeys modifierKeys) => Key == key && ModifierKeys == modifierKeys;

        /// <summary>"Ctrl+Shift+Z", in WPF's own names for keys and modifiers; <see cref="Parse"/> reads it back.</summary>
        public override string ToString()
        {
            var key = new KeyConverter().ConvertToInvariantString(Key)!;
            return ModifierKeys == ModifierKeys.None ? key : $"{new ModifierKeysConverter().ConvertToInvariantString(ModifierKeys)}+{key}";
        }

        /// <summary>Null when the text names no key.</summary>
        public static Hotkey? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            var split = text.LastIndexOf('+');
            try
            {
                var key = (Key)new KeyConverter().ConvertFromInvariantString(text[(split + 1)..])!;
                var modifiers = split < 0 ? ModifierKeys.None : (ModifierKeys)new ModifierKeysConverter().ConvertFromInvariantString(text[..split])!;
                return key == Key.None ? null : new Hotkey(key, modifiers);
            }
            catch (Exception e) when (e is NotSupportedException or ArgumentException or FormatException)
            {
                return null;
            }
        }
    }
}
