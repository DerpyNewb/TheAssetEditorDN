using System.IO;
using System.Xml.Linq;
using AvalonDock;
using AvalonDock.Layout;
using AvalonDock.Serializer.Xml;
using Serilog;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.CoreLog;

namespace Editors.Shared.Core.Common.BaseControl
{
    /// <summary>A 3D editor's dock layout, one XML file per editor type in a folder beside the settings file. A file that does
    /// not parse, or names other panes than the editor has, loads nothing and is left on disk; only <see cref="Delete"/> removes one.</summary>
    public sealed class DockLayoutStore(string folder)
    {
        static readonly ILogger _logger = Logging.Create<DockLayoutStore>();

        public static string DefaultFolder => Path.Combine(Path.GetDirectoryName(ApplicationSettingsService.SettingsFile)!, "layouts");

        public string PathFor(EditorEnums key) => Path.Combine(folder, $"{key}.xml");

        /// <summary>Never throws: it runs inside an editor's Close and on app exit. A save that cannot land is logged and the
        /// layout saved before it stays.</summary>
        public void Save(DockingManager dock, EditorEnums key)
        {
            var path = PathFor(key);
            var temp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(folder);
                using (var writer = new StreamWriter(temp))
                    new XmlLayoutSerializer(dock).Serialize(writer);
                File.Move(temp, path, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _logger.Here().Warning($"Layout {path} could not be saved, the one saved before stays: {e.Message}");
                try { File.Delete(temp); } catch (Exception x) when (x is IOException or UnauthorizedAccessException) { }
            }
        }

        public bool TryLoad(DockingManager dock, EditorEnums key)
        {
            var path = PathFor(key);
            if (File.Exists(path) == false)
                return false;

            string text;
            HashSet<string?> saved;
            try
            {
                text = File.ReadAllText(path);
                saved = XDocument.Parse(text).Descendants().Where(x => x.Name.LocalName is nameof(LayoutDocument) or nameof(LayoutAnchorable))
                    .Select(x => (string?)x.Attribute("ContentId")).ToHashSet();
            }
            catch (Exception e)
            {
                _logger.Here().Warning($"Layout {path} is damaged, the default layout is used: {e.Message}");
                return false;
            }

            var live = dock.Layout.Descendents().OfType<LayoutContent>().ToDictionary(x => x.ContentId, x => x.Content);
            if (saved.SetEquals(live.Keys) == false)
            {
                _logger.Here().Warning($"Layout {path} names panes [{string.Join(", ", saved)}], this editor has [{string.Join(", ", live.Keys)}]; the default layout is used");
                return false;
            }

            var before = Serialize(dock);
            try
            {
                Deserialize(dock, text, live);
                return true;
            }
            catch (Exception e)
            {
                _logger.Here().Warning($"Layout {path} could not be loaded, the default layout is used: {e.Message}");
                Deserialize(dock, before, live);
                return false;
            }
        }

        public void Delete(EditorEnums key) => File.Delete(PathFor(key));

        /// <summary>The dock's layout as the XML a saved file holds.</summary>
        public static string Serialize(DockingManager dock)
        {
            using var writer = new StringWriter();
            new XmlLayoutSerializer(dock).Serialize(writer);
            return writer.ToString();
        }

        /// <summary>Puts a layout back, each pane holding the live content its ContentId names.</summary>
        public static void Deserialize(DockingManager dock, string layout) =>
            Deserialize(dock, layout, dock.Layout.Descendents().OfType<LayoutContent>().ToDictionary(x => x.ContentId, x => x.Content));

        static void Deserialize(DockingManager dock, string layout, Dictionary<string, object> live)
        {
            var serializer = new XmlLayoutSerializer(dock);
            serializer.LayoutSerializationCallback += (_, e) => e.Content = live[e.Model.ContentId];
            using var reader = new StringReader(layout);
            serializer.Deserialize(reader);
        }
    }
}
