using AssetEditor.Services;
using AssetEditor.UiCommands;
using AssetEditor.ViewModels;
using AssetEditor.Views;
using AssetEditor.Views.Settings;
using AssetEditor.Views.Updater;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Commands;
using Shared.Core.DependencyInjection;
using Shared.Core.DevConfig;
using Shared.Core.ErrorHandling.Exceptions;
using Shared.Core.ToolCreation;
using Shared.Ui.Common.MenuSystem;

namespace AssetEditor
{
    internal class DependencyInjectionContainer : DependencyContainer
    {
        public override void Register(IServiceCollection serviceCollection)
        {
            serviceCollection.AddScoped<MainWindow>();
            serviceCollection.AddScoped<MainViewModel>();
            serviceCollection.AddSingleton(new HotkeyDefinition(MainViewModel.SaveHotkey, "Hotkeys.Group.General", "Hotkeys." + MainViewModel.SaveHotkey, new Hotkey(Key.S, ModifierKeys.Control)));
            serviceCollection.AddSingleton<IEditorManager, EditorManager>();

            serviceCollection.AddTransient<CreateNewProjectCommand>();
            serviceCollection.AddTransient<OpenPackForEditingCommand>();
            serviceCollection.AddTransient<CreateNewPackCommand>();
            serviceCollection.AddTransient<OpenGamePackCommand>();
            serviceCollection.AddTransient<ImportPackAsAsProjectCommand>();
            serviceCollection.AddTransient<ImportReferencePackCommand>();
            serviceCollection.AddTransient<OpenProjectCommand>();
            serviceCollection.AddTransient<OpenRecentPackCommand>();
            serviceCollection.AddTransient<OpenSettingsDialogCommand>();
            serviceCollection.AddTransient<OpenUpdaterWindowCommand>();
            serviceCollection.AddTransient<OpenWebpageCommand>();
            serviceCollection.AddTransient<PrintScopesCommand>();
            serviceCollection.AddTransient<PrintTrackedGraphicsResourcesCommand>();
            serviceCollection.AddTransient<OpenEditorCommand>();
            serviceCollection.AddTransient<TogglePackFileExplorerCommand>();

            serviceCollection.AddTransient<SettingsWindow>();
            serviceCollection.AddTransient<SettingsViewModel>();
            serviceCollection.AddTransient<UpdaterWindow>();
            serviceCollection.AddTransient<UpdaterViewModel>();
            serviceCollection.AddScoped<MenuBarViewModel>();

            serviceCollection.AddScoped<MainWindow>();

            serviceCollection.AddSingleton<RecentFilesTracker>();
            serviceCollection.AddSingleton<PackAutosave>();
            serviceCollection.AddScoped<PackChangedOnDiskChecker>();
            serviceCollection.AddSingleton<TouchedFilesRecorder>();

            serviceCollection.AddScoped<IExceptionInformationProvider, CurrentEditorExceptionInfoProvider>();


            RegisterAllAsInterface<IDeveloperConfiguration>(serviceCollection, ServiceLifetime.Transient);
        }
    }
}
