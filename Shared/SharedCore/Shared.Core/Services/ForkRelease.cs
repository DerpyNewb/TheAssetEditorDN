using System.Text.RegularExpressions;

namespace Shared.Core.Services
{
    /// <summary>
    /// This fork's releases: where they are, which one this build is, and how a tag names one. Compiled into AssetEditorUpdater as
    /// well, so the app and the updater can never disagree on either.
    /// </summary>
    public static class ForkRelease
    {
        public const string Owner = "DerpyNewb";
        public const string Repository = "TheAssetEditorDN";

        /// <summary>This build's DN version. A release of it is tagged v{upstream}-dn-v{this}, as v0.78-dn-v1.1.</summary>
        public static readonly Version Current = new(1, 3);

        /// <summary>The DN version a release tag names, or null for a tag that names none.</summary>
        public static Version? Parse(string tag)
        {
            var match = Regex.Match(tag.Trim(), @"-dn-v(\d+(?:\.\d+){0,3})$", RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;
            var text = match.Groups[1].Value;
            return Version.Parse(text.Contains('.') ? text : text + ".0");
        }

        /// <summary>The tags newer than this build, newest first.</summary>
        public static List<T> Newer<T>(IEnumerable<T> releases, Func<T, string> tag, Version current) =>
            releases.Select(x => (Release: x, Version: Parse(tag(x))))
                .Where(x => x.Version != null && x.Version > current)
                .OrderByDescending(x => x.Version)
                .Select(x => x.Release)
                .ToList();
    }
}
