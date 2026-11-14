using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinWh3Converter;
using Editors.Shared.Core.Common;
using GameWorld.Core.Services;
using Shared.Core.Commands;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Editors.AnimationFragmentEditor.AnimationPack.BinGrid
{
    /// <summary>A WH3 battle table as a grid, one row per variant. Edits go through the animpack editor's EditTable, so each is one
    /// undo step and the XML view shows the same bytes.</summary>
    public partial class BinGridViewModel : ObservableObject
    {
        static readonly BaseAnimationSlotHelper Slots = AnimationSlotTypeHelperWh3.GetInstance();

        readonly Action<AnimationBinWh3, Action<AnimationBinWh3>> _edit;
        readonly BinTableChecks _checks;
        readonly IStandardDialogs _dialogs;
        readonly IUiCommandFactory _commands;
        readonly IPackFileService _pfs;
        readonly ISkeletonAnimationLookUpHelper _skeletons;
        readonly IReadOnlyList<string> _otherBins;
        readonly Func<IReadOnlyCollection<string>, string?> _pickSlot;

        public AnimationBinWh3 Table { get; }

        [ObservableProperty] ObservableCollection<BinGridRow> _rows = [];
        [ObservableProperty] IList<BinGridRow> _selectedRows = new List<BinGridRow>();
        [ObservableProperty] string _status = "";

        public BinGridViewModel(AnimationBinWh3 table, Action<AnimationBinWh3, Action<AnimationBinWh3>> edit, BinTableChecks checks, IStandardDialogs dialogs,
            IUiCommandFactory commands, IPackFileService pfs, ISkeletonAnimationLookUpHelper skeletons, IReadOnlyList<string> otherBins, Func<IReadOnlyCollection<string>, string?> pickSlot)
        {
            Table = table;
            _edit = edit;
            _checks = checks;
            _dialogs = dialogs;
            _commands = commands;
            _pfs = pfs;
            _skeletons = skeletons;
            _otherBins = otherBins;
            _pickSlot = pickSlot;
            _ui = SynchronizationContext.Current;
            DeepRunner = _checks.Deep;
            BuildRows();
            _checkedBytes = Table.ToByteArray();
            CheckPaths();
        }

        // Checks: Header and Files after every change, here and now; Deep, which opens files, on the thread pool after a pause, on a
        // copy of the table, one run at a time. A run that a later one overtakes while it waits does not run, and a result is
        // dropped when the table has changed again since its run started.

        readonly SynchronizationContext? _ui;
        readonly object _deepLock = new();
        List<BinFinding> _cheap = [];
        List<BinFinding> _deep = [];
        byte[] _checkedBytes;
        int _deepRun;
        CancellationTokenSource? _deepCancel;

        public TimeSpan DeepDelay { get; set; } = TimeSpan.FromMilliseconds(500);

        /// <summary>The deep checks, on a copy of the table. BinTableChecks.Deep by default; a test swaps it to hold a run back.</summary>
        public Func<AnimationBinWh3, List<BinFinding>> DeepRunner { get; set; }

        /// <summary>The latest deep run, for a test to wait on.</summary>
        public Task DeepChecks { get; private set; } = Task.CompletedTask;

        public string? NameProblem => HeaderProblem("Name");
        public string? SkeletonProblem => HeaderProblem("Skeleton");
        public string? GraphProblem => HeaderProblem("LocomotionGraph");
        string? HeaderProblem(string where) => _cheap.Where(x => x.Entry == null && x.Where == where).Select(x => x.Message).FirstOrDefault();

        /// <summary>Every header finding, one per line, written out under the header; null when there is none.</summary>
        public string? HeaderFindings => _cheap.Any(x => x.Entry == null) ? string.Join("\n", _cheap.Where(x => x.Entry == null).Select(x => x.Message)) : null;

        /// <summary>The background file checks are running.</summary>
        [ObservableProperty] bool _checking;

        /// <summary>The graph's finding is the "not provided" warning, not an error: its field is marked orange, not red.</summary>
        public bool GraphIsWarning => _cheap.Any(x => x.Entry == null && x.Where == "LocomotionGraph" && !x.IsError);

        public string Problems
        {
            get
            {
                var all = _cheap.Concat(_deep).ToList();
                var errors = all.Count(x => x.IsError);
                var warnings = all.Count - errors;
                return $"{errors} {(errors == 1 ? "error" : "errors")}, {warnings} {(warnings == 1 ? "warning" : "warnings")}";
            }
        }

        void CheckPaths()
        {
            _cheap = [.. _checks.Header(Table, Table.FileName), .. _checks.Files(Table)];
            ShowFindings();
        }

        void ShowFindings()
        {
            var byEntry = _cheap.Concat(_deep).Where(x => x.Entry != null).ToLookup(x => x.Entry!.Value);
            foreach (var row in Rows)
                row.SetFindings(byEntry[row.Entry].ToList());
            OnPropertyChanged(nameof(NameProblem));
            OnPropertyChanged(nameof(SkeletonProblem));
            OnPropertyChanged(nameof(GraphProblem));
            OnPropertyChanged(nameof(GraphIsWarning));
            OnPropertyChanged(nameof(HeaderFindings));
            OnPropertyChanged(nameof(Problems));
        }

        /// <summary>Starts the deep checks over the table as it is now. The animpack editor calls it when it opens the grid.</summary>
        public void StartDeepChecks()
        {
            _deepCancel?.Cancel();
            var cancel = _deepCancel = new CancellationTokenSource();
            var run = ++_deepRun;
            var copy = new AnimationBinWh3(Table.FileName, Table.ToByteArray());
            var delay = DeepDelay;
            var runner = DeepRunner;
            Checking = true;
            DeepChecks = Task.Run(async () =>
            {
                await Task.Delay(delay, cancel.Token);
                List<BinFinding>? found = null;
                string? failure = null;
                lock (_deepLock)
                {
                    if (cancel.IsCancellationRequested)
                        return;
                    try
                    {
                        found = runner(copy);
                    }
                    catch (Exception e)
                    {
                        failure = e.Message;
                    }
                }
                void Show()
                {
                    if (run != _deepRun)
                        return;
                    Checking = false;
                    if (failure != null)
                    {
                        Status = $"The file checks stopped: {failure}";
                        return;
                    }
                    _deep = found!;
                    ShowFindings();
                }
                if (_ui == null)
                    Show();
                else
                    _ui.Post(_ => Show(), null);
            }, cancel.Token);
        }

        void RunChecks()
        {
            var bytes = Table.ToByteArray();
            if (bytes.AsSpan().SequenceEqual(_checkedBytes))
            {
                ShowFindings();
                return;
            }
            _checkedBytes = bytes;
            _deep = [];
            CheckPaths();
            StartDeepChecks();
        }

        // The header strip

        public string Name { get => Table.Name; set => EditHeader(x => x.Name = value ?? ""); }
        public string Skeleton { get => Table.SkeletonName; set => EditHeader(x => x.SkeletonName = value ?? ""); }
        public string MountBin { get => Table.MountBin; set => EditHeader(x => x.MountBin = value ?? ""); }
        public string LocomotionGraph { get => Table.LocomotionGraph; set => EditHeader(x => x.LocomotionGraph = value ?? ""); }

        /// <summary>The header's unnamed string, empty on all 2,830 CA tables (2026-11-12). One comment calls it the rider table's name.</summary>
        public string UnknownText { get => Table.Unknown; set => EditHeader(x => x.Unknown = value ?? ""); }

        // UnknownValue1 only ever has two bits set across CA's 2,830 tables (2026-11-12): bit 0 on 13 fliers (bats, crows, vultures,
        // sky junk, the pegasus master) and bit 8 on 375 riders, every one with a mount. What either does is unconfirmed.
        public bool SimpleFlight { get => (Table.UnknownValue1 & 1) != 0; set => EditHeader(x => x.UnknownValue1 = Flag(x.UnknownValue1, 1, value)); }
        public bool RiderFlag { get => (Table.UnknownValue1 & 256) != 0; set => EditHeader(x => x.UnknownValue1 = Flag(x.UnknownValue1, 256, value)); }
        static short Flag(short bits, int flag, bool on) => (short)(on ? bits | flag : bits & ~flag);

        public string VersionText => $"{Table.TableVersion} / {Table.TableSubVersion}";

        public IReadOnlyList<string> SlotNames { get; } = Slots.Values.Select(x => x.Value).ToList();
        public IReadOnlyList<string> MountBins => _otherBins;

        IReadOnlyList<string>? _skeletonNames;
        public IReadOnlyList<string> Skeletons => _skeletonNames ??= _skeletons.GetAllSkeletonFileNames().Select(x => Path.GetFileNameWithoutExtension(x)).Distinct().Order().ToList();

        // Edits

        void EditHeader(Action<AnimationBinWh3> change)
        {
            _edit(Table, change);
            Refresh();
        }

        /// <summary>A change to a row's slot. With several rows selected and this one among them, every selected slot takes it, as
        /// one undo step.</summary>
        public void EditSlots(BinGridRow row, Action<AnimationBinEntry> change)
        {
            var rows = SelectedRows.Count > 1 && SelectedRows.Contains(row) ? SelectedRows : [row];
            var entries = rows.Select(x => x.Entry).Distinct().ToList();
            _edit(Table, table => entries.ForEach(x => change(table.AnimationTableEntries[x])));
            Refresh();
        }

        /// <summary>A change to a row's variant. A slot with none gets one first.</summary>
        public void EditVariant(BinGridRow row, Action<AnimationBinEntry.AnimationRef> change)
        {
            if (row.Variant >= 0)
            {
                _edit(Table, table => change(table.AnimationTableEntries[row.Entry].AnimationRefs[row.Variant]));
                Refresh();
                return;
            }
            // The cell commits what it shows, and here that is "", which must not give the slot a variant.
            var added = EmptyVariant();
            change(added);
            if (added is { AnimationFile: "", AnimationMetaFile: "", AnimationSoundMetaFile: "" })
                return;
            _edit(Table, table => table.AnimationTableEntries[row.Entry].AnimationRefs.Add(added));
            Reload();
        }

        /// <summary>A picked or typed animation. An empty Meta cell takes the animation's own meta, in the same undo step, when exactly
        /// one exists: of CA's table rows whose animation has only &lt;anim&gt;.meta, 98% use it, and of those with only &lt;anim&gt;.anm.meta,
        /// 69% do, nearly all the rest a unit's own meta no path rule finds. With both, CA picks either, so neither is guessed. A Meta
        /// that is set is never changed.</summary>
        public void SetAnimation(BinGridRow row, string? path)
        {
            var meta = MatchingMeta(path);
            EditVariant(row, x =>
            {
                // A cell committed with the animation it already had fills nothing: 8,250 of CA's rows leave the meta empty on purpose.
                if (meta != null && string.IsNullOrEmpty(x.AnimationMetaFile) && x.AnimationFile != path)
                    x.AnimationMetaFile = meta;
                x.AnimationFile = path ?? "";
            });
        }

        string? MatchingMeta(string? animation)
        {
            if (animation == null || !animation.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                return null;
            var stem = Key(animation)[..^".anim".Length];
            var found = new[] { stem + ".anm.meta", stem + ".meta" }.Where(x => _pfs.FindFile(x) != null).ToList();
            return found.Count == 1 ? found[0] : null;
        }

        public void SetSlot(BinGridRow row, string name)
        {
            var id = BinXml.SlotId(Slots, name ?? "");
            if (id == null)
                Status = $"{name} is not a WH3 animation slot.";
            else if (Table.AnimationTableEntries.Where((_, i) => i != row.Entry).Any(x => x.AnimationId == (uint)id))
                Status = $"This table already has {name}.";
            else
            {
                _edit(Table, table => table.AnimationTableEntries[row.Entry].AnimationId = (uint)id);
                foreach (var variant in Rows.Where(x => x.Entry == row.Entry))
                    variant.SlotId = (uint)id;
            }
            Refresh();
        }

        static AnimationBinEntry.AnimationRef EmptyVariant() => new() { AnimationFile = "", AnimationMetaFile = "", AnimationSoundMetaFile = "" };

        [RelayCommand]
        void AddVariant(BinGridRow? row)
        {
            if (row == null)
                return;
            _edit(Table, table => table.AnimationTableEntries[row.Entry].AnimationRefs.Add(EmptyVariant()));
            Reload();
        }

        [RelayCommand]
        void RemoveVariant(BinGridRow? row)
        {
            if (row is not { Variant: >= 0 })
                return;
            _edit(Table, table => table.AnimationTableEntries[row.Entry].AnimationRefs.RemoveAt(row.Variant));
            Reload();
        }

        [RelayCommand]
        void MatchFileName() => Name = Path.GetFileNameWithoutExtension(Table.FileName);

        // Browse and Open, as the VFX editor's path rows do them. Browse writes the picked file's full path, lower case with forward
        // slashes the way CA's tables hold them.

        /// <summary>The "Not filtered to this skeleton" box: Browse offers every animation, not only the table skeleton's.</summary>
        [ObservableProperty] bool _anyAnimation;

        [RelayCommand]
        void BrowseAnimation(BinGridRow? row)
        {
            if (row == null)
                return;
            var anims = AnyAnimation ? null : _skeletons.GetAnimationsForSkeleton(Skeleton).Select(x => Key(x.AnimationFile)).ToHashSet();
            Browse([".anim"], anims == null ? null : x => anims.Contains(Key(x)), path => row.Animation = path);
        }

        [RelayCommand] void BrowseMeta(BinGridRow? row) => Browse([".anm.meta", ".meta"], null, path => row?.Meta = path);
        [RelayCommand] void BrowseSound(BinGridRow? row) => Browse([".snd.meta"], null, path => row?.Sound = path);
        [RelayCommand] void BrowseGraph() => Browse([".xml"], null, path => LocomotionGraph = path);

        static string Key(string path) => path.Replace('\\', '/').ToLowerInvariant();

        void Browse(List<string> extensions, Func<string, bool>? filter, Action<string> picked)
        {
            var pick = _dialogs.DisplayBrowseDialog(extensions, filter);
            if (pick.Result && pick.File != null)
                picked(Key(_pfs.GetFullPath(pick.File)));
        }

        [RelayCommand]
        void OpenPath(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            if (_pfs.FindFile(path) is not { } file)
            {
                Status = $"{path} is not in the loaded packs.";
                return;
            }
            try
            {
                _commands.Create<OpenEditorCommand>().Execute(file);
            }
            catch (Exception e)
            {
                Status = $"{path} did not open: {e.Message}";
            }
        }

        // Slots

        IReadOnlyCollection<string> SlotNamesInTable() => Table.AnimationTableEntries.Select(x => BinXml.SlotName(Slots, x.AnimationId)).ToList();

        /// <summary>The selected slots, each once, in the table's order.</summary>
        List<int> SelectedEntries() => SelectedRows.Select(x => x.Entry).Distinct().Order().ToList();

        [RelayCommand]
        void AddSlot()
        {
            var picked = _pickSlot(SlotNamesInTable());
            if (picked == null)
                return;
            var id = BinXml.SlotId(Slots, picked);
            if (id == null || Table.AnimationTableEntries.Any(x => x.AnimationId == (uint)id))
            {
                Status = $"{picked} is not a slot this table can take.";
                return;
            }
            // CA's commonest values over its 462,477 rows (2026-11-12): blend 0.3 on 397,005, weight 1 on 439,458, and all six
            // weapons shown (63) on 153,454.
            var row = new AnimationBinEntry { AnimationId = (uint)id, BlendIn = 0.3f, SelectionWeight = 1f, WeaponBools = 63, AnimationRefs = [EmptyVariant()] };
            _edit(Table, table => AnimTableEdits.InsertInOrder(table, row));
            Reload(new HashSet<uint> { (uint)id });
        }

        /// <summary>A copy of each selected slot, files, blend, weight and weapons, under a slot picked for it.</summary>
        [RelayCommand]
        void DuplicateSlots()
        {
            var taken = SlotNamesInTable().ToHashSet();
            var copies = new List<(string Slot, int From)>();
            foreach (var entry in SelectedEntries())
            {
                var picked = _pickSlot(taken);
                if (picked == null || BinXml.SlotId(Slots, picked) == null || !taken.Add(picked))
                    continue;
                copies.Add((picked, entry));
            }
            if (copies.Count == 0)
                return;
            _edit(Table, table =>
            {
                var sources = copies.Select(x => (x.Slot, Row: table.AnimationTableEntries[x.From])).ToList();
                foreach (var (slot, row) in sources)
                    AnimTableEdits.AddSlot(table, slot, row);
            });
            Reload(copies.Select(x => (uint)BinXml.SlotId(Slots, x.Slot)!.Value).ToHashSet());
        }

        [RelayCommand]
        void RemoveSlots()
        {
            var entries = SelectedEntries();
            if (entries.Count == 0)
                return;
            _edit(Table, table => entries.OrderDescending().ToList().ForEach(table.AnimationTableEntries.RemoveAt));
            SelectedRows = [];
            Reload();
        }

        // Clipboard: the selected slots as the XML view's Animation elements, so a copy pastes into the text and the text into here.

        public Action<string> SetClipboard { get; set; } = text => System.Windows.Clipboard.SetText(text);
        public Func<string> GetClipboard { get; set; } = () => System.Windows.Clipboard.GetText();

        public string CopySlots()
        {
            var entries = SelectedEntries();
            var text = BinXml.ToText(entries.Select(x => Table.AnimationTableEntries[x]));
            Status = $"{entries.Count} slots copied.";
            return text;
        }

        [RelayCommand]
        void Copy()
        {
            if (SelectedEntries().Count != 0)
                SetClipboard(CopySlots());
        }

        [RelayCommand]
        void Paste() => PasteSlots(GetClipboard());

        /// <summary>Every Animation element in the text, as slots of this table. A slot the table has is replaced only when the
        /// question says so.</summary>
        public void PasteSlots(string text)
        {
            var rows = BinXml.FromText(text ?? "")?.DistinctBy(x => x.AnimationId).ToList();
            if (rows == null)
            {
                Status = "Nothing to paste: the clipboard holds no <Animation> rows.";
                return;
            }
            var existing = rows.Where(x => Table.AnimationTableEntries.Any(y => y.AnimationId == x.AnimationId)).ToList();
            var replace = existing.Count != 0 && _dialogs.ShowYesNoBox(
                $"{existing.Count} slots are already in this table: {string.Join(", ", existing.Select(x => BinXml.SlotName(Slots, x.AnimationId)))}. OK replaces them, Cancel keeps this table's own.",
                "Paste") == ShowMessageBoxResult.OK;
            _edit(Table, table =>
            {
                foreach (var row in rows)
                {
                    var at = table.AnimationTableEntries.FindIndex(x => x.AnimationId == row.AnimationId);
                    if (at < 0)
                        AnimTableEdits.InsertInOrder(table, row);
                    else if (replace)
                        table.AnimationTableEntries[at] = row;
                }
            });
            Reload(rows.Where(x => replace || !existing.Contains(x)).Select(x => x.AnimationId).ToHashSet());
            Status = $"{rows.Count - (replace ? 0 : existing.Count)} slots pasted.";
        }

        // Find and replace in paths

        [ObservableProperty] string _findText = "";
        [ObservableProperty] string _replaceText = "";

        [RelayCommand]
        void Replace() => ReplaceInPaths(FindText, ReplaceText);

        /// <summary>Replaces text in the animation, meta and sound paths, ignoring case: of the selected rows when there are several,
        /// else of every row. One undo step. The number of paths changed.</summary>
        public int ReplaceInPaths(string find, string replace)
        {
            if (string.IsNullOrEmpty(find))
            {
                Status = "Type the text to find first.";
                return 0;
            }
            var rows = SelectedRows.Count > 1 ? SelectedRows : Rows;
            var variants = rows.Where(x => x.Variant >= 0).Select(x => (x.Entry, x.Variant)).Distinct().ToList();
            var changed = 0;
            string Swap(string path)
            {
                var swapped = path.Replace(find, replace ?? "", StringComparison.OrdinalIgnoreCase);
                if (swapped != path)
                    changed++;
                return swapped;
            }
            _edit(Table, table =>
            {
                foreach (var (entry, variant) in variants)
                {
                    var reference = table.AnimationTableEntries[entry].AnimationRefs[variant];
                    reference.AnimationFile = Swap(reference.AnimationFile);
                    reference.AnimationMetaFile = Swap(reference.AnimationMetaFile);
                    reference.AnimationSoundMetaFile = Swap(reference.AnimationSoundMetaFile);
                }
            });
            Refresh();
            Status = $"{changed} paths changed.";
            return changed;
        }

        // Rows

        /// <summary>Builds the rows anew from the table: after a change to which variants exist, or an undo. The selection is kept by
        /// slot and variant where those still exist, or moves to the given slots, the ones just added.</summary>
        public void Reload(IReadOnlySet<uint>? select = null)
        {
            BuildRows(select);
            RunChecks();
        }

        void BuildRows(IReadOnlySet<uint>? select = null)
        {
            var selected = SelectedRows.Select(x => (x.SlotId, x.Variant)).ToHashSet();
            var rows = new ObservableCollection<BinGridRow>();
            for (var entry = 0; entry < Table.AnimationTableEntries.Count; entry++)
            {
                var variants = Table.AnimationTableEntries[entry].AnimationRefs.Count;
                if (variants == 0)
                    rows.Add(new BinGridRow(this, entry, -1));
                for (var variant = 0; variant < variants; variant++)
                    rows.Add(new BinGridRow(this, entry, variant));
            }
            Rows = rows;
            SelectedRows = rows.Where(x => select?.Contains(x.SlotId) ?? selected.Contains((x.SlotId, x.Variant))).ToList();
            OnPropertyChanged(string.Empty);
        }

        /// <summary>Every cell and header field read again from the table, keeping the same rows.</summary>
        public void Refresh()
        {
            OnPropertyChanged(string.Empty);
            foreach (var row in Rows)
                row.Refresh();
            RunChecks();
        }
    }
}
