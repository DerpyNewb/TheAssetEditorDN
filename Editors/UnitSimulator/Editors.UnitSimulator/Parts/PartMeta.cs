using System.ComponentModel;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.ReferenceModel;
using Shared.Core.Events;
using Shared.Core.Events.Scoped;
using Shared.Core.PackFiles;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.UnitSimulator.Parts
{
    /// <summary>One part's two meta editors, persistent and slot, and the builder that draws its tags, as SuperView has for its one
    /// model. Each part has a builder of its own: a builder keeps what it built from one call to the next, and two parts sharing one
    /// would throw each other's away on every rebuild.</summary>
    public sealed class PartMeta : IDisposable
    {
        readonly SceneObjectViewModel _part;
        readonly MetaDataBuilder _builder;

        public MetaDataEditorViewModel Persistent { get; }
        public MetaDataEditorViewModel Slot { get; }
        public event Action? UnsavedChanged;

        public PartMeta(SceneObjectViewModel part, MetaDataBuilder builder, IUiCommandFactory uiCommandFactory, MetaDataFileParser parser, IEventHub eventHub)
        {
            _part = part;
            _builder = builder;
            Persistent = new MetaDataEditorViewModel(uiCommandFactory, parser, eventHub);
            Slot = new MetaDataEditorViewModel(uiCommandFactory, parser, eventHub);
            Persistent.PropertyChanged += OnEditorChanged;
            Slot.PropertyChanged += OnEditorChanged;
        }

        void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MetaDataEditorViewModel.HasUnsavedChanges))
                UnsavedChanged?.Invoke();
        }

        public bool HasUnsavedChanges
        {
            get => Persistent.HasUnsavedChanges || Slot.HasUnsavedChanges;
            set
            {
                Persistent.HasUnsavedChanges = value;
                Slot.HasUnsavedChanges = value;
            }
        }

        public bool Save() => Persistent.Save() & Slot.Save();

        /// <summary>The part's meta files changed (a slot was picked): load them into the editors and draw them.</summary>
        public void LoadAndRebuild()
        {
            Persistent.LoadFile(_part.Data.PersistMetaData);
            Slot.LoadFile(_part.Data.MetaData);
            Rebuild();
        }

        /// <summary>Draws the part's tags again, as SuperView's RecreateMetaDataInformation does.</summary>
        public void Rebuild()
        {
            var data = _part.Data;
            var old = data.MetaDataItems.ToList();
            data.MetaDataItems.Clear();
            data.Player.AnimationRules.Clear();
            data.MetaDataItems = _builder.Create(Persistent.ParsedFile, Slot.ParsedFile, Slot.SelectedAttribute, data.MainNode, data, data.Player, _part.FragAndSlotSelection.FragmentList.SelectedItem);
            foreach (var item in old.Except(data.MetaDataItems))
                item.CleanUp();
            data.Player.Refresh();
        }

        /// <summary>One of this part's editors saved its file: point the part at the saved copy, as SuperView does. A save from the
        /// other part's editors, or from a table, is not this part's.</summary>
        public void OnSaved(ScopedFileSavedEvent e, IPackFileService pfs, SceneObjectEditor editor)
        {
            if (e.FileOwner == Persistent)
                editor.SetMetaFile(_part.Data, _part.Data.MetaData, pfs.FindFile(e.NewPath));
            else if (e.FileOwner == Slot)
                editor.SetMetaFile(_part.Data, pfs.FindFile(e.NewPath), _part.Data.PersistMetaData);
        }

        public void Dispose()
        {
            Persistent.Close();
            Slot.Close();
            _builder.Dispose();
        }
    }
}
