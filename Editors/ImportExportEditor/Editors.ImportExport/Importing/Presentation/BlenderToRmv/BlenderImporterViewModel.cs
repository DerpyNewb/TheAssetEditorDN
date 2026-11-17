using CommunityToolkit.Mvvm.ComponentModel;
using Editors.ImportExport.Blender;
using Editors.ImportExport.Importing.Importers.BlenderToRmv;
using Editors.ImportExport.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Common.DataTemplates;

namespace Editors.ImportExport.Importing.Presentation.BlenderToRmv
{
    public partial class BlenderImporterViewModel(BlenderImporter importer, IStandardDialogs dialogs)
        : ObservableObject, IImporterViewModel, IViewProvider<BlenderImporterView>
    {
        public string DisplayName => "Blender or OBJ";
        public string OutputExtension => ".rigid_model_v2";
        public string[] InputExtensions => [".blend", ".obj"];

        public record VersionChoice(int Value, string Label);

        /// <summary>0 keeps each model's own version.</summary>
        public VersionChoice[] Versions { get; } = [new(0, "Keep the model's own"), new(6, "6"), new(7, "7"), new(8, "8")];

        [ObservableProperty] string _skeletonName = "";
        [ObservableProperty] int _version = 0;
        [ObservableProperty] bool _generateLods = false;

        public ImportSupportEnum CanImportFile(PackFile file) =>
            FileExtensionHelper.IsBlenderImportFile(file.Name) ? ImportSupportEnum.HighPriority : ImportSupportEnum.NotSupported;

        public void Execute(PackFile importSource, string outputPath, IPackFileContainer packFileContainer, GameTypeEnum gameType)
        {
            var outcome = importer.Import(importSource.Name, packFileContainer, outputPath,
                string.IsNullOrWhiteSpace(SkeletonName) ? null : SkeletonName.Trim(), Version, GenerateLods, AskToReplace);
            BlenderResultReport.Show("Import from Blender", outcome.Result);
        }

        bool AskToReplace(string originalPath) =>
            dialogs.ShowYesNoBox(
                $"This file was exported by AssetEditor from {originalPath}.\n\nWrite it back to that path? Any file already there is replaced. Choose No to import it into the folder you picked instead.",
                "Import from Blender") == ShowMessageBoxResult.OK;
    }
}
