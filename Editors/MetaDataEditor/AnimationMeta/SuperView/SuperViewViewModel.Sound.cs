using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Editors.AnimationMeta.Presentation;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Editors.Audio.SoundTriggers;
using Editors.Shared.Core.Common;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;

namespace Editors.AnimationMeta.SuperView
{
    /// <summary>One case of a switch group, as a picker offers it. The case the container plays when nothing is picked says so.</summary>
    public sealed record SoundSwitchOption(uint SwitchId, string Name, bool IsDefault)
    {
        public override string ToString() => IsDefault ? Name + " (default)" : Name;
    }

    /// <summary>A switch group the playing clip's sound triggers reach, such as ground surface: the case picked is what the next trigger plays.</summary>
    public sealed partial class SoundSwitchPicker(string groupName, IReadOnlyList<SoundSwitchOption> options, SoundSwitchOption selected, Action<SoundSwitchOption> picked)
        : ObservableObject
    {
        public string GroupName { get; } = groupName;
        public IReadOnlyList<SoundSwitchOption> Options { get; } = options;
        [ObservableProperty] SoundSwitchOption _selected = selected;

        partial void OnSelectedChanged(SoundSwitchOption value) => picked(value);
    }

    // Sound: with the Sound box on, every SOUND_TRIGGER of the four files plays its sound when the playhead crosses it, resolved as the
    // game resolves it (spec 2026-11-20). Off by default, as turning it on loads the game's audio, which takes minutes after a game update.
    public partial class SuperViewViewModel
    {
        [ObservableProperty] bool _soundOn;

        /// <summary>The .snd.meta problems, then, while Sound is on, the triggers' summary and a line for each one silent in game.</summary>
        [ObservableProperty] IReadOnlyList<string> _soundStatus = [];

        public ObservableCollection<SoundSwitchPicker> SoundSwitchPickers { get; } = [];

        /// <summary>Loads the game's audio and the tags table, off the UI thread, into the resolver the triggers go through.</summary>
        public Func<CancellationToken, Task<SoundTriggerResolver>> LoadSounds { get; set; } = null!;

        /// <summary>Has a source decoded ahead of its first firing. The default only queues it, and the queue is decoded off the UI thread
        /// once the triggers are resolved: a vanilla attack's triggers can reach 675 sources, which took 7.2 s. A source fired before it is
        /// decoded plays nothing.</summary>
        public Func<uint, bool> PrepareSound { get; set; } = null!;
        public Action<uint> PlaySound { get; set; } = null!;
        public Action StopSounds { get; set; } = null!;

        /// <summary>A switch group's or case's name, for the pickers.</summary>
        public Func<uint, string> SoundName { get; set; } = null!;

        /// <summary>Opens the output the default delegates play through, on the first Sound on. It throws when no audio device opens.</summary>
        public Func<SoundTriggerMixer> OpenMixer { get; set; } = () => new SoundTriggerMixer();

        /// <summary>Finishes when every source queued so far is decoded, or skipped as no longer wanted.</summary>
        public Task SoundDecoding { get; private set; } = Task.CompletedTask;

        SoundTriggerMixer? _mixer;
        IAudioRepository _audio = null!;
        readonly List<uint> _toDecode = [];
        // The sources the current triggers can play. A decode still queued for any other, left by a clip played before, is skipped.
        volatile HashSet<uint> _wantedSources = [];
        SoundTriggerSession? _session;
        CancellationTokenSource? _soundLoad;
        // Why sound could not be turned on, shown while it is off.
        string? _soundRefused;
        // The persistent row's .snd.meta, as OnSceneObjectUpdated parsed it; null when there is none or it does not parse.
        ParsedMetadataFile? _persistSoundMeta;
        IReadOnlyList<SwitchGroupSeen> _shownSwitches = [];
        // The crossing rule caps the time since the last Advance itself; a play after a pause relies on it being long.
        readonly Stopwatch _sinceAdvance = Stopwatch.StartNew();
        // A repository loads one way at a time, and Sound off then on again can start a second load before the first ends.
        static readonly object s_audioLoad = new();

        void InitSound(IAudioRepository audio)
        {
            _audio = audio;
            LoadSounds = token => Task.Run(() =>
            {
                lock (s_audioLoad)
                    audio.Load([Wh3LanguageInformation.GetLanguageAsString(Wh3Language.EnglishUK)]);
                token.ThrowIfCancellationRequested();
                var tags = AudioMetadataTags.Read(_packFileService);
                return new SoundTriggerResolver(tags.Find, id => audio.GetHircs(id).FirstOrDefault(), id => audio.FindWem(id.ToString()) != null);
            }, token);
            PrepareSound = id =>
            {
                _toDecode.Add(id);
                return _mixer != null;
            };
            PlaySound = id => _mixer?.Play(id);
            StopSounds = () => _mixer?.StopAll();
            SoundName = audio.GetNameFromId;

            // What fires follows the tabs' unsaved edits, a new slot or file (each scene update), and the pickers.
            foreach (var editor in new[] { PersistentMetaEditor, MetaEditor, SoundMetaEditor })
                editor.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MetaDataEditorViewModel.HasUnsavedChanges))
                        RebuildSounds();
                };
            _eventHub.Register<SceneObjectUpdateEvent>(this, _ => RebuildSounds());
            // A new clip or slot stops what the last one left ringing.
            _asset.Data.AnimationChanged += _ => StopSounds();
            _asset.Data.Player.OnFrameChanged += _ => AdvanceSounds();
            ShowSoundStatus();
        }

        partial void OnSoundOnChanged(bool value)
        {
            if (!value)
            {
                _soundLoad?.Cancel();
                _soundLoad = null;
                StopSounds();
                ShowSwitchPickers([]);
                ShowSoundStatus();
                return;
            }

            _soundRefused = null;
            try
            {
                _mixer ??= OpenMixer();
            }
            catch (Exception e)
            {
                Refuse($"No audio device could be opened: {e.Message}");
                return;
            }
            if (_session == null)
                LoadSession();
            RebuildSounds();
        }

        // Sound goes back off and says why.
        void Refuse(string why)
        {
            _soundRefused = why;
            SoundOn = false;
            ShowSoundStatus();
        }

        // A load that Sound off or Close cancelled, or that a later Sound on replaced, builds nothing when it ends.
        async void LoadSession()
        {
            var load = _soundLoad = new CancellationTokenSource();
            SoundTriggerResolver resolver;
            try
            {
                resolver = await LoadSounds(load.Token);
            }
            catch (Exception e)
            {
                if (load == _soundLoad && !load.IsCancellationRequested)
                {
                    _soundLoad = null;
                    Refuse($"The game's sounds could not be loaded: {e.Message}");
                }
                return;
            }
            if (load != _soundLoad || load.IsCancellationRequested)
                return;

            _soundLoad = null;
            _session = new SoundTriggerSession(resolver, id => PrepareSound(id), id => PlaySound(id), new Random());
            RebuildSounds();
        }

        void RebuildSounds()
        {
            if (SoundOn && _session != null)
            {
                _session.Rebuild(SoundTriggers(), SlotGrid.Set is CampaignAnimationSet);
                DecodeQueued(_session.Leaves.ToHashSet());
                ShowSwitchPickers(_session.Switches);
            }
            ShowSoundStatus();
        }

        // The sources PrepareSound queued, decoded off the UI thread one batch at a time, in order, into the mixer's lock-guarded cache. Each
        // batch ends by dropping what the triggers no longer play, so a batch from before cannot put back a source a later one dropped.
        void DecodeQueued(HashSet<uint> wanted)
        {
            var (mixer, ids, audio) = (_mixer, _toDecode.ToList(), _audio);
            _toDecode.Clear();
            _wantedSources = wanted;
            if (mixer == null)
                return;
            SoundDecoding = SoundDecoding.ContinueWith(_ =>
            {
                foreach (var id in ids)
                {
                    // Closed: the mixer is gone.
                    if (mixer != _mixer)
                        return;
                    if (_wantedSources.Contains(id))
                        mixer.Prepare(id, () => ReadWem(audio, id));
                }
                mixer.Keep(wanted);
            }, TaskScheduler.Default);
        }

        // A read that fails is a WEM that cannot be played, which Prepare reports as false.
        static byte[]? ReadWem(IAudioRepository audio, uint id)
        {
            try
            {
                return audio.FindWem(id.ToString())?.DataSource.ReadData();
            }
            catch (Exception)
            {
                return null;
            }
        }

        List<SoundTriggerTag> SoundTriggers() =>
        [
            .. Triggers(PersistentMetaEditor.ParsedFile, "persistent meta"),
            .. Triggers(MetaEditor.ParsedFile, "clip meta"),
            .. Triggers(SoundMetaEditor.ParsedFile, "slot .snd.meta"),
            .. Triggers(_persistSoundMeta, "persistent .snd.meta"),
        ];

        // Versions 4, 10 and 11 (which is a 10).
        static IEnumerable<SoundTriggerTag> Triggers(ParsedMetadataFile? file, string label) => (file?.Attributes ?? []).Select(x => x switch
        {
            SoundTrigger_v10 tag => new SoundTriggerTag(tag.SoundEvent, tag.StartTime, label),
            SoundTrigger_v4 tag => new SoundTriggerTag(tag.SoundEvent, tag.StartTime, label),
            _ => null,
        }).OfType<SoundTriggerTag>();

        void AdvanceSounds()
        {
            if (!SoundOn || _session == null)
                return;
            var player = _asset.Data.Player;
            var elapsed = _sinceAdvance.Elapsed.TotalSeconds;
            _sinceAdvance.Restart();
            _session.Advance(player.GetTimeUs() / 1_000_000d, player.IsPlaying, elapsed, Player.Speed.Value, player.GetAnimationLengthUs() / 1_000_000d);
        }

        void ShowSoundStatus()
        {
            IReadOnlyList<string> lines = [.. SoundFileProblems, .. SessionLines()];
            if (!lines.SequenceEqual(SoundStatus))
                SoundStatus = lines;

            IEnumerable<string> SessionLines()
            {
                if (SoundOn)
                    return _session?.StatusLines ?? ["Loading sounds..."];
                return _soundRefused == null ? [] : [_soundRefused];
            }
        }

        // The pickers are made again only when the groups or their cases change, so a pick, which rebuilds, keeps its picker.
        void ShowSwitchPickers(IReadOnlyList<SwitchGroupSeen> switches)
        {
            switches = [.. switches.Where(x => x.Values.Count != 0)];
            if (switches.Count == _shownSwitches.Count && switches.Zip(_shownSwitches).All(x => x.First.GroupId == x.Second.GroupId && x.First.Values.SequenceEqual(x.Second.Values)))
                return;

            _shownSwitches = switches;
            var session = _session!;
            SoundSwitchPickers.Clear();
            foreach (var group in switches)
            {
                var options = group.Values.Select(x => new SoundSwitchOption(x, SoundName(x), x == group.DefaultSwitch)).ToList();
                var picked = session.SwitchChoices.TryGetValue(group.GroupId, out var choice) ? options.FirstOrDefault(x => x.SwitchId == choice) : null;
                SoundSwitchPickers.Add(new SoundSwitchPicker(SoundName(group.GroupId), options, picked ?? options.FirstOrDefault(x => x.IsDefault) ?? options[0], x =>
                {
                    session.SwitchChoices[group.GroupId] = x.SwitchId;
                    RebuildSounds();
                }));
            }
        }

        // A decode still queued then finds no mixer and is skipped.
        void CloseSound()
        {
            _soundLoad?.Cancel();
            _soundLoad = null;
            _mixer?.Dispose();
            _mixer = null;
        }
    }
}
