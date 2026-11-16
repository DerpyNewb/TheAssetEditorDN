using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.AnimationPack;

namespace Editors.Shared.Core.Common.ReferenceModel
{
    /// <summary>The animation sets (bins) a skeleton has in the loaded packs: battle sets in animpacks and campaign sets.</summary>
    public static class AnimationSets
    {
        const string CampaignSetFolder = @"animations\campaign\database\bin\";

        static readonly ILogger _logger = Logging.CreateStatic(typeof(AnimationSets));

        public static List<IAnimationBinGenericFormat> ForSkeleton(IPackFileService pfs, string skeletonName, bool onlyPacksThatCanBeSaved = false)
        {
            var outputFragments = new List<IAnimationBinGenericFormat>();
            var animPacks = PackFileServiceUtility.GetAllAnimPacks(pfs);
            foreach (var animPack in animPacks)
            {
                if (onlyPacksThatCanBeSaved == true)
                {
                    if (pfs.GetPackFileContainer(animPack).IsCaPackFile)
                        continue;
                }

                var animPackFile = AnimationPackSerializer.Load(animPack, pfs);
                var fragments = animPackFile.GetGenericAnimationSets(skeletonName);
                foreach (var fragment in fragments)
                    outputFragments.Add(fragment);
            }

            // A campaign set is a loose file, not in an animpack, and the only set some skeletons have (Glottkin's). Each path
            // once, the copy the game would load.
            // ponytail: reads every set's string table on each skeleton change, 0.2 s for vanilla's 761; cache by file if it shows.
            var campaignSets = PackFileServiceUtility.FindAllWithExtentionIncludePaths(pfs, ".bin")
                .Select(x => PathNormalization.NormalizeFileName(x.FileName))
                .Where(x => x.StartsWith(CampaignSetFolder))
                .Distinct();
            foreach (var path in campaignSets)
            {
                var file = pfs.FindFile(path);
                if (file == null || onlyPacksThatCanBeSaved && pfs.GetPackFileContainer(file).IsCaPackFile)
                    continue;
                try
                {
                    if (CampaignAnimationBinLoader.ReadSkeletonName(file.DataSource.ReadDataAsChunk()) == skeletonName)
                        outputFragments.Add(new CampaignAnimationSet(path.Replace('\\', '/'), CampaignAnimationBinLoader.Load(file.DataSource.ReadDataAsChunk())));
                }
                catch (Exception e)
                {
                    _logger.Here().Warning($"Campaign animation set {path} could not be read, so it is not listed: {e.Message}");
                }
            }
            // A table is read once per animpack holding it: CA repeats most in two, and a save of a CA table puts it in one of the
            // editable pack's too. That copy is listed, as the one an edit goes to.
            return AnimTableEdits.OneCopyEach(pfs, outputFragments);
        }
    }
}
