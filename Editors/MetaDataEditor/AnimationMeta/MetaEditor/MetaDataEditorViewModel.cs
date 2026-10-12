using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimationMeta.MetaEditor.Commands;
using Shared.Core.Events;
using Shared.Core.PackFiles.Models;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.Presentation
{
    public partial class MetaDataEditorViewModel : ObservableObject, IEditorInterface, ISaveableEditor, IFileEditor
    {
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly MetaDataFileParser _metaDataFileParser;
        private readonly IEventHub _eventHub;
        public ParsedMetadataFile? ParsedFile { get; private set; }
        public ParsedMetadataAttribute? SelectedAttribute { get; private set; }

        [ObservableProperty] string _displayName = "Metadata Editor";
        [ObservableProperty] MetaDataEntry? _selectedTag;
        [ObservableProperty] ObservableCollection<MetaDataEntry> _tags = [];
        [ObservableProperty] int _metaDataFileVersion;

        // A field marks only its own row, and every add, delete, move and paste rebuilds the rows from the file, so a change made
        // before the last rebuild is kept here. Loading and saving clear it.
        bool _changedBeforeRebuild;

        public bool HasUnsavedChanges
        {
            get => _changedBeforeRebuild || Tags.Any(tag => tag.Variables.Any(x => x.IsModified));
            set
            {
                _changedBeforeRebuild = value;
                foreach (var tag in Tags)
                {
                    foreach (var variable in tag.Variables)
                        variable.IsModified = false;
                }
                OnPropertyChanged();
            }
        }
        public PackFile CurrentFile { get; set; }

        public MetaDataEditorViewModel(IUiCommandFactory uiCommandFactory, MetaDataFileParser metaDataFileParser, IEventHub eventHub)
        {
            _uiCommandFactory = uiCommandFactory;
            _metaDataFileParser = metaDataFileParser;
            _eventHub = eventHub;
            // An edited field says so on the hub; the tab that shows red while there are unsaved changes hears it from here.
            _eventHub.Register<MetaDataAttributeChangedEvent>(this, _ => OnPropertyChanged(nameof(HasUnsavedChanges)));
        }

        partial void OnSelectedTagChanged(MetaDataEntry? value)
        {
            if(value == null)
            
                SelectedAttribute = null;
            else
                SelectedAttribute = value._input;

            _eventHub.Publish(new SelecteMetaDataAttributeChangedEvent());
        }

        public bool Save() => _uiCommandFactory.Create<SaveCommand>().Execute(this);
        public void Close() => _eventHub.UnRegister(this);

        public void LoadFile(PackFile? file)
        {
            if (file == CurrentFile)
                return;

            CurrentFile = file;
            DisplayName = file == null ? "Metadata Editor" : file.Name;

            if (file == null)
            {
                ParsedFile = null;
                SelectedAttribute = null;
                Tags.Clear();
                SelectedTag = null;
            }
            else
            {
                var fileContent = CurrentFile.DataSource.ReadData();
                ParsedFile = _metaDataFileParser.ParseFile(fileContent);
                MetaDataFileVersion = ParsedFile.Version;
            }

            UpdateView();
            HasUnsavedChanges = false;
        }

        /// <summary>Rebuilds the rows from the file, which every caller but LoadFile has just changed: an entry added, deleted, moved
        /// or pasted.</summary>
        public void UpdateView()
        {
            _changedBeforeRebuild = true;
            Tags.Clear();
            if (ParsedFile != null)
            {
                // Descriptions are keyed by the bare tag name; DisplayName carries the version ("EFFECT_11").
                foreach (var metadataEntry in ParsedFile.Attributes)
                {
                    var desc = _metaDataFileParser.GetDatabase().GetDescriptionSafe(metadataEntry.Name);
                    Tags.Add(new MetaDataEntry(metadataEntry, desc, _eventHub, metadataEntry is not ParsedUnknownMetadataAttribute));
                }
            }
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }

        [RelayCommand] void MoveUpAction() => _uiCommandFactory.Create<MoveEntryCommand>().ExecuteUp(this);
        [RelayCommand] void MoveDownAction() => _uiCommandFactory.Create<MoveEntryCommand>().ExecuteDown(this);
        [RelayCommand] void DeleteAction() => _uiCommandFactory.Create<DeleteEntryCommand>(x => x.Configure(this)).Execute();
        [RelayCommand] void NewAction() => _uiCommandFactory.Create<NewEntryCommand>(x => x.Configure(this)).Execute();
        [RelayCommand] void PasteAction() => _uiCommandFactory.Create<CopyPastCommand>().ExecutePaste(this);
        [RelayCommand] void CopyAction() => _uiCommandFactory.Create<CopyPastCommand>().ExecuteCopy(this);
        [RelayCommand] void SaveAction() => _uiCommandFactory.Create<SaveCommand>().Execute(this);
    }
}

