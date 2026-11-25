using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using GameWorld.Core.Animation;
using Shared.Core.Misc;
using Shared.Ui.Common.Timeline;

namespace Editors.Shared.Core.Common.AnimationPlayer
{
    public class AnimationPlayerViewModel : NotifyPropertyChangedImpl, IDisposable
    {
        readonly List<SceneObject> _assetList = new();
        readonly List<ValueChangedDelegate<AnimationClip>> _animationChangedHandlers = new();
        private bool _disposed;

        public NotifyAttr<float> SelectedAnimationCurrentTime { get; private set; } = new();
        public NotifyAttr<float> SelectedAnimationMaxTime { get; private set; } = new();
        public NotifyAttr<int> SelectedAnimationFps { get; private set; } = new();
        public NotifyAttr<int> SelectedAnimationCurrentFrame { get; private set; } = new();
        public NotifyAttr<int> SelectedAnimationFrameCount { get; private set; } = new();
        public NotifyAttr<bool> IsEnabled { get; set; } = new();
        public NotifyAttr<bool> LoopAnimation { get; set; } = new(true);

        /// <summary>Raised when Loop starts the clip again, before the jump back to frame 0. The loop comes as playback enters the last
        /// frame, short of the clip's end, and the jump is a paused frame set, so a listener cannot tell it from the frames alone.</summary>
        public event Action? Looping;

        // Meta tags need no speed of their own: SceneObject.Update gives them the main player's time.
        public double[] Speeds { get; } = [1, 0.5, 0.25];
        public NotifyAttr<double> Speed { get; } = new(1);

        /// <summary>The main animation's time, for the scrub bar. Setting it pauses every asset on the nearest frame.</summary>
        public double ScrubTime
        {
            get => SelectedAnimationCurrentTime.Value;
            set => SetAnimationTime(value);
        }

        /// <summary>Markers an editor shows on the scrub bar (the SuperView's META tags). Empty for a plain scrub bar.</summary>
        public ObservableCollection<ITimelineItem> TimelineItems { get; } = [];

        ITimelineItem? _selectedTimelineItem;
        public ITimelineItem? SelectedTimelineItem { get => _selectedTimelineItem; set => SetAndNotifyWhenChanged(ref _selectedTimelineItem, value); }

        /// <summary>Raised when the user clicks a marker on the scrub bar.</summary>
        public event Action<ITimelineItem>? TimelineItemClicked;
        public ICommand TimelineItemClickCommand { get; }

        public NotifyAttr<Visibility> PlayerControlsVisibility { get; private set; } = new(Visibility.Collapsed);

        public ObservableCollection<AssetPlayerItem> PlayerItems { get; private set; } = new ObservableCollection<AssetPlayerItem>();

        AssetPlayerItem _selectedMainAnimation;
        public AssetPlayerItem SelectedMainAnimation { get { return _selectedMainAnimation; } set { OnMainAnimationChanged(_selectedMainAnimation, value); SetAndNotifyWhenChanged(ref _selectedMainAnimation, value); } }

        public AnimationPlayerViewModel()
        {
            TimelineItemClickCommand = new RelayCommand<ITimelineItem>(item =>
            {
                if (item != null)
                    TimelineItemClicked?.Invoke(item);
            });
            IsEnabled.Value = false;
            IsEnabled.PropertyChanged += (x, y) => OnAnimationPlayerEnabled(IsEnabled.Value);
            Speed.PropertyChanged += (x, y) => _assetList.ForEach(asset => asset.Player.Speed = Speed.Value);
        }

        private void OnMainAnimationChanged(AssetPlayerItem oldAnimation, AssetPlayerItem mainAnimation)
        {
            if (oldAnimation != null)
                oldAnimation.Asset.Player.OnFrameChanged -= OnAnimationFrameChanged;

            SelectedAnimationFrameCount.Value = mainAnimation.MaxFrames.Value;
            SelectedAnimationMaxTime.Value = LengthInSeconds(mainAnimation.Asset);
            mainAnimation.Asset.Player.OnFrameChanged += OnAnimationFrameChanged;
        }

        public void RegisterAsset(SceneObject asset)
        {
            _assetList.Add(asset);
            var playerItem = new AssetPlayerItem() { Asset = asset };
            PlayerItems.Add(playerItem);

            asset.Player.LoopAnimation = false;
            asset.Player.Speed = Speed.Value;
            if (SelectedMainAnimation == null)
                SelectedMainAnimation = playerItem;

            OnAnimationPlayerEnabled(IsEnabled.Value);
            RefreshAssetViewModel(asset, playerItem);
            ValueChangedDelegate<AnimationClip> handler = (x) => RefreshAssetViewModel(asset, playerItem);
            _animationChangedHandlers.Add(handler);
            asset.AnimationChanged += handler;
        }

        // Removes the asset's row; the asset itself is left as it is, and no longer steps with the controls
        public void UnregisterAsset(SceneObject asset)
        {
            var index = _assetList.IndexOf(asset);
            if (index == -1)
                return;

            asset.AnimationChanged -= _animationChangedHandlers[index];
            _animationChangedHandlers.RemoveAt(index);
            _assetList.RemoveAt(index);

            var playerItem = PlayerItems.First(x => x.Asset == asset);
            PlayerItems.Remove(playerItem);

            if (SelectedMainAnimation == playerItem)
            {
                asset.Player.OnFrameChanged -= OnAnimationFrameChanged;
                _selectedMainAnimation = null;
                if (PlayerItems.Count != 0)
                    SelectedMainAnimation = PlayerItems[0];
                else
                    NotifyPropertyChanged(nameof(SelectedMainAnimation));
            }
        }

        // Picks up a changed Description
        public void RefreshAsset(SceneObject asset)
        {
            var playerItem = PlayerItems.FirstOrDefault(x => x.Asset == asset);
            if (playerItem != null)
                RefreshAssetViewModel(asset, playerItem);
        }

        void RefreshAssetViewModel(SceneObject asset, AssetPlayerItem playerItem)
        {
            playerItem.AnimationName.Value = asset.AnimationName.Value;
            playerItem.SlotName.Value = asset.Description;
            playerItem.MaxFrames.Value = asset.Player.FrameCount();
            if (playerItem == SelectedMainAnimation)
                SelectedAnimationMaxTime.Value = LengthInSeconds(asset);
        }

        static float LengthInSeconds(SceneObject asset) => (float)asset.Player.GetAnimationLengthUs() / 1_000_000;


        public void ToggleAnimationPausePlay()
        {
            foreach (var item in _assetList)
            {
                if (item.Player.IsPlaying)
                    Pause(item);
                else
                    Resume(item);
            }
        }

        public void SetAnimationNextFrame() => Step(NextFrame);
        public void SetAnimationPrivFrame() => Step(PrivFrame);

        // A step onto the last frame is not the clip running out, so Loop does not start it again.
        bool _stepping;
        void Step(Action<SceneObject> step)
        {
            _stepping = true;
            try { _assetList.ForEach(step); }
            finally { _stepping = false; }
        }

        public void SetAnimationTime(double timeInSeconds)
        {
            var main = SelectedMainAnimation?.Asset.Player;
            if (main == null || main.FrameCount() == 0 || main.AnimationClip.MicrosecondsPerFrame <= 0)
                return;

            var frame = (int)Math.Round(timeInSeconds * 1_000_000 / main.AnimationClip.MicrosecondsPerFrame);
            frame = Math.Clamp(frame, 0, main.FrameCount() - 1);
            _assetList.ForEach(x => SetFrame(x, frame));
        }

        public void SetAnimationFirstFrame() => _assetList.ForEach(x => SetFrame(x, 0));

        public void SetAnimationLastFrame()
        {
            foreach (var item in _assetList)
                SetFrame(item, SelectedMainAnimation.Asset.Player.FrameCount() - 1);
        }

        private void OnAnimationFrameChanged(int currentFrame)
        {
            SelectedAnimationFrameCount.Value = SelectedMainAnimation.Asset.Player.FrameCount();
            SelectedAnimationCurrentFrame.Value = currentFrame;
            SelectedAnimationCurrentTime.Value = (float)SelectedMainAnimation.Asset.Player.GetTimeUs() / 1_000_000;
            SelectedAnimationMaxTime.Value = (float)SelectedMainAnimation.Asset.Player.GetAnimationLengthUs() / 1_000_000;
            SelectedAnimationFps.Value = SelectedMainAnimation.Asset.Player.GetFps();
            NotifyPropertyChanged(nameof(ScrubTime));

            // Playback reaching the end loops; a frame set by hand that happens to be the last one is not playback reaching it.
            if (_stepping == false && SelectedMainAnimation.Asset.Player.IsPlaying && SelectedAnimationCurrentFrame.Value + 1 == SelectedMainAnimation.Asset.Player.FrameCount())
            {
                if (LoopAnimation.Value)
                {
                    Looping?.Invoke();
                    SetAnimationFirstFrame();
                    ToggleAnimationPausePlay();
                }
            }
        }

        private void OnAnimationPlayerEnabled(bool isEnabled)
        {
            if (isEnabled)
            {
                PlayerControlsVisibility.Value = Visibility.Visible;

                SelectedAnimationFrameCount.Value = 0;
                if (SelectedMainAnimation != null)
                    SelectedAnimationFrameCount.Value = SelectedMainAnimation.MaxFrames.Value;

                _assetList.ForEach(x => Play(x));
            }
            else
            {
                PlayerControlsVisibility.Value = Visibility.Collapsed;
                _assetList.ForEach(x => Stop(x));
            }
        }

        // A paused clip carries on from where it stopped; one at its very end starts again.
        void Play(SceneObject asset)
        {
            if (asset.Player.GetTimeUs() >= asset.Player.GetAnimationLengthUs())
                asset.Player.CurrentFrame = 0;
            asset.Player.Play();

            foreach (var metaItem in asset.MetaDataItems)
            {
                if (metaItem.Player != null)
                {
                    metaItem.Player.CurrentFrame = asset.Player.CurrentFrame;
                    metaItem.Player.Play();
                }
            }
        }

        // Plays on from the current frame (scrubbed or stepped to); from the start once the end was reached
        void Resume(SceneObject asset)
        {
            var player = asset.Player;
            if (player.GetAnimationLengthUs() > 0 && player.GetTimeUs() >= player.GetAnimationLengthUs())
            {
                Play(asset);
                return;
            }

            player.Play();
            foreach (var metaItem in asset.MetaDataItems)
                metaItem.Player?.Play();
        }

        void Pause(SceneObject asset)
        {
            asset.Player.Pause();
            foreach (var metaItem in asset.MetaDataItems)
                metaItem.Player?.Pause();
        }

        void Stop(SceneObject asset)
        {
            asset.Player.Stop();
            foreach (var metaItem in asset.MetaDataItems)
                metaItem.Player?.Stop();
        }

        void SetFrame(SceneObject asset, int newFrame)
        {
            asset.Player.Pause();
            asset.Player.CurrentFrame = newFrame;

            foreach (var metaItem in asset.MetaDataItems)
            {
                if (metaItem.Player != null)
                {
                    metaItem.Player.Pause();
                    metaItem.Player.CurrentFrame = newFrame;
                }
            }
        }

        void NextFrame(SceneObject asset)
        {
            asset.Player.Pause();
            asset.Player.CurrentFrame++;

            foreach (var metaItem in asset.MetaDataItems)
            {
                if (metaItem.Player != null)
                {
                    metaItem.Player.Pause();
                    metaItem.Player.CurrentFrame = asset.Player.CurrentFrame;
                }
            }
        }

        void PrivFrame(SceneObject asset)
        {
            asset.Player.Pause();
            asset.Player.CurrentFrame--;

            foreach (var metaItem in asset.MetaDataItems)
            {
                if (metaItem.Player != null)
                {
                    metaItem.Player.Pause();
                    metaItem.Player.CurrentFrame = asset.Player.CurrentFrame;
                }
            }
        }

        public class AssetPlayerItem : NotifyPropertyChangedImpl
        {
            public SceneObject Asset { get; init; }
            public NotifyAttr<string> SlotName { get; internal set; } = new();
            public NotifyAttr<int> MaxFrames { get; internal set; } = new();
            public NotifyAttr<string> AnimationName { get; internal set; } = new();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_selectedMainAnimation != null)
                _selectedMainAnimation.Asset.Player.OnFrameChanged -= OnAnimationFrameChanged;

            for (var i = 0; i < _assetList.Count; i++)
            {
                if (i < _animationChangedHandlers.Count)
                    _assetList[i].AnimationChanged -= _animationChangedHandlers[i];
            }

            _animationChangedHandlers.Clear();
            _assetList.Clear();
        }
    }
}
