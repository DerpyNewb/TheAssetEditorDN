using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AvalonDock;
using AvalonDock.Layout;
using Editors.Shared.Core.Common.BaseControl;
using Shared.Core.ToolCreation;

namespace Test.Editors.Shared.Core
{
    // A 3D editor's layout is kept per editor type. A file that is damaged, or names other panes than the editor has, loads
    // nothing and stays on disk; only Reset layout deletes it.
    [Apartment(ApartmentState.STA)]
    public class DockLayoutStoreTests
    {
        const EditorEnums Key = EditorEnums.SuperView_Editor;
        string _folder = "";
        readonly List<Window> _windows = [];

        [SetUp]
        public void SetUp() => _folder = Directory.CreateTempSubdirectory("docklayout").FullName;

        [TearDown]
        public void TearDown()
        {
            _windows.ForEach(x => x.Close());
            _windows.Clear();
            Directory.Delete(_folder, true);
        }

        // The editors' default: the 3D view, the player under it, scene content over the editor's panel on the right.
        DockingManager Dock()
        {
            static LayoutAnchorable Pane(string id) => new() { ContentId = id, Title = id, Content = new TextBlock { Text = id } };
            var group = new LayoutAnchorablePaneGroup { Orientation = Orientation.Vertical, DockWidth = new GridLength(400) };
            group.Children.Add(new LayoutAnchorablePane(Pane("SceneContent")));
            group.Children.Add(new LayoutAnchorablePane(Pane("EditorPanel")));
            var left = new LayoutPanel { Orientation = Orientation.Vertical };
            left.Children.Add(new LayoutDocumentPane(new LayoutDocument { ContentId = "GameView", Title = "3D view", CanClose = false, CanFloat = false, Content = new ContentControl() }));
            left.Children.Add(new LayoutAnchorablePane(Pane("AnimationPlayer")) { DockHeight = new GridLength(130) });
            var root = new LayoutPanel { Orientation = Orientation.Horizontal };
            root.Children.Add(left);
            root.Children.Add(group);
            var dock = new DockingManager { Layout = new LayoutRoot { RootPanel = root } };

            var window = new Window { Content = dock, Width = 1600, Height = 900, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000 };
            _windows.Add(window);
            window.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            window.UpdateLayout();
            return dock;
        }

        static LayoutAnchorable Pane(DockingManager dock, string id) => dock.Layout.Descendents().OfType<LayoutAnchorable>().Single(x => x.ContentId == id);

        static string Serialized(DockingManager dock)
        {
            using var writer = new StringWriter();
            new AvalonDock.Serializer.Xml.XmlLayoutSerializer(dock).Serialize(writer);
            return writer.ToString();
        }

        DockLayoutStore Store() => new(_folder);

        [Test]
        public void Save_ThenTryLoad_IntoAFreshDock_PutsEveryPaneBack()
        {
            var dock = Dock();
            var panel = Pane(dock, "EditorPanel");
            var scenePane = (LayoutAnchorablePane)Pane(dock, "SceneContent").Parent;
            ((ILayoutContainer)panel.Parent).RemoveChild(panel);
            scenePane.Children.Add(panel);
            Pane(dock, "AnimationPlayer").ToggleAutoHide();
            Store().Save(dock, Key);

            var fresh = Dock();
            Assert.That(Store().TryLoad(fresh, Key), Is.True);
            Assert.That(Pane(fresh, "EditorPanel").Parent, Is.SameAs(Pane(fresh, "SceneContent").Parent), "the panel tabbed with scene content");
            Assert.That(Pane(fresh, "AnimationPlayer").IsAutoHidden, Is.True);
            Assert.That(Pane(fresh, "SceneContent").Content, Is.InstanceOf<TextBlock>(), "the live content, not an empty pane");
            var document = fresh.Layout.Descendents().OfType<LayoutDocument>().Single();
            Assert.That((document.CanClose, document.CanFloat), Is.EqualTo((false, false)));
        }

        [Test]
        public void ClosedPane_StaysClosedAfterLoad()
        {
            var dock = Dock();
            Pane(dock, "SceneContent").Hide();
            Store().Save(dock, Key);

            var fresh = Dock();
            Assert.That(Store().TryLoad(fresh, Key), Is.True);
            Assert.That(Pane(fresh, "SceneContent").IsHidden, Is.True);
        }

        [Test]
        public void AFileNamingAnUnknownPane_LoadsNothing_AndIsKept()
        {
            var dock = Dock();
            Store().Save(dock, Key);
            var path = Store().PathFor(Key);
            File.WriteAllText(path, File.ReadAllText(path).Replace("ContentId=\"EditorPanel\"", "ContentId=\"EditorPanel\" /><LayoutAnchorable ContentId=\"Gone\""));
            Assert.That(File.ReadAllText(path), Does.Contain("\"Gone\""), "the file was not changed, so this proves nothing");

            var fresh = Dock();
            var before = Serialized(fresh);
            Assert.That(Store().TryLoad(fresh, Key), Is.False);
            Assert.That(Serialized(fresh), Is.EqualTo(before));
            Assert.That(File.Exists(path), Is.True);
        }

        [Test]
        public void AFileMissingALivePane_LoadsNothing()
        {
            var dock = Dock();
            Pane(dock, "AnimationPlayer").Hide();
            var hidden = Pane(dock, "AnimationPlayer");
            dock.Layout.Hidden.Remove(hidden);
            Store().Save(dock, Key);
            Assert.That(File.ReadAllText(Store().PathFor(Key)), Does.Not.Contain("AnimationPlayer"), "the pane is still in the file, so this proves nothing");

            Assert.That(Store().TryLoad(Dock(), Key), Is.False);
        }

        [Test]
        public void ATruncatedFile_LoadsNothing_NoException_AndIsKept()
        {
            Store().Save(Dock(), Key);
            var path = Store().PathFor(Key);
            var text = File.ReadAllText(path);
            File.WriteAllText(path, text[..(text.Length / 2)]);

            var fresh = Dock();
            var before = Serialized(fresh);
            Assert.That(Store().TryLoad(fresh, Key), Is.False);
            Assert.That(Serialized(fresh), Is.EqualTo(before));
            Assert.That(File.Exists(path), Is.True);
        }

        [Test]
        public void NoFile_LoadsNothing() => Assert.That(Store().TryLoad(Dock(), Key), Is.False);

        // Two editors of one type: the second one closed writes while nothing stops the first's file being read.
        [Test]
        public void Save_WhileTheFileIsOpenForReading_DoesNotThrow()
        {
            var dock = Dock();
            Store().Save(dock, Key);
            using var reader = new FileStream(Store().PathFor(Key), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Assert.DoesNotThrow(() => Store().Save(dock, Key));
            Assert.DoesNotThrow(() => Store().Save(dock, Key));
            Assert.That(Directory.EnumerateFiles(_folder, "*.tmp"), Is.Empty, "a save that could not land left its temp file");
        }

        [Test]
        public void Delete_RemovesTheFile()
        {
            Store().Save(Dock(), Key);
            Store().Delete(Key);
            Assert.That(File.Exists(Store().PathFor(Key)), Is.False);
        }

        [Test]
        public void ThePath_IsOneFilePerEditorType()
        {
            Assert.That(Store().PathFor(EditorEnums.UnitSimulator_Editor), Is.EqualTo(Path.Combine(_folder, "UnitSimulator_Editor.xml")));
            Assert.That(DockLayoutStore.DefaultFolder, Is.EqualTo(Path.Combine(Path.GetDirectoryName(global::Shared.Core.Settings.ApplicationSettingsService.SettingsFile)!, "layouts")));
        }
    }
}
