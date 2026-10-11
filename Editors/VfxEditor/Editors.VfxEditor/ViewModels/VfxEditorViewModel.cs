using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.VfxEditor.Document;
using GameWorld.Core.Components;
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
    /// <summary>
    /// The VFX editor tab (docs/superpowers/specs/2026-10-11-vfx-editor-design.md): the effect as a tree, the picked node's
    /// values, quality tiers, and saving. Every change goes through the document as splices, so the file keeps every byte
    /// nobody edited.
    /// </summary>
    public sealed partial class VfxEditorViewModel : ObservableObject, IEditorInterface, IFileEditor, ISaveableEditor, IDisposable
    {
        static readonly string[] s_nodeFlags = [nameof(IsEmitterNode), nameof(IsChainNode), nameof(IsModifierNode), nameof(IsReferencesNode),
            nameof(IsReferenceNode), nameof(IsOverrideNode), nameof(CanAddProperty), nameof(IsBlockMissing)];

        readonly ILogger _logger = Logging.Create<VfxEditorViewModel>();
        readonly VfxFileSession _session;
        readonly IStandardDialogs _dialogs;

        public VfxEditorViewModel(IPackFileService packFileService, IFileSaveService fileSaveService, IStandardDialogs dialogs,
            IWpfGame gameWorld, IComponentInserter componentInserter)
        {
            _session = new VfxFileSession(packFileService, fileSaveService);
            _dialogs = dialogs;
            Scene = gameWorld;
            componentInserter.Execute();
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
        [ObservableProperty] string? _loadError;

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
            Tree.Clear();
            Rows.Clear();
            if (_session.Document == null)
                return;
            _session.Document.Changed += OnDocumentChanged;
            RebuildTree();
            Status = $"{_session.EffectName} ({_session.Document.Encoding})";
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
        }

        partial void OnSelectedNodeChanged(VfxTreeNode? value)
        {
            RefreshRows(rebuild: true);
            NotifyNodeFlags();
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
        }

        // ---- edits

        /// <summary>Builds splices against the current document and applies them as one undo step. A refusal (a taken id,
        /// a tier with no block, an edit that would break the file) goes to the status line and changes nothing.</summary>
        void Edit(Func<VfxDocument, IEnumerable<Splice>> build)
        {
            var document = _session.Document;
            if (document == null)
                return;
            try
            {
                document.Apply(build(document).ToList());
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                _logger.Warning("VFX edit refused: {Reason}", e.Message);
                Status = e.Message;
                RefreshRows();
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
            });
        }

        // ---- the value rows

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
            IReadOnlyList<string>? choices = null;
            if (choices != null && !choices.Contains(value))
                choices = [value, .. choices];
            var kind = attribute == null ? (CurveText.Parse(value) != null ? RowKind.Curve : RowKind.Text)
                : value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase) ? RowKind.Bool
                : float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? RowKind.Number
                : choices != null ? RowKind.Choice
                : RowKind.Text;
            return new PropertyRow(property, attribute, label, kind, value, choices, note, Commit, message => Status = message);
        }

        // ---- the tree

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

        // ---- saving, undo

        public bool Save() => Store(_session.Save);

        [RelayCommand] void SaveFile() => Save();
        [RelayCommand] void SaveFileAs() => Store(_session.SaveAs);
        [RelayCommand] void Undo() => _session.Document?.Undo();
        [RelayCommand] void Redo() => _session.Document?.Redo();

        bool Store(Func<bool> save)
        {
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
            if (_session.Document != null)
                _session.Document.Changed -= OnDocumentChanged;
        }
    }
}
