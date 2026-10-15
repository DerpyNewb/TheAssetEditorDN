using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Test.E2EVerification
{
    // The Settings window as tabs with groups. WPF drops a binding to a missing property and LocalizationManager prints a
    // missing key as the key, both without a word, so the XAML is read here against the view model and the language files.
    internal class SettingsView_Tests
    {
        internal static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        static readonly Regex BindingPath = new(@"^\{Binding\s+(?:Path=)?([A-Za-z_]\w*)(?=[\s,.}])");
        static readonly Regex LocKey = new(@"\{loc:Loc ([^}\s]+)\}");

        internal static string Root()
        {
            var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "AssetEditor.sln")))
                root = root.Parent;
            Assert.That(root, Is.Not.Null, "no AssetEditor.sln above the test directory");
            return root!.FullName;
        }

        static string SettingsFile(string name) => Path.Combine(Root(), "AssetEditor", "Views", "Settings", name);
        static XDocument View() => XDocument.Load(SettingsFile("SettingsView.xaml"));
        static Type ViewModel(string name) => typeof(AssetEditor.ViewModels.MainViewModel).Assembly.GetType("AssetEditor.ViewModels." + name)!;

        internal static IEnumerable<(XElement Element, string Path)> Bindings(XContainer scope) =>
            scope.Descendants().SelectMany(e => e.Attributes().Select(a => (e, BindingPath.Match(a.Value))))
                .Where(x => x.Item2.Success).Select(x => (x.e, x.Item2.Groups[1].Value));

        static bool InTemplate(XElement e) => e.Ancestors(Wpf + "DataTemplate").Any();

        [Test]
        public void EveryBinding_NamesAPropertyItsItemHas()
        {
            var settings = ViewModel("SettingsViewModel");
            var gameFolder = ViewModel("GamePathItem");
            var bindings = Bindings(View()).ToList();

            var missing = bindings.Where(b => (InTemplate(b.Element) ? gameFolder : settings).GetProperty(b.Path) == null)
                .Select(b => b.Path).ToList();

            Assert.That(bindings.Count(b => InTemplate(b.Element)), Is.GreaterThan(0), "the game folder rows are bound to nothing");
            Assert.That(missing, Is.Empty);
        }

        [Test]
        public void TheView_IsFourTabs_AndEverySettingSitsInOneOfThem_UnderAGroupTitle()
        {
            var view = View();
            var tabs = view.Descendants(Wpf + "TabItem").ToList();
            // A setting is a field CommunityToolkit makes a property of; the generated name drops the underscore and capitalises.
            var settings = ViewModel("SettingsViewModel").GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.GetCustomAttribute<ObservablePropertyAttribute>() != null)
                .Select(f => char.ToUpperInvariant(f.Name[1]) + f.Name[2..]).ToList();

            var placed = settings.ToDictionary(s => s, s => tabs.Count(t => Bindings(t).Any(b => b.Path == s)));

            Assert.That(tabs, Has.Count.EqualTo(4));
            Assert.That(settings, Has.Count.GreaterThan(5), "the view model's settings were not found");
            Assert.That(placed.Where(x => x.Value != 1).Select(x => x.Key), Is.Empty, "a setting in no tab, or in two");
            Assert.That(tabs.Where(t => t.Descendants(Wpf + "TextBlock").Any(b => (string?)b.Attribute("Style") == "{StaticResource GroupTitle}") == false), Is.Empty,
                "a tab with no group title");
        }

        [Test]
        public void EveryTextTheSettingsWindowShows_IsInEveryLanguage()
        {
            var keys = LocKeys(SettingsFile("SettingsView.xaml"), SettingsFile("SettingsWindow.xaml"));

            Assert.That(keys, Has.Count.GreaterThan(10), "the walk found too few keys to mean anything");
            Assert.That(MissingInALanguage(keys), Is.Empty);
        }

        // ThemesController loads Themes/ColourDictionaries/<ThemeType>.xaml by name, and a missing key draws as nothing.
        [Test]
        public void EveryTheme_HasItsColourFile_WithEveryKeyTheDarkOneHas()
        {
            static List<string> Keys(string theme) => XDocument.Load(Path.Combine(Root(), "AssetEditor", "Themes", "ColourDictionaries", theme + ".xaml"))
                .Root!.Elements().Select(e => (string)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!).ToList();

            var dark = Keys(nameof(Shared.Core.Settings.ThemeType.DarkTheme));
            Assert.That(dark, Has.Count.GreaterThan(100));
            foreach (var theme in Enum.GetValues<Shared.Core.Settings.ThemeType>())
                Assert.That(Keys(theme.ToString()), Is.EqualTo(dark), theme.ToString());
        }

        [Test]
        public void EveryRenderBackground_HasANameAndAColour()
        {
            foreach (var colour in Enum.GetValues<Shared.Core.Settings.BackgroundColour>())
            {
                Assert.That(Shared.Core.Settings.ApplicationSettingsHelper.GetEnumAsString(colour), Is.Not.Empty);
                Assert.That(() => Shared.Core.Settings.ApplicationSettingsHelper.GetEnumAsColour(colour), Throws.Nothing, colour.ToString());
            }
        }

        internal static List<string> LocKeys(params string[] files) =>
            files.SelectMany(f => LocKey.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value)).Distinct().ToList();

        internal static List<string> MissingInALanguage(List<string> keys) => new[] { "En", "Fr", "Cn" }.SelectMany(lang =>
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "AssetEditor", $"Language_{lang}.json")));
            return keys.Where(k => json.RootElement.TryGetProperty(k, out _) == false).Select(k => $"{lang}: {k}").ToList();
        }).ToList();
    }
}
