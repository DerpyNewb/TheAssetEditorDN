using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace Editors.Ipc
{
    public class ExternalPackFileLookup : IExternalPackFileLookup
    {
        private readonly IPackFileService _packFileService;

        public ExternalPackFileLookup(IPackFileService packFileService)
        {
            _packFileService = packFileService;
        }

        public PackFile FindByPath(string path) => _packFileService.FindFile(path);

        public List<string> Search(string text, int limit) => _packFileService.GetAllPackfileContainers()
            .SelectMany(x => x.GetAllFiles().Keys)
            .Where(x => x.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }
}
