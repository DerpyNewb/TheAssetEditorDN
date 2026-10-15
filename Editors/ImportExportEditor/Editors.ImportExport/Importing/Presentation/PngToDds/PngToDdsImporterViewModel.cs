using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Editors.ImportExport.Importing.Importers.PngToDds;
using Editors.ImportExport.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel.Types;
using Shared.Ui.Common.DataTemplates;

namespace Editors.ImportExport.Importing.Presentation.PngToDds
{
    /// <summary>An image file as a .dds of the same name in the folder it was imported into, in the format the game uses for that kind of texture.</summary>
    public partial class PngToDdsImporterViewModel(IPackFileService packFileService) : ObservableObject, IImporterViewModel, IViewProvider<PngToDdsImporterView>
    {
        public string DisplayName => "Texture (image to DDS)";
        public string OutputExtension => ".dds";
        public string[] InputExtensions => [".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff"];

        public TextureType[] TextureTypes { get; } = [TextureType.BaseColour, TextureType.MaterialMap, TextureType.Normal, TextureType.Mask, TextureType.Diffuse, TextureType.Specular, TextureType.Gloss];

        [ObservableProperty] TextureType _textureType = TextureType.BaseColour;
        [ObservableProperty] bool _convert = true;

        public ImportSupportEnum CanImportFile(PackFile file)
        {
            if (!InputExtensions.Contains(Path.GetExtension(file.Name).ToLowerInvariant()))
                return ImportSupportEnum.NotSupported;

            TextureType = Guess(Path.GetFileNameWithoutExtension(file.Name).ToLowerInvariant());
            return ImportSupportEnum.HighPriority;
        }

        public void Execute(PackFile importSource, string outputPath, IPackFileContainer packFileContainer, GameTypeEnum gameType)
        {
            // Lower case: since WH3 6.1 an upper-case letter in a pack path crashes the game.
            var name = Path.GetFileNameWithoutExtension(importSource.Name).ToLowerInvariant() + ".dds";
            var dds = PngToDdsImporter.Import(importSource.DataSource.ReadData(), TextureType, gameType, name, Convert);
            packFileService.AddFilesToPack(packFileContainer, [new NewPackFileEntry(outputPath, dds)]);
        }

        // From CA's suffixes, and the ones this editor's own exports and Blender's add.
        static TextureType Guess(string name) => name switch
        {
            _ when name.Contains("normal") => TextureType.Normal,
            _ when name.Contains("material") => TextureType.MaterialMap,
            _ when name.Contains("mask") => TextureType.Mask,
            _ when name.Contains("spec") => TextureType.Specular,
            _ when name.Contains("gloss") => TextureType.Gloss,
            _ when name.Contains("diffuse") => TextureType.Diffuse,
            _ => TextureType.BaseColour,
        };
    }
}
