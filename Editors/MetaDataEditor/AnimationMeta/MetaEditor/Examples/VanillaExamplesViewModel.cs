using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Editors.AnimationMeta.MetaEditor.Examples
{
    /// <summary>The vanilla examples of one tag and version, and a button that uses the picked one. The New tag dialog inserts a
    /// tag like it; the editor's examples window copies its values into the selected tag.</summary>
    public partial class VanillaExamplesViewModel : ObservableObject
    {
        readonly VanillaMetaExamples _examples;
        string? _key;

        [ObservableProperty] string _status = "";
        [ObservableProperty] List<VanillaExample> _items = [];
        [ObservableProperty, NotifyCanExecuteChangedFor(nameof(UseCommand))] VanillaExample? _selected;

        /// <summary>Called with the picked example when the button is clicked.</summary>
        public Action<VanillaExample>? Used { get; set; }

        public VanillaExamplesViewModel(VanillaMetaExamples examples) => _examples = examples;

        bool CanUse() => Selected != null;

        [RelayCommand(CanExecute = nameof(CanUse))]
        void Use()
        {
            if (Selected != null)
                Used?.Invoke(Selected);
        }

        /// <summary>Shows the examples of a "NAME_VERSION" key, starting the scan the first time. The task ends when they are shown,
        /// or when another key was asked for meanwhile.</summary>
        public async Task Show(string? key)
        {
            _key = key;
            Items = [];
            // Started even with no tag picked, so the New tag dialog has it under way while a tag is being picked.
            var scan = _examples.Scan();
            if (key == null)
            {
                Status = "Pick a tag to see how vanilla WH3 uses it.";
                return;
            }

            if (scan == null)
            {
                Status = "No game packs are loaded, so there are no vanilla examples to show. Open them with File > Open Game files, then pick the tag again.";
                return;
            }
            if (!scan.IsCompleted)
                Status = "Reading the game's animation metas for examples. This takes a few seconds the first time; after that they show at once.";

            IReadOnlyDictionary<string, List<VanillaExample>> found;
            try
            {
                found = await scan;
            }
            catch (Exception e)
            {
                if (_key == key)
                    Status = $"Reading the game's animation metas failed: {e.Message}";
                return;
            }

            if (_key != key)
                return;
            Items = found.GetValueOrDefault(key) ?? [];
            Selected = Items.FirstOrDefault();
            Status = Items.Count == 0
                ? $"No animation in the loaded game packs uses {key}."
                : $"How the game's own animations use {key}. Pick one to see its values.";
        }
    }
}
