using CommunityToolkit.Diagnostics;
using Editors.AnimationMeta.Presentation;
using Shared.Core.Events;
using Shared.Core.Events.Scoped;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
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

        void IAeCommand.Execute() => throw new NotSupportedException("Use Execute(MetaDataEditorViewModel) instead.");

        public SaveCommand(IPackFileService packFileService, IEventHub eventHub, IFileSaveService packFileSaveService, IStandardDialogs standardDialogs, MetaDataFileParser metaDataFileParser)
        {
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

