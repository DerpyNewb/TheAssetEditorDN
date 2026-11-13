using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.VfxEditor.Document;
using Serilog;
using Shared.Core.Commands;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Events;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.CoreLog;

namespace Editors.VfxEditor.VariantMesh
{
    /// <summary>The variant mesh grid (docs/superpowers/specs/2026-11-13-asseteditor-variant-mesh-grid-design.md). Every change is a
    /// set of splices applied to the document in one step, so undo is the document's own stack and untouched text never changes.</summary>
    public sealed partial class VmdEditorViewModel : ObservableObject, IEditorInterface, IFileEditor, ISaveableEditor
    {
        readonly ILogger _logger = Logging.Create<VmdEditorViewModel>();
        readonly IPackFileService _packFileService;
        readonly IFileSaveService _fileSaveService;
        readonly IStandardDialogs _dialogs;
        readonly IUiCommandFactory _commands;
        readonly IGlobalEventHub _globalEventHub;
        List<VmdRowRef> _refs = [];
        Func<VmdRowRef, bool>? _select;

        // The checks run after every edit; the packs are asked about each path once (a query on the game's cache), and again only
        // after a pack changes. The file's own path is found once per load or save.
        readonly Dictionary<string, bool> _exists = new(StringComparer.OrdinalIgnoreCase);
        string? _ownPath;

        public VmdEditorViewModel(IPackFileService packFileService, IFileSaveService fileSaveService, IStandardDialogs dialogs, IUiCommandFactory commands,
            IGlobalEventHub globalEventHub)
        {
            _packFileService = packFileService;
            _fileSaveService = fileSaveService;
            _dialogs = dialogs;
            _commands = commands;
            _globalEventHub = globalEventHub;
            // Every pack, file and folder change derives from this event.
            globalEventHub.Register<PackFileContainerManipulationEvent>(this, _ =>
            {
                _exists.Clear();
                RunChecks();
            });
        }

        bool Exists(string path)
        {
            var key = path.Replace('/', '\\');
            if (!_exists.TryGetValue(key, out var found))
                _exists[key] = found = _packFileService.FindFile(path) != null;
            return found;
        }

        // CA's most used names first (variants.pack, 2026-11-13); any other name can be typed.
        public static string[] SlotNames { get; } = ["body", "head", "weapon_1", "weapon_2", "weapon_3", "weapon_4", "weapon_5", "weapon_6", "shield", "legs",
            "torso", "cape", "wings", "left_arm", "right_arm", "accessories", "tail", "stump_left_arm", "stump_right_arm", "stump_waist", "stump_neck", "stump_severed_head"];

        public static string[] AttachPoints { get; } = ["be_prop_0", "be_prop_1", "be_prop_2", "be_prop_3", "be_prop_4", "be_prop_5", "root", "head", "head_0",
            "spine_1", "spine_2", "clav_left", "clav_right", "clav_left_0", "clav_right_0", "weapon_1", "weapon_2", "weapon_3", "weapon_4", "weapon_5", "weapon_6",
            "fr_spine_2", "fr_clav_left", "fr_clav_right", "fr_hips"];

        public VfxDocument? Document { get; private set; }
        public PackFile? CurrentFile { get; private set; }
        public ObservableCollection<VmdRow> Rows { get; } = [];

        [ObservableProperty] string _displayName = "Variant Mesh Editor";
        [ObservableProperty] IReadOnlyList<VmdRow> _selectedRows = [];
        [ObservableProperty] string _status = "";
        [ObservableProperty] string _problems = "";
        [ObservableProperty] string _findText = "";
        [ObservableProperty] string _replaceText = "";
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasLoadError))] string? _loadError = "No file is open. Double-click a .variantmeshdefinition in the pack explorer.";
        [ObservableProperty] bool _hasUnsavedChanges;

        public bool HasLoadError => LoadError != null;

        /// <summary>Set by the view: writes a cell still being edited before a save, undo or command that does not take its focus.</summary>
        public Action? FlushPendingEdits { get; set; }

        public void LoadFile(PackFile file)
        {
            if (Document != null)
                Document.Changed -= OnDocumentChanged;
            CurrentFile = file;
            DisplayName = file.Name;
            _ownPath = _packFileService.GetFullPath(file);
            (Document, LoadError) = VmdLayout.Load(file.DataSource.ReadData());
            if (Document != null)
                Document.Changed += OnDocumentChanged;
            HasUnsavedChanges = false;
            Status = "";
            Rows.Clear();
            _refs = [];
            Sync();
            OnPropertyChanged(nameof(CurrentFile));
        }

        void OnDocumentChanged()
        {
            HasUnsavedChanges = true;
            Sync();
        }

        /// <summary>Re-reads the rows. Rows that keep their place are refreshed in place, so a value edit leaves the grid as it was;
        /// otherwise they are rebuilt, keeping the selection by slot and entry, or selecting what the edit added.</summary>
        void Sync()
        {
            var refs = Document == null ? [] : VmdLayout.Rows(Document);
            if (refs.Select(r => r.Key).SequenceEqual(_refs.Select(r => r.Key)) && Rows.Count == refs.Count)
            {
                for (var i = 0; i < refs.Count; i++)
                {
                    Rows[i].Ref = refs[i];
                    Rows[i].Refresh();
                }
            }
            else
            {
                var kept = SelectedRows.Select(r => r.Ref.Key).ToHashSet();
                var select = _select ?? (r => kept.Contains(r.Key));
                Rows.Clear();
                foreach (var reference in refs)
                    Rows.Add(new VmdRow(this, reference));
                SelectedRows = Rows.Where(r => select(r.Ref)).ToList();
            }
            _refs = refs;
            _select = null;
            RunChecks();
        }

        void RunChecks()
        {
            if (Document == null)
            {
                Problems = "";
                return;
            }
            var findings = VmdChecks.Run(Document, _refs, Exists, _ownPath);
            var byRow = findings.ToLookup(f => f.Row);
            for (var i = 0; i < Rows.Count; i++)
                Rows[i].SetFindings(byRow[i].ToList());
            var errors = findings.Count(f => f.IsError);
            var warnings = findings.Count - errors;
            Problems = findings.Count == 0 ? "No problems found" : $"{Plural(errors, "error")}, {Plural(warnings, "warning")}";
        }

        static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

        /// <summary>Applies the edit's splices as one undo step. With selectAdded, the rows the edit inserts become the selection.</summary>
        internal void Edit(Func<VfxDocument, IEnumerable<Splice>> edit, bool selectAdded = false)
        {
            if (Document == null)
                return;
            var splices = edit(Document).ToList();
            if (splices.Count == 0)
                return;
            if (selectAdded)
                _select = Added(splices);
            try
            {
                Document.Apply(splices);
            }
            catch (InvalidOperationException e)
            {
                Status = e.Message;
            }
            finally
            {
                _select = null;
            }
        }

        /// <summary>The rows whose slot or entry starts in text the splices insert, in the text after they are applied.</summary>
        Func<VmdRowRef, bool> Added(List<Splice> splices)
        {
            var regions = new List<(int Start, int End)>();
            var shift = 0;
            foreach (var splice in splices.OrderBy(s => s.Start))
            {
                regions.Add((splice.Start + shift, splice.Start + shift + splice.Text.Length));
                shift += splice.Text.Length - (splice.End - splice.Start);
            }
            bool Inside(int at) => regions.Any(r => at >= r.Start && at < r.End);
            return row => Inside(Document!.At(row.Slot).Start) || (VmdLayout.EntryOf(Document!, row) is { } entry && Inside(entry.Start));
        }

        /// <summary>The selected rows' slots, each once; a slot inside another selected slot goes with that one, so Copy and Remove
        /// take it once.</summary>
        List<SpanElement> SelectedSlots
        {
            get
            {
                var slots = SelectedRows.Select(r => r.SlotElement).DistinctBy(s => s.Start).ToList();
                return slots.Where(s => !slots.Any(o => o.Start < s.Start && s.End <= o.End)).ToList();
            }
        }

        // Rows

        [RelayCommand]
        void AddSlot()
        {
            FlushPendingEdits?.Invoke();
            Edit(d => [VmdEdits.AddSlot(d, SelectedSlots.FirstOrDefault()).Splice], selectAdded: true);
        }

        [RelayCommand]
        void RemoveSlots()
        {
            FlushPendingEdits?.Invoke();
            var slots = SelectedSlots;
            if (slots.Count == 0)
                return;
            // Nothing stays selected: the next slot takes the removed one's place, and a second click would remove it too.
            SelectedRows = [];
            Edit(d => slots.Select(d.Remove));
            Status = $"Removed {Plural(slots.Count, "slot")}";
        }

        [RelayCommand]
        void AddEntry(VmdRow? row)
        {
            if (row == null || Document == null)
                return;
            FlushPendingEdits?.Invoke();
            var path = Browse([".wsmodel", ".rigid_model_v2", ".variantmeshdefinition"]);
            if (path != null)
                Edit(d => [VmdEdits.AddEntry(d, row.SlotElement, path)], selectAdded: true);
        }

        [RelayCommand]
        void RemoveEntry(VmdRow? row)
        {
            FlushPendingEdits?.Invoke();   // first: writing a pending cell moves the offsets the entry is found by
            if (row?.Entry is not { } entry)
                return;
            Edit(d => [d.Remove(entry)]);
        }

        [RelayCommand]
        void Copy()
        {
            var text = CopyText();
            if (text.Length == 0)
                return;
            try
            {
                Clipboard.SetText(text);
                Status = $"Copied {Plural(SelectedSlots.Count(), "slot")}";
            }
            catch (Exception e)   // the clipboard can be held by another program
            {
                Status = "Could not copy: " + e.Message;
            }
        }

        internal string CopyText()
        {
            FlushPendingEdits?.Invoke();
            if (Document == null || SelectedRows.Count == 0)
            {
                Status = "Select the slots to copy.";
                return "";
            }
            return VmdEdits.Copy(Document, SelectedSlots);
        }

        [RelayCommand]
        void Paste()
        {
            string text;
            try
            {
                text = Clipboard.GetText();
            }
            catch (Exception e)
            {
                Status = "Could not read the clipboard: " + e.Message;
                return;
            }
            PasteText(text);
        }

        internal void PasteText(string text)
        {
            if (Document == null)
                return;
            FlushPendingEdits?.Invoke();
            List<Splice> splices;
            try
            {
                splices = VmdEdits.Paste(Document, SelectedSlots.FirstOrDefault(), text);
            }
            catch (ArgumentException e)
            {
                Status = e.Message;
                return;
            }
            Edit(_ => splices, selectAdded: true);
        }

        [RelayCommand]
        void Replace()
        {
            if (Document == null)
                return;
            FlushPendingEdits?.Invoke();
            var rows = SelectedRows.Count > 1 ? SelectedRows : Rows;
            var entries = rows.Select(r => r.Entry).OfType<SpanElement>().DistinctBy(e => e.Start).ToList();
            var splices = VmdEdits.ReplaceInPaths(Document, entries, FindText, ReplaceText, out var cells);
            Edit(_ => splices);
            Status = cells == 0 ? $"No path holds \"{FindText}\"" : $"Replaced in {Plural(cells, "cell")}";
        }

        // Paths

        [RelayCommand]
        void BrowsePath(VmdRow? row)
        {
            FlushPendingEdits?.Invoke();
            if (row?.Entry is not { } entry)
                return;
            var path = Browse(entry.Name == "VARIANT_MESH" ? [".wsmodel", ".rigid_model_v2"] : [".variantmeshdefinition"]);
            if (path != null)
                row.Path = path;
        }

        [RelayCommand]
        void BrowseImposter(VmdRow? row)
        {
            FlushPendingEdits?.Invoke();
            if (row is not { HasTags: true })
                return;
            var path = Browse([".rigid_model_v2", ".wsmodel"]);
            if (path != null)
                row.Imposter = path;
        }

        string? Browse(List<string> extensions)
        {
            var picked = _dialogs.DisplayBrowseDialog(extensions);
            return picked.Result && picked.File != null ? _packFileService.GetFullPath(picked.File) : null;
        }

        [RelayCommand]
        void OpenPath(string? path)
        {
            FlushPendingEdits?.Invoke();
            if (string.IsNullOrWhiteSpace(path))
                return;
            if (_packFileService.FindFile(path) is { } file)
                _commands.Create<OpenEditorCommand>().Execute(file);
            else
                Status = "Not found in the loaded packs: " + path;
        }

        // Saving, undo

        [RelayCommand]
        void Undo()
        {
            FlushPendingEdits?.Invoke();
            Document?.Undo();
        }

        [RelayCommand]
        void Redo()
        {
            FlushPendingEdits?.Invoke();
            Document?.Redo();
        }

        [RelayCommand] void SaveFile() => Save();

        public bool Save()
        {
            FlushPendingEdits?.Invoke();
            if (Document == null || CurrentFile == null)
                return false;
            var path = _packFileService.GetFullPath(CurrentFile);
            try
            {
                var saved = _fileSaveService.Save(path, Document.Save(), prompOnConflict: false);
                if (saved == null)
                    return false;
                CurrentFile = saved;
                _ownPath = path;
                HasUnsavedChanges = false;
                Status = "Saved " + path;
                OnPropertyChanged(nameof(CurrentFile));
                return true;
            }
            catch (Exception e)
            {
                _logger.Error(e, "Saving {Path} failed", path);
                _dialogs.ShowExceptionWindow(e, "The file was not saved. Is an editable pack selected?");
                return false;
            }
        }

        public void Close()
        {
            _globalEventHub.UnRegister(this);
            if (Document != null)
                Document.Changed -= OnDocumentChanged;
        }
    }
}
