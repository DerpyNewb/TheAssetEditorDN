using Shared.Core.Services;

namespace Shared.CoreTest.Services
{
    // The update prompt read upstream's releases, so accepting it replaced this fork with upstream's build. It reads the fork's
    // now, by the DN version in each tag.
    public class ForkReleaseTests
    {
        [TestCase("v0.78-dn-v1", "1.0")]
        [TestCase("v0.78-dn-v1.1", "1.1")]
        [TestCase("v0.79-DN-v2.0.3", "2.0.3")]
        public void ATag_NamesItsDnVersion(string tag, string version) => Assert.That(ForkRelease.Parse(tag), Is.EqualTo(Version.Parse(version)));

        [TestCase("v0.78")]
        [TestCase("v0.78-dn-v")]
        [TestCase("v0.78-dn-v1-beta")]
        public void AnUpstreamOrOddTag_NamesNone(string tag) => Assert.That(ForkRelease.Parse(tag), Is.Null);

        [Test]
        public void Newer_IsTheDnReleasesAfterThisOne_NewestFirst()
        {
            var tags = new[] { "v0.78-dn-v1.1", "v0.80", "v0.79-dn-v1.3", "v0.78-dn-v1", "v0.79-dn-v1.2" };
            Assert.That(ForkRelease.Newer(tags, x => x, new Version(1, 1)), Is.EqualTo(new[] { "v0.79-dn-v1.3", "v0.79-dn-v1.2" }));
            Assert.That(ForkRelease.Newer(tags, x => x, new Version(1, 3)), Is.Empty);
        }

        [Test]
        public void ThisBuild_IsNewerThanEveryPublishedRelease() =>
            Assert.That(ForkRelease.Current, Is.GreaterThan(ForkRelease.Parse("v0.78-dn-v1.1")));
    }
}
