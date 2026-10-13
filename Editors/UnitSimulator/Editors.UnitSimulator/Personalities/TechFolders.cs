using Shared.Core.PackFiles;

namespace Editors.UnitSimulator.Personalities
{
    /// <summary>The skeletons in a variant's tech folder (variants.tech_folder): every .anim there in any loaded pack, sorted. A tech
    /// skeleton is the animation skeleton with the ap_ bones appended; CA's folders hold one or two (the Warsphinx's _01 and _02).</summary>
    public static class TechFolders
    {
        // CA writes some folders with capitals and backslashes (VariantMeshes\wh_variantmodels\...); pack paths are lower case.
        public static List<string> Skeletons(IPackFileService pfs, string techFolder)
        {
            var folder = techFolder.Replace('/', '\\').Trim('\\').ToLowerInvariant();
            var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var container in pfs.GetAllPackfileContainers())
                if (container.GetAllFilesByFolder().TryGetValue(folder, out var names))
                    foreach (var name in names.Where(x => x.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)))
                        found.Add(folder + @"\" + name.ToLowerInvariant());
            return [.. found];
        }
    }
}
