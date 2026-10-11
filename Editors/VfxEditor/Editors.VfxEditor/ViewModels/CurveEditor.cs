using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.VfxEditor.Document;

namespace Editors.VfxEditor.ViewModels
{
    /// <summary>A curve's channels and the picked channel's keys. Every change rebuilds the curve text and commits it as the
    /// property's new text; the mapping part is shown, not edited.</summary>
    public sealed partial class CurveEditor : ObservableObject
    {
        readonly Action<string> _commit;
        readonly Action<string> _report;
        CurveText _curve;

        public CurveEditor(CurveText curve, Action<string> commit, Action<string> report)
        {
            _curve = curve;
            _commit = commit;
            _report = report;
            SelectedChannel = curve.Channels.FirstOrDefault();
        }

        public IReadOnlyList<CurveText.Channel> Channels => _curve.Channels;
        public string Mapping => _curve.Mapping;
        public ObservableCollection<CurveKeyRow> Keys { get; } = [];

        [ObservableProperty] CurveText.Channel? _selectedChannel;
        [ObservableProperty] CurveKeyRow? _selectedKey;

        partial void OnSelectedChannelChanged(CurveText.Channel? value) => ShowKeys();

        [RelayCommand]
        void AddKey()
        {
            if (SelectedChannel is not { } channel)
                return;
            _curve.InsertKeyAfter(channel, SelectedKey?.Index ?? channel.Keys.Count - 1);
            _commit(_curve.ToString());
        }

        [RelayCommand]
        void DeleteKey()
        {
            if (SelectedChannel is not { Keys.Count: > 1 } channel || SelectedKey == null)
            {
                _report("Pick a key to delete; a channel keeps at least one");
                return;
            }
            channel.Keys.RemoveAt(SelectedKey.Index);
            _commit(_curve.ToString());
        }

        internal void SetField(int key, int field, string value)
        {
            SelectedChannel!.Keys[key][field] = value;
            _commit(_curve.ToString());
        }

        internal void Report(string message) => _report(message);

        /// <summary>The document changed: read the curve again, keeping the picked channel and key.</summary>
        internal void Reload(string text)
        {
            if (CurveText.Parse(text) is not { } curve)
                return;
            var channel = SelectedChannel == null ? 0 : Math.Max(0, _curve.Channels.IndexOf(SelectedChannel));
            var key = SelectedKey?.Index;
            _curve = curve;
            OnPropertyChanged(nameof(Channels));
            OnPropertyChanged(nameof(Mapping));
            SelectedChannel = curve.Channels.ElementAtOrDefault(channel);
            SelectedKey = key is { } k ? Keys.ElementAtOrDefault(k) : null;
        }

        // In place when the count is unchanged, so a box being typed in keeps its focus.
        void ShowKeys()
        {
            var keys = SelectedChannel?.Keys ?? [];
            if (Keys.Count == keys.Count)
            {
                for (var i = 0; i < keys.Count; i++)
                    Keys[i].Show(keys[i]);
                return;
            }
            Keys.Clear();
            for (var i = 0; i < keys.Count; i++)
                Keys.Add(new CurveKeyRow(this, i, keys[i]));
        }
    }

    public sealed class CurveKeyRow(CurveEditor owner, int index, string[] fields) : ObservableObject
    {
        string[] _fields = fields;

        public int Index { get; } = index;
        public string Time { get => _fields[0]; set => Edit(0, value); }
        public string Value { get => _fields[1]; set => Edit(1, value); }
        public string In { get => _fields[2]; set => Edit(2, value); }
        public string Out { get => _fields[3]; set => Edit(3, value); }
        public string TanInX { get => _fields[4]; set => Edit(4, value); }
        public string TanInY { get => _fields[5]; set => Edit(5, value); }
        public string TanOutX { get => _fields[6]; set => Edit(6, value); }
        public string TanOutY { get => _fields[7]; set => Edit(7, value); }
        public string Flag { get => _fields[8]; set => Edit(8, value); }

        internal void Show(string[] fields)
        {
            _fields = fields;
            OnPropertyChanged(string.Empty);
        }

        void Edit(int field, string value)
        {
            if (value == _fields[field])
                return;
            if (!CurveText.IsValidField(field, value))
            {
                owner.Report($"{value} is not a valid {CurveText.FieldNames[field]}, so the key keeps {_fields[field]}");
                OnPropertyChanged(string.Empty);
                return;
            }
            owner.SetField(Index, field, value.Trim());
        }
    }
}
