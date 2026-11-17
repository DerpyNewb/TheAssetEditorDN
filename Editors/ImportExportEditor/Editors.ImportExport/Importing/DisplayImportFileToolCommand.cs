using System.Windows.Forms;
using Shared.Core.Events;
using Shared.Core.PackFiles.Models;
using TreeNode = Shared.Ui.BaseDialogs.PackFileTree.TreeNode;
using Shared.Core.PackFiles;
using Editors.ImportExport.Importing.Importers.GltfToRmv;
using System.Windows.Forms.Design;
using Shared.Core.Settings;
using Shared.Core.Misc;
using Editors.ImportExport.Importing.Presentation;

namespace Editors.ImportExport.Importing
{
    public class DisplayImportFileToolCommand : IAeCommand
    {
        private readonly IEnumerable<IImporterViewModel> _importerViewModels;
        private readonly IAbstractFormFactory<ImportWindow> _importWindowFactory;
        private IPackFileContainer _packFileContainer = null!;
        private string _packPath = string.Empty;

        public DisplayImportFileToolCommand(IAbstractFormFactory<ImportWindow> exportWindowFactory, IEnumerable<IImporterViewModel> exporterViewModels)
        {
            _importWindowFactory = exportWindowFactory;
            _importerViewModels = exporterViewModels;
        }

        private string? _diskFile;

        /// <param name="diskFile">A file already chosen (dropped on the tree); null asks with a file dialog.</param>
        public void Configure(IPackFileContainer packFileContainer, string packPath, string? diskFile = null)
        {
            _packFileContainer = packFileContainer;
            _packPath = packPath;
            _diskFile = diskFile;
        }

        public void Execute()
        {
            var diskFilePath = _diskFile;
            if (diskFilePath == null)
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog { Filter = GetFileDialogFilters() };
                if (openFileDialog.ShowDialog() != true)
                    return;
                diskFilePath = openFileDialog.FileName;
            }

            var window = _importWindowFactory.Create();
            window.Initialize(_packFileContainer, _packPath, diskFilePath);
            window.ShowDialog();
        }

        /// <summary>
        /// Makes a filter string for the file dialog ("Word Documents|*.doc|Excel Worksheets|*.xls" )
        /// Searching all importer view models for their supported file extensions
        /// </summary>        
        private string GetFileDialogFilters()
        {
            var all = string.Join(";", _importerViewModels.SelectMany(x => x.InputExtensions).Select(e => "*" + e));
            var fileExtentionFilters = $"All supported files ({all})|{all}|";
            foreach (var importViewModel in _importerViewModels) // generate file dialog filters, of all support file formats
            {
                var tempFilter = $"*{String.Join(";*", importViewModel.InputExtensions)}";
                fileExtentionFilters += $"{importViewModel.DisplayName} ({tempFilter}) |{tempFilter}|";
            }

            fileExtentionFilters += "All files (*.*)|*.*";

            return fileExtentionFilters;
        }
    }
}
