using System.IO;
using System.Text.RegularExpressions;

namespace Shared.UiTest.Common
{
    // XAML reads the text between tags as a literal, so {loc:Loc Key} written there prints the markup itself: Kitbash's tint
    // panel showed "{loc:Loc KitbashTool.TintView.Variation}" where "Variation:" belonged. Markup works only in an attribute.
    internal class LocMarkupTests
    {
        [Test]
        public void NoXamlWritesLocMarkupAsText()
        {
            var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "AssetEditor.sln")))
                root = root.Parent;
            Assert.That(root, Is.Not.Null, "no AssetEditor.sln above the test directory");

            var asText = new Regex(@">[^<]*\{loc:Loc [^}]*\}[^<]*<");
            var found = Xaml(root!).SelectMany(f => asText.Matches(File.ReadAllText(f))
                .Select(m => $"{Path.GetRelativePath(root!.FullName, f)}: {Regex.Replace(m.Value, @"\s+", " ")}")).ToList();

            Assert.That(Xaml(root!).Count(), Is.GreaterThan(100), "the walk found too few .xaml files to mean anything");
            Assert.That(found, Is.Empty);
        }

        static IEnumerable<string> Xaml(DirectoryInfo dir)
        {
            foreach (var file in dir.EnumerateFiles("*.xaml"))
                yield return file.FullName;
            foreach (var sub in dir.EnumerateDirectories().Where(d => d.Name is not ("bin" or "obj" or "artifacts" or ".git")))
                foreach (var file in Xaml(sub))
                    yield return file;
        }
    }
}
