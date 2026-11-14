using System.Diagnostics;
using System.Text.RegularExpressions;
using Shared.ByteParsing;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.MetaEditor.Examples
{
    /// <summary>One vanilla use of a tag: the file it is in, its bytes, which Create reads into a new tag, and its fields as the
    /// editor names them.</summary>
    public record VanillaExample(string Path, string Name, int Version, byte[] Data, IReadOnlyList<VanillaExampleField> Fields)
    {
        public string Key => $"{Name}_{Version}";
        public string FileName => System.IO.Path.GetFileName(Path);
    }

    public record VanillaExampleField(string Name, string Value);

    /// <summary>
    /// A few vanilla uses of each tag and version, read from the loaded CA packs the first time any are asked for and kept for
    /// the session. The packs are read on the user's machine, so nothing of CA's is stored in this repo; MetaDataDatabase holds
    /// only the use counts. WH3's 67,530 metas, all in anim2.pack, took 32.5 s to read on one thread and 5.5 to 9.7 s on
    /// eleven (2026-11-14), so the scan runs off the UI thread and Scan hands back the task.
    /// </summary>
    public class VanillaMetaExamples
    {
        public const int PerKey = 5;

        readonly IPackFileService _packFileService;
        readonly MetaDataFileParser _parser;
        readonly ILogger _logger = Logging.Create<VanillaMetaExamples>();
        readonly object _lock = new();
        Task<IReadOnlyDictionary<string, List<VanillaExample>>>? _scan;

        public VanillaMetaExamples(IPackFileService packFileService, MetaDataFileParser parser)
        {
            _packFileService = packFileService;
            _parser = parser;
        }

        /// <summary>Examples by "NAME_VERSION". Null while no CA pack is loaded: nothing is cached then, so a request after the game's
        /// packs are opened starts the scan. A scan that failed is started again too.</summary>
        public Task<IReadOnlyDictionary<string, List<VanillaExample>>>? Scan()
        {
            lock (_lock)
            {
                if (_scan is { IsFaulted: true })
                    _scan = null;
                if (_scan == null && _packFileService.GetAllPackfileContainers().Any(x => x.IsCaPackFile))
                    _scan = Task.Run(Build);
                return _scan;
            }
        }

        /// <summary>A new tag with the example's values. Read again from its bytes, so editing it leaves the example alone.</summary>
        public ParsedMetadataAttribute Create(VanillaExample example) =>
            _parser.DeSerialize(new ParsedUnknownMetadataAttribute { Name = example.Name, Version = example.Version, Data = example.Data }, out var error)
            ?? throw new InvalidOperationException($"{example.Key} from {example.Path} no longer reads: {error}");

        IReadOnlyDictionary<string, List<VanillaExample>> Build()
        {
            var watch = Stopwatch.StartNew();
            var found = new Dictionary<string, Kept>();
            var files = _packFileService.GetAllPackfileContainers()
                .Where(x => x.IsCaPackFile)
                .SelectMany(x => x.SearchFiles(null, [".meta"]))
                .Where(x => x.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Both fill a lookup the first time they are used, which is not safe from two threads at once.
            _parser.GetDatabase().GetSupportedTypes();
            ByteParserFactory.Create(typeof(int));

            // Each file is read and parsed on its own, so that runs in parallel, leaving one core for the UI. The tags are still
            // offered in the files' order, so the same examples come back every time.
            var parsedFiles = files.AsParallel().AsOrdered()
                .WithDegreeOfParallelism(Math.Max(1, Environment.ProcessorCount - 1))
                .Select(x => (x.Path, Parsed: TryParse(x.Path, x.File)));
            foreach (var (path, parsed) in parsedFiles)
            {
                if (parsed == null)
                    continue;
                foreach (var tag in parsed.Attributes.Where(x => x is not ParsedUnknownMetadataAttribute))
                {
                    var key = $"{tag.Name}_{tag.Version}";
                    if (!found.TryGetValue(key, out var kept))
                        found[key] = kept = new Kept();
                    kept.Offer(tag, () => Example(path, tag));
                }
            }

            _logger.Here().Information($"Vanilla examples: read {files.Count} metas in {watch.Elapsed.TotalSeconds:0.0} s, {found.Count} tag versions");
            return found.ToDictionary(x => x.Key, x => x.Value.Examples());
        }

        // 9 of WH3's vanilla metas do not read at all; they are passed over.
        ParsedMetadataFile? TryParse(string path, PackFile file)
        {
            try
            {
                return _parser.ParseFile(file.DataSource.ReadData());
            }
            catch (Exception e)
            {
                _logger.Here().Warning($"Vanilla examples: {path} did not read: {e.Message}");
                return null;
            }
        }

        VanillaExample Example(string path, ParsedMetadataAttribute tag)
        {
            var fields = (_parser.DeSerializeToStrings(tag, out _) ?? [])
                .Where(x => x.Header != nameof(ParsedMetadataAttribute.Version))
                .Select(x => new VanillaExampleField(SpacedName(x.Header), x.Value))
                .ToList();
            return new VanillaExample(path, tag.Name, tag.Version, tag.Data, fields);
        }

        // The editor shows "StartTime" as "Start Time"; the examples use the same names so a value can be matched to its box.
        static string SpacedName(string name) => Regex.Replace(name, "(?<!^)([A-Z])", " $1");

        /// <summary>
        /// Prefers examples whose values differ in more than their times: of SOUND_TRIGGER_11's 261,641 vanilla uses, the first
        /// five are otherwise the same tag at five moments, which shows one way to use it, not five. When a tag has fewer than
        /// PerKey such examples, ones that differ only in their times fill the rest.
        /// </summary>
        sealed class Kept
        {
            readonly List<VanillaExample> _distinct = [];
            readonly List<VanillaExample> _spare = [];
            readonly HashSet<string> _values = [];
            readonly HashSet<string> _bytes = [];

            public void Offer(ParsedMetadataAttribute tag, Func<VanillaExample> example)
            {
                if (_distinct.Count == PerKey)
                    return;
                if (_values.Add(WithoutTimes(tag)))
                {
                    _bytes.Add(Convert.ToBase64String(tag.Data));
                    _distinct.Add(example());
                }
                else if (_spare.Count < PerKey && _bytes.Add(Convert.ToBase64String(tag.Data)))
                {
                    _spare.Add(example());
                }
            }

            public List<VanillaExample> Examples() => [.. _distinct, .. _spare.Take(PerKey - _distinct.Count)];

            // Both timed base classes put Start Time and End Time straight after the version, at bytes 4 to 12.
            static string WithoutTimes(ParsedMetadataAttribute tag) =>
                tag is DecodedMetaEntryBase or DecodedMetaEntryBase_v2 && tag.Data.Length >= 12
                    ? Convert.ToBase64String(tag.Data, 0, 4) + Convert.ToBase64String(tag.Data, 12, tag.Data.Length - 12)
                    : Convert.ToBase64String(tag.Data);
        }
    }
}
