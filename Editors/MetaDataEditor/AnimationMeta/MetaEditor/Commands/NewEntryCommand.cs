using Editors.AnimationMeta.MetaEditor.Examples;
using Editors.AnimationMeta.Presentation.View;
using Shared.Core.Events;
using Shared.GameFormats.AnimationMeta.Parsing;
using System.Windows;
using Editors.AnimationMeta.Presentation;

namespace Editors.AnimationMeta.MetaEditor.Commands
{
    public class NewEntryCommand : IAeCommand
    {
        private readonly MetaDataFileParser _metaDataFileParser;
        private readonly IMetaDataDatabase _metaDataDatabase;
        private readonly VanillaMetaExamples _vanillaExamples;
        private MetaDataEditorViewModel _controller = null!;

        public NewEntryCommand(MetaDataFileParser metaDataFileParser, IMetaDataDatabase metaDataDatabase, VanillaMetaExamples vanillaExamples) 
        {
            _metaDataFileParser = metaDataFileParser;
            _metaDataDatabase = metaDataDatabase;
            _vanillaExamples = vanillaExamples;
        }

        public void Configure(MetaDataEditorViewModel controller)
        {
            _controller = controller;
        }

        public void Execute()
        {
            var dialog = new NewMetaDataEntryWindow() { Owner = Application.Current.MainWindow };
            // Keys are "NAME_VERSION"; descriptions are stored per NAME.
            var items = _metaDataDatabase.GetSupportedTypes()
                .Select(key => new NewTagItem(key, _metaDataDatabase.GetDescriptionSafe(key[..key.LastIndexOf('_')]), _metaDataDatabase.GetWarhammer3UseCount(key)))
                .ToList();

            VanillaExample? example = null;
            var examples = new VanillaExamplesViewModel(_vanillaExamples) { Used = x => { example = x; dialog.DialogResult = true; } };
            _ = examples.Show(null);
            var model = new NewTagWindowViewModel(items, examples);
            dialog.DataContext = model;

            var res = dialog.ShowDialog();
            if (res.HasValue && res.Value == true)
                Add(model.SelectedItem!.Key, example);

            dialog.DataContext = null;
        }

        /// <summary>Adds a tag of the key at the end and selects it: the example's values when one was picked, else the defaults.</summary>
        public void Add(string key, VanillaExample? example)
        {
            var newEntry = example != null ? _vanillaExamples.Create(example) : _metaDataFileParser.CreateDefault(key);
            _controller.ParsedFile!.Attributes.Add(newEntry);
            _controller.UpdateView();
            _controller.SelectedTag = _controller.Tags.LastOrDefault();
        }

    }
}
