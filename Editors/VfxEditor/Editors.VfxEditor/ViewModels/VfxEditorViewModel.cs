using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.VfxEditor.Document;
using Editors.VfxEditor.Preview;
using Editors.VfxEditor.Vocabulary;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Grid;
using GameWorld.Core.WpfWindow;
using Serilog;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.CoreLog;

namespace Editors.VfxEditor.ViewModels
{
    /// <summary>The VFX editor tab (docs/superpowers/specs/2026-10-11-vfx-editor-design.md). Every change goes through the
    /// document as splices, so the file keeps every byte nobody edited.</summary>
    public sealed partial class VfxEditorViewModel : ObservableObject, IEditorInterface, IFileEditor, ISaveableEditor, IDisposable
    {
        static readonly string[] s_nodeFlags = [nameof(IsEmitterNode), nameof(IsChainNode), nameof(IsModifierNode), nameof(IsReferencesNode),
            nameof(IsReferenceNode), nameof(IsOverrideNode), nameof(CanAddProperty), nameof(IsBlockMissing)];

        readonly ILogger _logger = Logging.Create<VfxEditorViewModel>();
        readonly VfxFileSession _session;
        readonly IStandardDialogs _dialogs;
        VfxVocabulary? _vocabulary;
        readonly VfxPreviewComponent _preview;
        readonly GridComponent _grid;
        readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
        int _shownLines;

        public VfxEditorViewModel(IPackFileService packFileService, IFileSaveService fileSaveService, IStandardDialogs dialogs,
            IWpfGame gameWorld, IComponentInserter componentInserter, VfxVocabularyService vocabularyService, VfxPreviewComponent preview, GridComponent grid)
        {
            _grid = grid;
            _session = new VfxFileSession(packFileService, fileSaveService);
            _dialogs = dialogs;
            Scene = gameWorld;
            componentInserter.Execute();
            _preview = preview;
            _preview.TimeChanged += OnPreviewTimeChanged;
            _previewTimer.Tick += (_, _) =>
            {
                _previewTimer.Stop();
                RefreshPreview();
            };
            VocabularyLoaded = LoadVocabulary(vocabularyService);
        }

        /// <summary>The vocabulary read and the rows rebuilt with it (or the failure reported).</summary>
        internal Task VocabularyLoaded { get; }

        async Task LoadVocabulary(VfxVocabularyService vocabularyService)
        {
            Status = "Reading vanilla effects";
            try
            {
                _vocabulary = await vocabularyService.Get();
                Status = $"Read {_vocabulary.EffectNames.Count} vanilla effects";
            }
            catch (Exception e)
            {
                _logger.Error(e, "Reading the vanilla effects failed");
                Status = "Could not read the vanilla effects: " + e.Message;
            }
            OnVocabularyArrived();
        }

        internal void OnVocabularyArrived()
        {
            FlushPendingEdits?.Invoke();   // the rows are rebuilt below, and a box's typed text would go with them
            OnPropertyChanged(nameof(FragmentChoices));
            OnPropertyChanged(nameof(EffectChoices));
            RefreshRows(rebuild: true);
            RefreshAddChoices();
        }

        public IWpfGame Scene { get; }
        [ObservableProperty] string _displayName = "VFX Editor";
        public PackFile? CurrentFile => _session.File;

        public bool HasUnsavedChanges
        {
            get => _session.HasUnsavedChanges;
            set
            {
                _session.HasUnsavedChanges = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<VfxTreeNode> Tree { get; } = [];
        public ObservableCollection<PropertyRow> Rows { get; } = [];
        public string[] TierNames { get; } = ["Ultra", "High", "Medium"];

        [ObservableProperty] int _tier;
        [ObservableProperty] bool _thisTierOnly;
        [ObservableProperty] VfxTreeNode? _selectedNode;
        [ObservableProperty] string _status = "";
        // Opened from the Tools menu, the tab has no file yet.
        [ObservableProperty] string? _loadError = "No file is open. Use Open to pick a vfx\\*.xml from the loaded packs, or double-click one in the pack explorer.";

        /// <summary>Set by the view: writes a box's typed text before a tier, toggle, save or undo, none of which take its
        /// focus, so the value lands on the tier and node it was typed against.</summary>
        public Action? FlushPendingEdits { get; set; }

        partial void OnTierChanging(int value) => FlushPendingEdits?.Invoke();
        partial void OnThisTierOnlyChanging(bool value) => FlushPendingEdits?.Invoke();

        public bool HasLoadError => LoadError != null;
        public bool IsLoaded => LoadError == null;
        public bool IsEmitterNode => SelectedNode?.Kind == NodeKind.Emitter;
        public bool IsChainNode => SelectedNode?.Kind == NodeKind.Chain;
        public bool IsModifierNode => SelectedNode?.Kind == NodeKind.Modifier;
        public bool IsReferencesNode => SelectedNode?.Kind == NodeKind.References;
        public bool IsReferenceNode => SelectedNode?.Kind == NodeKind.Reference;
        public bool IsOverrideNode => SelectedNode?.Kind == NodeKind.Override;
        public bool CanAddProperty => SelectedNode?.Kind is NodeKind.Emitter or NodeKind.Stack or NodeKind.Modifier;
        public bool IsBlockMissing => SelectedNode?.Kind == NodeKind.Chain && _session.Document is { } document
            && TierEdits.Block(document.At(SelectedNode.Path), Tier) == null;

        public void LoadFile(PackFile file)
        {
            if (_session.Document != null)
                _session.Document.Changed -= OnDocumentChanged;
            _session.Load(file);
            DisplayName = file.Name;
            LoadError = _session.LoadError;
            OnPropertyChanged(nameof(CurrentFile));
            OnPropertyChanged(nameof(HasUnsavedChanges));
            Tree.Clear();
            Rows.Clear();
            if (_session.Document == null)
                return;
            _session.Document.Changed += OnDocumentChanged;
            RebuildTree();
            Status = $"{_session.EffectName} ({_session.Document.Encoding})";
            RefreshPreview();
        }

        partial void OnLoadErrorChanged(string? value)
        {
            OnPropertyChanged(nameof(HasLoadError));
            OnPropertyChanged(nameof(IsLoaded));
        }

        partial void OnTierChanged(int value)
        {
            RebuildTree();
            RefreshRows(rebuild: true);
            NotifyNodeFlags();
            RefreshAddChoices();
            RefreshPreview();
        }

        partial void OnSelectedNodeChanged(VfxTreeNode? value)
        {
            RefreshRows(rebuild: true);
            NotifyNodeFlags();
            if (value?.Kind == NodeKind.Emitter && _session.Document is { } document)
                NewEmitterId = document.At(value.Path).Attr("id") + "_copy";
            RefreshAddChoices();
        }

        void NotifyNodeFlags()
        {
            foreach (var name in s_nodeFlags)
                OnPropertyChanged(name);
        }

        void OnDocumentChanged()
        {
            OnPropertyChanged(nameof(HasUnsavedChanges));
            RebuildTree();
            RefreshRows();
            NotifyNodeFlags();
            RefreshAddChoices();
            SchedulePreview();
        }

        // Edits

        /// <summary>Builds splices against the current document and applies them as one undo step. A refusal (a taken id,
        /// a tier with no block, an edit that would break the file) goes to the status line and changes nothing.</summary>
        bool Edit(Func<VfxDocument, IEnumerable<Splice>> build, VfxTreeNode? node = null)
        {
            var document = _session.Document;
            if (document == null)
                return false;
            // Read before the edit: a remove can bring the counts back in line.
            var skipped = node is { Kind: NodeKind.Modifier } && !ThisTierOnly ? TierEdits.UnmatchedTiers(ChainOf(document, node), Tier, node.Fragment!) : [];
            try
            {
                document.Apply(build(document).ToList());
                if (skipped.Count > 0)
                    Status = $"{string.Join(" and ", skipped.Select(t => TierNames.ElementAtOrDefault(t) ?? $"tier {t}"))} left alone: its block has a different number of {node!.Fragment}, so which one matches is unknown";
                return true;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                _logger.Warning("VFX edit refused: {Reason}", e.Message);
                Status = e.Message;
                RefreshRows();
                return false;
            }
        }

        void Commit(PropertyRow row, string value)
        {
            if (SelectedNode is not { } node)
                return;
            Edit(document =>
            {
                var element = document.At(node.Path);
                return node.Kind switch
                {
                    NodeKind.Emitter or NodeKind.Stack => TierEdits.SetProperty(document, element, row.Property, row.Attribute, Tier, value, ThisTierOnly),
                    NodeKind.Modifier => TierEdits.SetModifierProperty(document, element.Parent!.Parent!, Tier, node.Fragment!, node.Occurrence,
                        row.Property, row.Attribute, value, ThisTierOnly),
                    _ => [document.SetAttribute(element, row.Attribute!, value)],
                };
            }, node);
        }

        // The value rows

        void RefreshRows(bool rebuild = false)
        {
            var document = _session.Document;
            List<PropertyRow> rows = document == null || SelectedNode == null ? [] : BuildRows(document, SelectedNode);
            if (!rebuild && rows.Select(r => (r.Key, r.Kind)).SequenceEqual(Rows.Select(r => (r.Key, r.Kind))))
            {
                for (var i = 0; i < rows.Count; i++)
                    Rows[i].Refresh(rows[i]);
                return;
            }
            Rows.Clear();
            foreach (var row in rows)
                Rows.Add(row);
        }

        List<PropertyRow> BuildRows(VfxDocument document, VfxTreeNode node)
        {
            var element = document.At(node.Path);
            return node.Kind switch
            {
                NodeKind.Effect or NodeKind.Reference => element.Attributes.Select(a => Row("", a.Name, a.Value, "")).ToList(),
                NodeKind.Override => element.Attributes.Where(a => a.Name != "name").Select(a => Row("", a.Name, a.Value, "")).ToList(),
                NodeKind.Emitter or NodeKind.Stack => TierEdits.EffectiveProperties(element, Tier).SelectMany(p => RowsOf(document, p, TierNote(p))).ToList(),
                NodeKind.Modifier => element.Children.Where(c => c.Attr("name") != null).SelectMany(p => RowsOf(document, p, "")).ToList(),
                _ => [],
            };
        }

        IEnumerable<PropertyRow> RowsOf(VfxDocument document, SpanElement property, string note)
        {
            var name = property.Attr("name")!;
            foreach (var attribute in property.Attributes.Where(a => a.Name is not ("name" or "quality")))
                yield return Row(name, attribute.Name, attribute.Value, note);
            if (!property.SelfClosing && property.Children.Count == 0 && document.TextOf(property).Trim().Length > 0)
                yield return Row(name, null, document.TextOf(property), note);
        }

        string TierNote(SpanElement property) => Tier == 0 ? "" : TierEdits.TierOf(property) == Tier ? "this tier" : "from Ultra";

        PropertyRow Row(string property, string? attribute, string value, string note)
        {
            var label = property.Length == 0 ? attribute! : attribute is null or "value" ? property : $"{property} {attribute}";
            var choices = attribute != null && property.Length > 0 ? _vocabulary?.Choices(property, attribute) : null;
            if (choices != null && !choices.Contains(value))
                choices = [value, .. choices];
            var kind = attribute == null ? (CurveText.Parse(value) != null ? RowKind.Curve : RowKind.Text)
                : value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase) ? RowKind.Bool
                : float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? RowKind.Number
                : choices != null ? RowKind.Choice
                : RowKind.Text;
            return new PropertyRow(property, attribute, label, kind, value, choices, note, Commit, message => Status = message);
        }

        // The tree

        void RebuildTree()
        {
            var document = _session.Document;
            if (document == null)
                return;
            var fresh = BuildTree(document);
            if (Signature(fresh).SequenceEqual(Signature(Tree)))
            {
                foreach (var (old, now) in All(Tree).Zip(All(fresh)))
                    old.Path = now.Path;
                return;
            }
            var expanded = All(Tree).Where(n => n.IsExpanded).Select(n => n.Key).ToHashSet();
            var selected = SelectedNode?.Key;
            Tree.Clear();
            foreach (var node in fresh)
                Tree.Add(node);
            foreach (var node in All(Tree))
                node.IsExpanded = expanded.Contains(node.Key) || node.Kind is NodeKind.Emitters or NodeKind.References;
            var again = All(Tree).FirstOrDefault(n => n.Key == selected);
            if (again != null)
                again.IsSelected = true;
            SelectedNode = again;
        }

        static IEnumerable<VfxTreeNode> All(IEnumerable<VfxTreeNode> nodes) => nodes.SelectMany(n => All(n.Children).Prepend(n));

        static IEnumerable<string> Signature(IEnumerable<VfxTreeNode> nodes) => All(nodes).Select(n => n.Key + "\n" + n.Label);

        List<VfxTreeNode> BuildTree(VfxDocument document)
        {
            var tree = new List<VfxTreeNode>();
            if (document.Root.Child("vfx") is { } vfx)
            {
                tree.Add(new VfxTreeNode(NodeKind.Effect, "Effect", "effect", vfx.Path));
                if (vfx.Child("emitters") is { } emitters)
                {
                    var folder = new VfxTreeNode(NodeKind.Emitters, "Emitters", "emitters", emitters.Path);
                    foreach (var emitter in emitters.ChildrenNamed("emitter"))
                        folder.Children.Add(EmitterNode(document, emitter));
                    tree.Add(folder);
                }
            }
            if (document.Section("vfx_references") is { } references)
            {
                var folder = new VfxTreeNode(NodeKind.References, "References", "references", references.Path);
                var index = 0;
                foreach (var reference in references.ChildrenNamed("vfx"))
                {
                    var node = new VfxTreeNode(NodeKind.Reference, $"{reference.Attr("vfx_ref")} ({reference.Attr("inst_name")})", $"reference#{index++}", reference.Path);
                    foreach (var row in reference.ChildrenNamed("emitter"))
                    {
                        var name = row.Attr("name") ?? "";
                        node.Children.Add(new VfxTreeNode(NodeKind.Override, row.Attr("enable") == "false" ? name + " (off)" : name, $"{node.Key}/{name}", row.Path));
                    }
                    folder.Children.Add(node);
                }
                tree.Add(folder);
            }
            return tree;
        }

        VfxTreeNode EmitterNode(VfxDocument document, SpanElement emitter)
        {
            var id = emitter.Attr("id") ?? "";
            var node = new VfxTreeNode(NodeKind.Emitter, $"{id} ({emitter.Attr("type")})", "emitter:" + id, emitter.Path);
            if (document.ById("behaviour_chains", id) is { } chain)
            {
                var block = TierEdits.Block(chain, Tier);
                var chainNode = new VfxTreeNode(NodeKind.Chain, block == null ? "Behaviour chain (draws nothing at this tier)" : "Behaviour chain",
                    node.Key + "/chain", chain.Path);
                if (block != null)
                {
                    foreach (var (modifier, fragment, occurrence) in TierEdits.Modifiers(block))
                    {
                        chainNode.Children.Add(new VfxTreeNode(NodeKind.Modifier, occurrence == 0 ? fragment : $"{fragment} ({occurrence + 1})",
                            $"{chainNode.Key}/{fragment}#{occurrence}", modifier.Path) { Fragment = fragment, Occurrence = occurrence });
                    }
                }
                node.Children.Add(chainNode);
            }
            if (document.ById("effect_render_stacks", id) is { } stack)
                node.Children.Add(new VfxTreeNode(NodeKind.Stack, "Render stack", node.Key + "/stack", stack.Path));
            return node;
        }

        // Adding and removing

        [ObservableProperty] string? _newPropertyName;
        [ObservableProperty] string? _newFragment;
        [ObservableProperty] string _newEmitterId = "";
        [ObservableProperty] string? _newReferenceTarget;
        [ObservableProperty] string? _newOverrideName;

        public ObservableCollection<string> PropertyNameChoices { get; } = [];
        public ObservableCollection<string> OverrideChoices { get; } = [];
        public IReadOnlyList<string> FragmentChoices => _vocabulary?.Fragments ?? [];
        public IReadOnlyList<string> EffectChoices => _vocabulary?.EffectNames ?? [];

        static string ContextOf(VfxTreeNode node, SpanElement element) => node.Kind switch
        {
            NodeKind.Emitter => "emitter:" + element.Attr("type"),
            NodeKind.Stack => "stack",
            _ => "fragment:" + node.Fragment,
        };

        static SpanElement ChainOf(VfxDocument document, VfxTreeNode modifier) => document.At(modifier.Path).Parent!.Parent!;

        /// <summary>The property names vanilla uses here that the node does not have yet, and a reference's target emitters
        /// that have no row yet.</summary>
        void RefreshAddChoices()
        {
            PropertyNameChoices.Clear();
            OverrideChoices.Clear();
            if (_session.Document is not { } document || SelectedNode is not { } node)
                return;
            var element = document.At(node.Path);
            if (CanAddProperty && _vocabulary != null)
            {
                var present = node.Kind == NodeKind.Modifier
                    ? element.Children.Select(c => c.Attr("name")).OfType<string>().ToHashSet()
                    : TierEdits.EffectiveProperties(element, 0).Select(p => p.Attr("name")!).ToHashSet();
                foreach (var name in _vocabulary.PropertyNames(ContextOf(node, element)).Where(n => !present.Contains(n)))
                    PropertyNameChoices.Add(name);
            }
            if (node.Kind == NodeKind.Reference && _session.EmitterIdsOf(element.Attr("vfx_ref") ?? "") is { } ids)
            {
                var present = element.ChildrenNamed("emitter").Select(r => r.Attr("name")).ToHashSet();
                foreach (var id in ids.Where(i => !present.Contains(i)))
                    OverrideChoices.Add(id);
            }
        }

        [RelayCommand]
        void AddProperty()
        {
            if (SelectedNode is not { } node || _vocabulary is not { } vocabulary || string.IsNullOrEmpty(NewPropertyName))
                return;
            var name = NewPropertyName;
            var added = Edit(document =>
            {
                var element = document.At(node.Path);
                var template = vocabulary.PropertyTemplate(ContextOf(node, element), name) ?? throw new ArgumentException($"vanilla never writes {name} here");
                return node.Kind == NodeKind.Modifier
                    ? VfxEdits.AddModifierProperty(document, ChainOf(document, node), Tier, node.Fragment!, node.Occurrence, template, ThisTierOnly)
                    : [VfxEdits.AddProperty(document, element, template, ThisTierOnly ? Tier : 0)];
            }, node);
            if (added && ThisTierOnly && Tier == 0 && node.Kind != NodeKind.Modifier)
                Status = $"{name} added at Ultra; High and Medium inherit it, having no value of their own to keep";
        }

        [RelayCommand]
        void RemoveProperty(PropertyRow? row)
        {
            if (row is not { CanRemove: true } || SelectedNode is not { } node)
                return;
            Edit(document => node.Kind == NodeKind.Modifier
                ? VfxEdits.RemoveModifierProperty(document, ChainOf(document, node), Tier, node.Fragment!, node.Occurrence, row.Property, ThisTierOnly)
                : VfxEdits.RemoveProperty(document, document.At(node.Path), row.Property, Tier, ThisTierOnly), node);
        }

        [RelayCommand]
        void AddModifier()
        {
            if (SelectedNode is not { Kind: NodeKind.Chain } node || _vocabulary is not { } vocabulary || string.IsNullOrEmpty(NewFragment))
                return;
            var fragment = NewFragment;
            Edit(document => VfxEdits.AddModifier(document, document.At(node.Path), Tier, fragment, vocabulary.ModifierTemplate(fragment), ThisTierOnly));
        }

        [RelayCommand]
        void RemoveModifier()
        {
            if (SelectedNode is not { Kind: NodeKind.Modifier } node)
                return;
            Edit(document => VfxEdits.RemoveModifier(document, ChainOf(document, node), Tier, node.Fragment!, node.Occurrence, ThisTierOnly), node);
        }

        [RelayCommand]
        void CopyUltraBlock()
        {
            if (SelectedNode is not { Kind: NodeKind.Chain } node)
                return;
            Edit(document => [VfxEdits.CopyBlock(document, document.At(node.Path), 0, Tier)]);
        }

        [RelayCommand]
        void DuplicateEmitter()
        {
            if (SelectedNode is not { Kind: NodeKind.Emitter } node)
                return;
            var newId = NewEmitterId;
            Edit(document => VfxEdits.DuplicateEmitter(document, document.At(node.Path).Attr("id") ?? "", newId));
        }

        [RelayCommand]
        void RemoveEmitter()
        {
            if (SelectedNode is not { Kind: NodeKind.Emitter } node)
                return;
            Edit(document => VfxEdits.RemoveEmitter(document, document.At(node.Path).Attr("id") ?? ""));
        }

        [RelayCommand]
        void AddReference()
        {
            var target = NewReferenceTarget?.Trim();
            if (string.IsNullOrEmpty(target))
                return;
            if (_session.EffectName is { } self && GameWorld.Core.Vfx.VfxResolver.Key(target) == GameWorld.Core.Vfx.VfxResolver.Key(self))
            {
                Status = $"{target} is this effect; a reference to itself would loop";
                return;
            }
            if (_session.EmitterIdsOf(target) is not { } ids)
            {
                Status = $"{GameWorld.Core.Vfx.VfxResolver.PathOf(target)} is not in the loaded packs, or does not read";
                return;
            }
            Edit(document => [VfxEdits.AddReference(document, target, ids)]);
        }

        [RelayCommand]
        void RemoveReference()
        {
            if (SelectedNode is not { Kind: NodeKind.Reference } node)
                return;
            Edit(document => [document.Remove(document.At(node.Path))]);
        }

        [RelayCommand]
        void AddOverride()
        {
            if (SelectedNode is not { Kind: NodeKind.Reference } node || string.IsNullOrEmpty(NewOverrideName))
                return;
            var name = NewOverrideName;
            Edit(document => [VfxEdits.AddOverride(document, document.At(node.Path), name)]);
        }

        [RelayCommand]
        void RemoveOverride()
        {
            if (SelectedNode is not { Kind: NodeKind.Override } node)
                return;
            Edit(document => [document.Remove(document.At(node.Path))]);
        }

        [RelayCommand]
        void AllOverridesOn() => SetAllOverrides(true);

        [RelayCommand]
        void AllOverridesOff() => SetAllOverrides(false);

        void SetAllOverrides(bool enable)
        {
            if (SelectedNode is not { Kind: NodeKind.Reference } node)
                return;
            Edit(document => VfxEdits.SetAllOverrides(document, document.At(node.Path), enable));
        }

        // The preview

        public ObservableCollection<string> PreviewLines { get; } = [];
        public double PreviewTime { get => _preview.Time; set => _preview.SetTime((float)value); }
        public bool IsPlaying { get => _preview.IsPlaying; set { _preview.IsPlaying = value; OnPropertyChanged(); } }
        public bool Loop { get => _preview.Loop; set { _preview.Loop = value; OnPropertyChanged(); } }
        public double LoopLength
        {
            get => _preview.LoopLength;
            set
            {
                _preview.LoopLength = Math.Max(0.1f, (float)value);
                OnPropertyChanged();
                if (Projectile)
                    RefreshPreview();   // one loop is one flight
            }
        }
        public double Speed { get => _preview.Speed; set { _preview.Speed = (float)value; OnPropertyChanged(); } }

        public string[] AxisNames { get; } = ProjectileFlight.Axes.Select(a => a.Name).ToArray();
        public bool Projectile { get => _preview.Projectile; set { _preview.Projectile = value; OnPropertyChanged(); RefreshPreview(); } }
        public double ProjectileSpeed { get => _preview.ProjectileSpeed; set { _preview.ProjectileSpeed = (float)value; OnPropertyChanged(); RefreshPreview(); } }
        public int ProjectileAxis
        {
            get => _preview.ProjectileAxis;
            set
            {
                _preview.ProjectileAxis = Math.Clamp(value, 0, ProjectileFlight.Axes.Count - 1);
                OnPropertyChanged();
                RefreshPreview();
            }
        }

        public bool ShowGrid { get => _grid.ShowGrid; set { _grid.ShowGrid = value; OnPropertyChanged(); } }

        public bool StaticView { get => _preview.StaticView; set { _preview.StaticView = value; OnPropertyChanged(); _preview.SetTime(_preview.Time); } }

        [ObservableProperty] string? _modelName;

        /// <summary>A model from the packs to stand where the effect is, as a guide. The preview only; nothing is written.</summary>
        [RelayCommand]
        void LoadModel()
        {
            var picked = _dialogs.DisplayBrowseDialog([".rigid_model_v2"]);
            if (!picked.Result || picked.File == null)
                return;
            _preview.LoadModel(picked.File);
            ModelName = _preview.Model == null ? null : picked.File.Name;
            ShowPreviewLines();
        }

        [RelayCommand]
        void ClearModel()
        {
            _preview.ClearModel();
            ModelName = null;
            RefreshPreview();
        }

        // 300 ms after the last edit, so typing a value re-simulates once.
        void SchedulePreview()
        {
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        void RefreshPreview()
        {
            if (_session.Document == null)
                return;
            _preview.Show(_session.EffectName, _session.Document.Save(), Tier);
            ShowPreviewLines();
        }

        void OnPreviewTimeChanged()
        {
            OnPropertyChanged(nameof(PreviewTime));
            if (_preview.Lines.Count != _shownLines)
                ShowPreviewLines();
        }

        void ShowPreviewLines()
        {
            _shownLines = _preview.Lines.Count;
            PreviewLines.Clear();
            foreach (var line in _preview.Lines.Distinct())
                PreviewLines.Add(line);
        }

        // Saving, undo

        public bool Save() => Store(_session.Save);

        /// <summary>Picks a file from the loaded packs, the way a tab opened from the Tools menu gets one.</summary>
        [RelayCommand]
        void OpenFile()
        {
            FlushPendingEdits?.Invoke();
            if (HasUnsavedChanges && _dialogs.ShowYesNoBox($"{DisplayName} has edits that are not saved. Open another file and lose them?", "Unsaved edits") != ShowMessageBoxResult.OK)
                return;
            var picked = _dialogs.DisplayBrowseDialog([".xml"]);
            if (picked.Result && picked.File != null)
                LoadFile(picked.File);
        }

        [RelayCommand] void SaveFile() => Save();
        [RelayCommand] void SaveFileAs() => Store(_session.SaveAs);
        [RelayCommand]
        void Undo()
        {
            FlushPendingEdits?.Invoke();
            _session.Document?.Undo();
        }

        [RelayCommand]
        void Redo()
        {
            FlushPendingEdits?.Invoke();
            _session.Document?.Redo();
        }

        bool Store(Func<bool> save)
        {
            FlushPendingEdits?.Invoke();
            if (_session.Document == null)
                return false;
            try
            {
                if (!save())
                    return false;
                DisplayName = _session.File!.Name;
                Status = "Saved " + _session.EffectName;
                OnPropertyChanged(nameof(HasUnsavedChanges));
                OnPropertyChanged(nameof(CurrentFile));
                return true;
            }
            catch (Exception e)
            {
                _logger.Error(e, "Saving {Name} failed", _session.EffectName);
                _dialogs.ShowExceptionWindow(e, "The effect was not saved. Is an editable pack selected?");
                return false;
            }
        }

        public void Close()
        {
        }

        public void Dispose()
        {
            _previewTimer.Stop();
            _preview.TimeChanged -= OnPreviewTimeChanged;
            if (_session.Document != null)
                _session.Document.Changed -= OnDocumentChanged;
        }
    }
}
