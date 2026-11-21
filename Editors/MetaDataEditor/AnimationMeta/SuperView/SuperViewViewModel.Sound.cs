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

    // Sound: with the Sound box on, the SOUND_TRIGGERs of the four files play their sounds as the playhead crosses them, Loop's restart
    // included, resolved as the game resolves them (spec 2026-11-20). A trigger whose event goes through a Wwise type SuperView does not
    // play is listed as not supported. Off by default, as turning it on loads the game's audio, which takes minutes after a game update.
    public partial class SuperViewViewModel
    {
        [ObservableProperty] bool _soundOn;

        /// <summary>The .snd.meta problems, then, while Sound is on, the triggers' summary and a line for each one that plays nothing here:
        /// silent in game, or not supported.</summary>
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

        /// <summary>How long before its tag a sound starts, in real seconds, to make up for the time a fired sound takes to reach WASAPI
        /// loopback; the session turns it into clip time at the playback speed. A sound fired by the session reached WASAPI loopback a median 23.1 to
        /// 27.9 ms later in six measurements of 20 firings at the mixer's 30 ms WASAPI latency (SoundTimingScratch, 2026-11-21). When
        /// WASAPI cannot open, the mixer falls back to WaveOut at 60 ms or more, and this lead is too short there. The marker lights on
        /// the frame that fires the sound, so this lines the sound up with that frame. How late the picture reaches the screen was not
        /// measured, so the sound may land ahead of the lit marker by that much.</summary>
        public const double SoundLeadSeconds = 0.026;

        /// <summary>Opens the output the default delegates play through, on the first Sound on. It throws when no audio device opens.</summary>
        public Func<SoundTriggerMixer> OpenMixer { get; set; } = () => new SoundTriggerMixer();

        /// <summary>Finishes when the background work queued so far has ended: each rebuild's first resolves, then its decodes, or their
        /// skipping once a later rebuild, Sound off or Close has made them stale.</summary>
        public Task SoundDecoding { get; private set; } = Task.CompletedTask;

        SoundTriggerMixer? _mixer;
        IAudioRepository _audio = null!;
        readonly List<uint> _toDecode = [];
        SoundTriggerResolver? _resolver;
        SoundTriggerSession? _session;
        // The UI thread, where the background work hands its results back, and the session lives.
        SynchronizationContext? _ui;
        // Counts the rebuilds asked for. Work for an older one is skipped. Written on the UI thread only.
        volatile int _rebuilds;
        // The rebuild the session last took. Until it is the latest, the session holds the last clip's triggers. UI thread only.
        int _applied;
        // Background jobs queued and not yet back on the UI thread, for the status. UI thread only.
        int _backgroundJobs;
        CancellationTokenSource? _soundLoad;
        // Why sound could not be turned on, shown while it is off.
        string? _soundRefused;
        // Why the latest rebuild's background work failed, shown while sound is on, until the next rebuild.
        string? _soundFailed;
        // The persistent row's .snd.meta, as OnSceneObjectUpdated parsed it; null when there is none or it does not parse.
        ParsedMetadataFile? _persistSoundMeta;
        IReadOnlyList<SwitchGroupSeen> _shownSwitches = [];
        // The crossing rule caps the time since the last Advance itself; a play after a pause relies on it being long.
        readonly Stopwatch _sinceAdvance = Stopwatch.StartNew();

        // The repository takes one load at a time and guards its own lookups, as the UI thread and the background both look up. A lookup
        // waits for another lookup or a load's last step, never for a whole load. The list is copied, as the repository keeps its own.
        void InitSound(IAudioRepository audio)
        {
            _audio = audio;
            _ui = SynchronizationContext.Current;
            LoadSounds = token => Task.Run(() =>
            {
                audio.Load([Wh3LanguageInformation.GetLanguageAsString(Wh3Language.EnglishUK)]);
                token.ThrowIfCancellationRequested();
                var tags = AudioMetadataTags.Read(_packFileService);
                return new SoundTriggerResolver(tags.Find, id => audio.GetHircs(id).ToList(), id => audio.FindWem(id.ToString()) != null);
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
            // A new clip or slot stops what the last one left ringing. Its rebuild is asked for at once, before the player puts the new
            // clip back on the last one's frame index, which raises a frame that must not fire the last clip's triggers. The last clip's
            // pickers go with its status lines, and the rebuild puts back what the new clip's triggers reach.
            _asset.Data.AnimationChanged += _ =>
            {
                RebuildSounds();
                StopSounds();
                ShowSwitchPickers([]);
            };
            _asset.Data.Player.OnFrameChanged += _ => AdvanceSounds();
            Player.Looping += LoopSounds;
            ShowSoundStatus();
        }

        partial void OnSoundOnChanged(bool value)
        {
            if (!value)
            {
                _soundLoad?.Cancel();
                _soundLoad = null;
                _rebuilds++;
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
            _resolver = resolver;
            _session = new SoundTriggerSession(resolver, id => PrepareSound(id), id => PlaySound(id), new Random()) { OffsetSeconds = SoundLeadSeconds };
            RebuildSounds();
        }

        // The tags are resolved twice. First off the UI thread, only to fill the repository's cache: a new clip's first resolves read the
        // banks, which took 1.25 s for a vanilla attack. Then on it, by the session, against that cache. The second pass reads the tags
        // again and is skipped when a later rebuild was asked for, so what plays always follows the latest tags.
        void RebuildSounds()
        {
            if (SoundOn && _session != null)
            {
                var (request, tags, campaign, choices, resolver) = (++_rebuilds, SoundTriggers(), SlotGrid.Set is CampaignAnimationSet, new Dictionary<uint, uint>(_session.SwitchChoices), _resolver!);
                _soundFailed = null;
                // A warm-up a later rebuild made stale stops at its next tag, so clicking through slots resolves only the last one's,
                // and after Close nothing reaches the repository the editor's scope disposed.
                Background(request, () =>
                {
                    foreach (var tag in tags)
                    {
                        if (request != _rebuilds)
                            return;
                        resolver.Resolve(tag.SoundEvent, campaign, choices);
                    }
                    OnUi(() => ApplyRebuild(request));
                });
            }
            ShowSoundStatus();
        }

        void ApplyRebuild(int request)
        {
            if (request != _rebuilds || !SoundOn || _session == null)
                return;
            _session.Rebuild(SoundTriggers(), SlotGrid.Set is CampaignAnimationSet);
            _applied = request;
            DecodeQueued(request, _session.Leaves.ToHashSet());
            ShowSwitchPickers(_session.Switches);
            ShowSoundStatus();
        }

        // The sources PrepareSound queued, decoded off the UI thread into the mixer's lock-guarded cache. A batch ends by dropping what the
        // triggers no longer play. A stale batch stops, and as batches run one at a time, it cannot put back a source a later one dropped.
        void DecodeQueued(int request, HashSet<uint> wanted)
        {
            var (mixer, ids, audio) = (_mixer, _toDecode.ToList(), _audio);
            _toDecode.Clear();
            if (mixer == null)
                return;
            Background(request, () =>
            {
                foreach (var id in ids)
                {
                    if (request != _rebuilds)
                        return;
                    mixer.Prepare(id, () => ReadWem(audio, id));
                }
                mixer.Keep(wanted);
            });
        }

        // One job on the background chain, which runs its jobs one at a time, in order. The status counts it until its end is back on the
        // UI thread. A job that throws ends like any other, so SoundDecoding never faults, and the status says why while its rebuild is
        // the latest.
        void Background(int request, Action job)
        {
            _backgroundJobs++;
            SoundDecoding = SoundDecoding.ContinueWith(_ =>
            {
                string? failure = null;
                try
                {
                    job();
                }
                catch (Exception e)
                {
                    failure = e.Message;
                }
                OnUi(() =>
                {
                    _backgroundJobs--;
                    if (failure != null && request == _rebuilds)
                        _soundFailed = $"Sounds could not be prepared: {failure}";
                    ShowSoundStatus();
                });
            }, TaskScheduler.Default);
        }

        // SuperView always opens on the UI thread, which has a context. The fallback is for tests that set none: the work then runs
        // where it is, as BinGridViewModel's does.
        void OnUi(Action action)
        {
            if (_ui == null)
                action();
            else
                _ui.Post(_ => action(), null);
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

        // Until the latest rebuild has landed, the session holds the last clip's triggers, which must not fire on this one. The frames
        // skipped make the first one after it a jump, so a trigger at the clip's start still fires.
        bool SoundsLive => SoundOn && _session != null && _applied == _rebuilds;

        void AdvanceSounds()
        {
            if (!SoundsLive)
                return;
            var player = _asset.Data.Player;
            var elapsed = _sinceAdvance.Elapsed.TotalSeconds;
            _sinceAdvance.Restart();
            _session!.Advance(player.GetTimeUs() / 1_000_000d, player.IsPlaying, elapsed, Player.Speed.Value, player.GetAnimationLengthUs() / 1_000_000d,
                Player.LoopAnimation.Value);
        }

        // Loop jumps back to frame 0 as playback enters the last frame, by a paused frame set. Without this the session would never see
        // the rest of the clip, nor the jump as a loop, and the triggers at either end would not sound.
        void LoopSounds()
        {
            if (SoundsLive)
                _session!.Loop(Player.Speed.Value, _asset.Data.Player.GetAnimationLengthUs() / 1_000_000d);
        }

        void ShowSoundStatus()
        {
            IReadOnlyList<string> lines = [.. SoundFileProblems, .. SessionLines()];
            if (!lines.SequenceEqual(SoundStatus))
                SoundStatus = lines;

            // Loading lasts until the first resolve is back, and decoding while any background work is. The session's own lines describe
            // the rebuild it last took, so they are left out until that is the latest.
            IEnumerable<string> SessionLines()
            {
                if (!SoundOn)
                    return _soundRefused == null ? [] : [_soundRefused];
                if (_session == null || _session.StatusLines.Count == 0)
                    return [_soundFailed ?? "Loading sounds..."];
                List<string> lines = _applied == _rebuilds ? [.. _session.StatusLines] : [];
                if (_backgroundJobs != 0)
                    lines.Add("Decoding sounds...");
                if (_soundFailed != null)
                    lines.Add(_soundFailed);
                return lines;
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

        // Work still queued is then stale and skipped.
        void CloseSound()
        {
            _soundLoad?.Cancel();
            _soundLoad = null;
            _rebuilds++;
            _mixer?.Dispose();
            _mixer = null;
        }
    }
}
