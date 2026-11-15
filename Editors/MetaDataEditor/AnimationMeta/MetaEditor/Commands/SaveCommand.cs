using CommunityToolkit.Diagnostics;
using Editors.AnimationMeta.Presentation;
using Shared.Core.Events;
using Shared.Core.Events.Scoped;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.MetaEditor.Commands
{
    class SaveCommand : IAeCommand
    {
        private readonly ILogger _logger = Logging.Create<SaveCommand>();
        private readonly IPackFileService _packFileService;
        private readonly IEventHub _eventHub;
        private readonly IFileSaveService _packFileSaveService;
        private readonly IStandardDialogs _standardDialogs;
        private readonly MetaDataFileParser _metaDataFileParser;
        private readonly ApplicationSettingsService _settings;

        void IAeCommand.Execute() => throw new NotSupportedException("Use Execute(MetaDataEditorViewModel) instead.");

        public SaveCommand(IPackFileService packFileService, IEventHub eventHub, IFileSaveService packFileSaveService, IStandardDialogs standardDialogs, MetaDataFileParser metaDataFileParser, ApplicationSettingsService settings)
        {
            _settings = settings;
            _packFileService = packFileService;
            _eventHub = eventHub;
            _packFileSaveService = packFileSaveService;
            _standardDialogs = standardDialogs;
            _metaDataFileParser = metaDataFileParser;
        }

        public bool Execute(MetaDataEditorViewModel controller)
        {
            // SuperView shows the panel before a meta file is picked; Save there has nothing to write.
            if (controller.ParsedFile == null || controller.CurrentFile == null)
                return false;

            // Ensure there are no errors
            foreach (var tag in controller.Tags)
            {
                var currentErrorMessage = tag.HasError();
                if (string.IsNullOrWhiteSpace(currentErrorMessage) == false)
                {
                    _standardDialogs.ShowDialogBox($"Unable to save : {currentErrorMessage}");
                    return false;
                }
            }

            // Save the file
            var path = _packFileService.GetFullPath(controller.CurrentFile);

            // A path the editable pack does not have yet is someone else's file: saved there, the copy overrides it for every bin
            // that names it. Without an editable pack the save fails below, as it always has, so there is nothing to ask.
            var editable = _packFileService.GetEditablePack();
            if (editable != null && _packFileService.FindFile(path, editable) == null)
            {
                var source = CustomMeta.Source(_packFileService, path);
                var (whose, from) = source switch
                {
                    "vanilla" => ("vanilla", "a vanilla file"),
                    "" => ("the", "not in your pack"),
                    _ => ($"{source}'s", $"from {source}"),
                };
                var choice = _standardDialogs.ShowChoiceBox(
                    $"{CustomMeta.Normalise(path)} is {from}. Saving it puts a copy at the same path in your pack, and that copy replaces it for every unit whose animation bin uses it.\n\nSave your edits as a custom meta of your own instead?",
                    "Save meta", ["Save as custom meta", $"Override {whose} file", "Cancel"]);
                if (choice == 0 && controller.SaveAsCustom != null)
                    return controller.SaveAsCustom();
                if (choice == 0)
                {
                    var custom = CustomMeta.Ask(_standardDialogs, _packFileService, _settings, path, CustomMeta.Offered(path, CustomMeta.Prefix(_settings)));
                    if (custom == null)
                        return false;
                    path = custom;
                }
                else if (choice != 1)
                {
                    return false;
                }
            }
            _logger.Here().Information("Creating metadata file. TagCount=" + controller.Tags.Count + " " + path);

            var bytes = _metaDataFileParser.GenerateBytes(controller.MetaDataFileVersion, controller.ParsedFile);
            _logger.Here().Information("Saving");
            var res = _packFileSaveService.Save(path, bytes, false);
            if (res != null)
            {
                controller.CurrentFile = res;
                controller.DisplayName = res.Name;
                controller.HasUnsavedChanges = false;
            }

            _logger.Here().Information("Creating metadata file complete");
            var saveEvent = new ScopedFileSavedEvent()
            {
                FileOwner = controller,
                NewPath = path,
            };
            _eventHub.Publish(saveEvent);
            
            return true;
        }
    }
}

