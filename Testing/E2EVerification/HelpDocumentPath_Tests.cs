using System.Text.RegularExpressions;

namespace Test.E2EVerification
{
    // A window's help button opens HelpDocumentPath next to the exe, where the build copies docs/. Three windows pointed at
    // docs\index.html#doc=..., which redirects to the Installing page whatever the fragment says, so every help button opened
    // the wrong page and no check said so.
    internal class HelpDocumentPath_Tests
    {
        static readonly Regex HelpPath = new(@"HelpDocumentPath=""([^""]+)""");

        [Test]
        public void EveryHelpButtonOpensAPageThatExists()
        {
            var root = SettingsView_Tests.Root();
            var paths = Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "artifacts" + Path.DirectorySeparatorChar))
                .SelectMany(f => HelpPath.Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Path: m.Groups[1].Value)))
                .ToList();

            Assert.That(paths, Has.Count.GreaterThanOrEqualTo(8), "the windows that have a page");
            foreach (var (file, path) in paths)
            {
                var page = path.Split('#', '?')[0];
                Assert.That(Path.GetFileName(page), Is.Not.EqualTo("index.html"), $"{file}: index.html redirects to Installing");
                Assert.That(File.Exists(Path.Combine(root, page)), Is.True, $"{file}: {path}");
            }
        }
    }
}
