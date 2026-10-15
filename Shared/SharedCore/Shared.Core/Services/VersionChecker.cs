using System.Reflection;
using Octokit;

namespace Shared.Core.Services
{
    public class VersionChecker
    {
        private static readonly ILogger s_logger = Logging.Create<VersionChecker>();

        // This fork's releases, not upstream's: accepting upstream's would install its build over this one.
        private const string GitHubOwner = ForkRelease.Owner;
        private const string GitHubRepository = ForkRelease.Repository;

        public static async Task<List<Release>?> GetNewerReleases()
        {
            var releases = await GetReleasesAsync();
            if (releases == null || releases.Count == 0)
                return null;

            var newerReleases = ForkRelease.Newer(releases, x => x.TagName, ForkRelease.Current);
            return newerReleases.Count > 0 ? newerReleases : null;
        }

        private static async Task<IReadOnlyList<Release>?> GetReleasesAsync()
        {
            try
            {
                var gitHubClient = new GitHubClient(new ProductHeaderValue("AssetEditor"));
                var releases = await gitHubClient.Repository.Release.GetAll(GitHubOwner, GitHubRepository);
                return releases.Count > 0 ? releases : null;
            }
            catch (ApiException exception)
            {
                s_logger.Information($"Unable to retrieve latest release from GitHub: {exception.Message}");
                return null;
            }
        }

        public static Version GetCurrentVersion()
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            if (version == null)
                throw new Exception("Current version is unknown.");

            if (version.Build == 0 && version.Revision == 0)
                return new Version(version.Major, version.Minor);

            return new Version(version.Major, version.Minor, version.Build);
        }
    }
}
