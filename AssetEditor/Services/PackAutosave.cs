using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Events;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace AssetEditor.Services
{
    /// <summary>Every <see cref="ApplicationSettings.AutosaveMinutes"/>, a pack changed since it was last saved or autosaved is
    /// written to the backup folder (<see cref="PackBackups.WriteAutosave"/>). The pack itself is only ever written by Save.</summary>
    class PackAutosave : IDisposable
    {
        private readonly ILogger _logger = Logging.Create<PackAutosave>();
        private readonly IGlobalEventHub _globalEventHub;
        private readonly ApplicationSettingsService _settingsService;
        private readonly DispatcherTimer _timer;
        private readonly HashSet<IPackFileContainer> _changed = [];

        public PackAutosave(IGlobalEventHub globalEventHub, ApplicationSettingsService settingsService)
        {
            _globalEventHub = globalEventHub;
            _settingsService = settingsService;

            _globalEventHub.Register<PackFileContainerManipulationEvent>(this, OnChanged);
            _globalEventHub.Register<PackFileContainerSavedEvent>(this, x => _changed.Remove(x.Container));

            _timer = new DispatcherTimer { Interval = Interval() };
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
        }

        // A setting changed in Settings applies from the next tick; off still ticks, once a minute, to notice it coming back on.
        TimeSpan Interval() => TimeSpan.FromMinutes(Math.Max(1, _settingsService.CurrentSettings.AutosaveMinutes));

        void OnChanged(PackFileContainerManipulationEvent e)
        {
            switch (e)
            {
                case PackFileContainerRemovedEvent x: _changed.Remove(x.Container); break;
                case PackFileContainerFilesUpdatedEvent x: _changed.Add(x.Container); break;
                case PackFileContainerFilesAddedEvent x: _changed.Add(x.Container); break;
                case PackFileContainerFilesRemovedEvent x: _changed.Add(x.Container); break;
                case PackFileContainerFolderRemovedEvent x: _changed.Add(x.Container); break;
                case PackFileContainerFolderRenamedEvent x: _changed.Add(x.Container); break;
            }
        }

        internal void Tick()
        {
            _timer.Interval = Interval();
            var settings = _settingsService.CurrentSettings;
            if (settings.AutosaveMinutes <= 0)
                return;

            var game = GameInformationDatabase.GetGameById(settings.CurrentGame);
            foreach (var pack in _changed.ToList())
            {
                try
                {
                    using var busy = BusyWork.Doing($"Autosaving {pack.Name}");
                    var path = PackBackups.WriteAutosave(pack, settings, game);
                    _changed.Remove(pack);
                    if (path != null)
                        _logger.Here().Information($"Autosaved '{pack.Name}' to '{path}'");
                }
                catch (Exception ex)
                {
                    // Kept as changed, so the next tick tries again.
                    _logger.Here().Error(ex, $"Autosave of '{pack.Name}' failed");
                }
            }
        }

        public void Dispose()
        {
            _timer.Stop();
            _globalEventHub.UnRegister(this);
        }
    }
}
