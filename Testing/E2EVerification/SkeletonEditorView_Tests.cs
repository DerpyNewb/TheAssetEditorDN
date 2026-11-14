using System.Text.RegularExpressions;
using System.Xml.Linq;
using Editor.VisualSkeletonEditor.SkeletonEditor;

namespace Test.E2EVerification
{
    // The Skeleton editor's panel. Its file path was a read-only label, so Save could only write over the file it opened.
    internal class SkeletonEditorView_Tests
    {
        static string ViewFile() => Path.Combine(SettingsView_Tests.Root(), "Editors", "SkeletonEditor", "Editor.VisualSkeletonEditor", "SkeletonEditor", "EditorView.xaml");

        [Test]
        public void TheSkeletonPath_IsAnEditableBox()
        {
            var bound = SettingsView_Tests.Bindings(XDocument.Load(ViewFile())).Where(x => x.Path == nameof(SkeletonEditorViewModel.SkeletonName)).Select(x => x.Element).ToList();

            Assert.That(bound, Has.Count.EqualTo(1));
            Assert.That(bound[0].Name, Is.EqualTo(SettingsView_Tests.Wpf + "TextBox"));
            Assert.That(bound[0].Attribute("IsReadOnly")?.Value, Is.Not.EqualTo("True"));
            Assert.That(bound[0].Attribute("Text")!.Value, Does.Contain("Mode=TwoWay"));
        }

        [Test]
        public void EveryButton_CallsAMethodTheEditorHas()
        {
            var methods = Regex.Matches(File.ReadAllText(ViewFile()), @"\{s:MethodBinding (\w+)\}").Select(m => m.Groups[1].Value).Distinct().ToList();

            Assert.That(methods, Does.Contain(nameof(SkeletonEditorViewModel.SaveSkeletonAsAction)));
            Assert.That(methods.Where(m => typeof(SkeletonEditorViewModel).GetMethod(m) == null), Is.Empty);
        }

        [Test]
        public void EveryText_IsInEveryLanguage()
        {
            var keys = SettingsView_Tests.LocKeys(ViewFile());

            Assert.That(keys, Does.Contain("SkeletonEditor.Buttons.SaveAs"));
            Assert.That(SettingsView_Tests.MissingInALanguage(keys), Is.Empty);
        }
    }
}
