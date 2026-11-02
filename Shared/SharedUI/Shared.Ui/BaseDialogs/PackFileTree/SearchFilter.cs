using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Shared.Core.Misc;
using Shared.Core.Services;

namespace Shared.Ui.BaseDialogs.PackFileTree
{
    // Not an IDataErrorInfo: the search box validated through it, and its indexer ran the whole filter a second time after every
    // search.
    public class SearchFilter : NotifyPropertyChangedImpl, IDisposable
    {
        private readonly ObservableCollection<RootTreeNode> _rootNodes;
        private readonly IStandardDialogs? _standardDialogs;

        private CancellationTokenSource? _debounceCts;
        private readonly object _debounceLock = new();
        private const int DebounceMilliseconds = 250;
        private bool _useDebounce;

        string _filterText = "";
        public string FilterText
        {
            get => _filterText;
            set
            {
                SetAndNotify(ref _filterText, value);
                if (_useDebounce)
                    DebounceFilter();
                else
                    ApplyFilter(_filterText);
            }
        }

        public bool UseDebounce
        {
            get => _useDebounce;
            set => _useDebounce = value;
        }

        private bool _showFoldersOnly;
        public bool ShowFoldersOnly
        {
            get => _showFoldersOnly;
            set
            {
                SetAndNotify(ref _showFoldersOnly, value);
                ApplyFilter(FilterText);
            }
        }

        List<string>? _extensionFilter;

        // Narrows the extension match further by full path. The extension match is a substring
        // match on purpose (legacy .wem variants), so this is how a caller asks for something
        // stricter, or for one folder only.
        Func<string, bool>? _pathFilter;

        public int AutoExapandResultsAfterLimitedCount { get; set; } = 25;
        public bool HasActiveFilter => !string.IsNullOrWhiteSpace(FilterText) || ShowFoldersOnly || (_extensionFilter?.Count > 0) || _pathFilter != null;
        public Action? FilterCleared { get; set; }

        internal SearchFilter(ObservableCollection<RootTreeNode> nodes, IStandardDialogs? standardDialogs = null)
        {
            _rootNodes = nodes;
            _standardDialogs = standardDialogs;
        }

        private async void DebounceFilter()
        {
            CancellationToken token;
            lock (_debounceLock)
            {
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = new CancellationTokenSource();
                token = _debounceCts.Token;
            }

            try
            {
                await Task.Delay(DebounceMilliseconds, token);
                if (!token.IsCancellationRequested)
                    ApplyFilter(FilterText);
            }
            catch (TaskCanceledException)
            {
                // Debounce cancelled — next keystroke will restart
            }
        }

        void ApplyFilter(string text)
        {
            using var _ = _standardDialogs?.ShowWaitCursor();
            var rootNodes = _rootNodes.ToList();

            if (HasActiveFilter)
            {
                var textFilter = string.IsNullOrWhiteSpace(text) ? null : text;
                var hasSearchFilter = textFilter != null || (_extensionFilter?.Count > 0) || _pathFilter != null;

                if (hasSearchFilter)
                {
                    foreach (var rootNode in rootNodes)
                    {
                        ShowMatches(rootNode, textFilter, _extensionFilter, _pathFilter);
                        rootNode.IsVisible = true;
                    }
                }

                if (ShowFoldersOnly)
                {
                    foreach (var rootNode in rootNodes)
                        ApplyFoldersOnlyFilter(rootNode);
                }

                if (AutoExapandResultsAfterLimitedCount != -1)
                {
                    var visibleNodes = 0;
                    foreach (var item in rootNodes)
                        visibleNodes += CountVisibleNodes(item);

                    foreach (var item in rootNodes)
                        item.ExpandForFilter();

                    if (visibleNodes <= AutoExapandResultsAfterLimitedCount)
                    {
                        foreach (var item in rootNodes)
                            item.ExpandIfVisible(markAsFilterExpansion: true);
                    }
                }
            }
            else
            {
                foreach (var rootNode in rootNodes)
                    SetVisibilityRecursive(rootNode, true);

                foreach (var item in rootNodes)
                    item.AbsorbFilterExpansion();

                FilterCleared?.Invoke();
            }
        }

        public void Reapply()
        {
            ApplyFilter(FilterText);
        }

        public void SetExtensions(List<string> extentions, Func<string, bool>? pathFilter = null)
        {
            _extensionFilter = extentions;
            _pathFilter = pathFilter;
            ApplyFilter(FilterText);
        }

        // Matched on the tree, which holds every file already, in one walk that shows the matches and hides the rest. Asking the
        // container instead cost the game's cache a LIKE over 750,000 rows, 250 ms a search, and LIKE read the _ in a name such as
        // bst_minotaur as a wildcard. The path filter sees only the files the name and extensions already let through.
        // Optimized from its first call: each node is a new call, so the session's first search ran on the quick first-tier code
        // until the background recompile caught up. 750,000 files cold took 110-160 ms that way, 70-90 ms this way.
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static bool ShowMatches(TreeNode node, string? text, List<string>? extensions, Func<string, bool>? pathFilter)
        {
            bool visible;
            if (node.NodeType == NodeType.File)
            {
                visible = text == null || node.Name.Contains(text, StringComparison.OrdinalIgnoreCase);
                if (visible && extensions?.Count > 0)
                {
                    visible = false;
                    foreach (var extension in extensions)
                    {
                        if (node.Name.Contains(extension, StringComparison.OrdinalIgnoreCase))
                        {
                            visible = true;
                            break;
                        }
                    }
                }
                if (visible && pathFilter != null)
                    visible = pathFilter(node.GetFullPath());
            }
            else
            {
                visible = false;
                foreach (var child in node.Children)
                    visible |= ShowMatches(child, text, extensions, pathFilter);
            }

            if (node.IsVisible != visible)
                node.IsVisible = visible;
            return visible;
        }

        private static void SetVisibilityRecursive(TreeNode node, bool visible)
        {
            if (node.IsVisible != visible)
                node.IsVisible = visible;

            foreach (var child in node.Children)
                SetVisibilityRecursive(child, visible);
        }

        private static void ApplyFoldersOnlyFilter(TreeNode node)
        {
            if (node.NodeType == NodeType.File)
            {
                node.IsVisible = false;
                return;
            }

            node.IsVisible = true;
            foreach (var child in node.Children)
                ApplyFoldersOnlyFilter(child);
        }

        private static int CountVisibleNodes(TreeNode node)
        {
            if (node.NodeType == NodeType.File && node.IsVisible)
                return 1;

            var count = 0;
            foreach (var child in node.Children)
                count += CountVisibleNodes(child);

            return count;
        }

        public void Dispose()
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }
    }
}
