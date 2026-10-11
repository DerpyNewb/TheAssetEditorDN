using System.Text;
using Editors.VfxEditor.Document;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace Editors.VfxEditor.Vocabulary
{
    /// <summary>Builds the vocabulary once per session from the CA packs' vfx files, on a background thread, the first time
    /// a VFX editor asks for it. A file that does not read adds nothing.</summary>
    public sealed class VfxVocabularyService(IPackFileService packFileService)
    {
        readonly object _lock = new();
        Task<VfxVocabulary>? _task;

        public Task<VfxVocabulary> Get()
        {
            lock (_lock)
                return _task ??= Task.Run(Build);
        }

        VfxVocabulary Build()
        {
            var files = packFileService.GetAllPackfileContainers().Where(c => c.IsCaPackFile)
                .SelectMany(c => packFileService.FindAllWithExtention(".xml", c))
                .Where(f => VfxVocabulary.EffectName(f.FileName) != null);
            return VfxVocabulary.Build(Read(files));
        }

        static IEnumerable<(string, VfxDocument)> Read(IEnumerable<(string FileName, PackFile Pack)> files)
        {
            foreach (var (path, file) in files)
            {
                VfxDocument? document = null;
                try
                {
                    document = VfxDocument.Load(file.DataSource.ReadData());
                }
                catch (Exception e) when (e is VfxParseException or DecoderFallbackException)
                {
                }
                if (document != null)
                    yield return (path, document);
            }
        }
    }
}
