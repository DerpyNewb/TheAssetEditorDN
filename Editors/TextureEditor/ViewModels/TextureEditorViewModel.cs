using System;
using System.IO;
using DirectXTexNet;
using Editors.ImportExport.Importing.Importers.PngToDds;
using Editors.ImportExport.Importing.Presentation.PngToDds;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.GameFormats.RigidModel.Types;

namespace Editors.TextureEditor.ViewModels
{
    public record DdsFormatChoice(string Label, DXGI_FORMAT? Format)
    {
        public override string ToString() => Label;
    }

    public class TextureEditorViewModel : NotifyPropertyChangedImpl, IEditorInterface, IFileEditor, IDisposable
    {
        private readonly IPackFileService _pfs;
        private readonly TextureBuilder _textureBuilder;
        private readonly IStandardDialogs _dialogs;
        private readonly IFileSaveService _fileSaveService;
        private readonly ApplicationSettingsService _settings;

        public string DisplayName { get; set; } = "Not set";

        PackFile _packFile;
        public PackFile CurrentFile => _packFile;

        TexturePreviewViewModel _viewModel; // UseNotify
        public TexturePreviewViewModel ViewModel
        {
            get => _viewModel;
            set => SetAndNotify(ref _viewModel, value);
        }

        // Replace from image: what the picked image is written as. The type is guessed from the texture's name, as the importer does.
        public TextureType[] ReplaceTextureTypes { get; } = [TextureType.BaseColour, TextureType.MaterialMap, TextureType.Normal, TextureType.Mask, TextureType.Diffuse, TextureType.Specular, TextureType.Gloss];
        public NotifyAttr<TextureType> ReplaceTextureType { get; } = new(TextureType.BaseColour);
        public DdsFormatChoice[] ReplaceFormats { get; } =
        [
            new("From the texture type", null),
            new("BC1_UNORM_SRGB", DXGI_FORMAT.BC1_UNORM_SRGB),
            new("BC3_UNORM_SRGB", DXGI_FORMAT.BC3_UNORM_SRGB),
            new("BC7_UNORM_SRGB", DXGI_FORMAT.BC7_UNORM_SRGB),
            new("BC1_UNORM", DXGI_FORMAT.BC1_UNORM),
            new("BC3_UNORM", DXGI_FORMAT.BC3_UNORM),
            new("BC7_UNORM", DXGI_FORMAT.BC7_UNORM),
        ];
        public NotifyAttr<DdsFormatChoice> ReplaceFormat { get; } = new();
        public NotifyAttr<bool> ReplaceMipmaps { get; } = new(true);
        public NotifyAttr<bool> ReplaceConvert { get; } = new(true);

        public TextureEditorViewModel(IPackFileService pfs, TextureBuilder textureBuilder, IStandardDialogs dialogs, IFileSaveService fileSaveService, ApplicationSettingsService settings)
        {
            _pfs = pfs;
            _textureBuilder = textureBuilder;
            _dialogs = dialogs;
            _fileSaveService = fileSaveService;
            _settings = settings;
            ReplaceFormat.Value = ReplaceFormats[0];
        }

        public void LoadFile(PackFile packFile)
        {
            _packFile = packFile;

            DisplayName = _packFile.Name;
            ReplaceTextureType.Value = PngToDdsImporterViewModel.Guess(Path.GetFileNameWithoutExtension(_packFile.Name).ToLowerInvariant());

            var viewModel = new TexturePreviewViewModel();
            viewModel.ImagePath.Value = _pfs.GetFullPath(_packFile);

            _textureBuilder.Build(viewModel, _pfs.GetFullPath(_packFile));
            ViewModel = viewModel;
        }

        public void ShowTextureDetailsInfo() => ViewModel.ShowTextureDetailsInfo();

        public void ReplaceFromImage()
        {
            if (CanReplace() is { } refusal)
            {
                _dialogs.ShowDialogBox(refusal, "Replace from image");
                return;
            }

            var picked = _dialogs.ShowSystemOpenFileDialog(false, "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff");
            if (picked.Result == false || picked.FilePaths.Count == 0)
                return;

            try
            {
                using var _ = _dialogs.ShowWaitCursor();
                Replace(File.ReadAllBytes(picked.FilePaths[0]));
            }
            catch (Exception e)
            {
                _dialogs.ShowExceptionWindow(e, $"Unable to replace {_pfs.GetFullPath(_packFile)} from {picked.FilePaths[0]}");
            }
        }

        public string? CanReplace()
        {
            if (_pfs.GetEditablePack() == null)
                return "No editable pack is selected - pick one in the Pack File Explorer first.";
            if (_packFile.Name.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) == false)
                return "Only a .dds texture can be replaced from an image.";
            return null;
        }

        /// <summary>The image as a DDS at this texture's path in the editable pack, then shown here.</summary>
        public void Replace(byte[] image)
        {
            var path = _pfs.GetFullPath(_packFile);
            var dds = PngToDdsImporter.Import(image, ReplaceTextureType.Value, _settings.CurrentSettings.CurrentGame, _packFile.Name,
                ReplaceConvert.Value, ReplaceFormat.Value?.Format, ReplaceMipmaps.Value);
            var saved = _fileSaveService.Save(path, dds.DataSource.ReadData(), false);
            if (saved != null)
                LoadFile(saved);
        }

        public void Close() => Dispose();

        public void Dispose()
        {
            _textureBuilder.Dispose();
        }
    }
}
