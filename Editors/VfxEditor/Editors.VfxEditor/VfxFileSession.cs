using System.Text;
using Editors.VfxEditor.Document;
using GameWorld.Core.Vfx;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;

namespace Editors.VfxEditor
{
    /// <summary>The open file (spec section 8): its document, where it lives, and saving through the AssetEditor's save
    /// service. Save writes to this path in the editable pack, so a file opened from CA's packs is created there.</summary>
    public sealed class VfxFileSession(IPackFileService packFileService, IFileSaveService fileSaveService)
    {
        const string ReopenHint = "Open in text editor shows its text, where it can be fixed.";

        public PackFile? File { get; private set; }
        public VfxDocument? Document { get; private set; }
        public string? LoadError { get; private set; }
        public bool HasUnsavedChanges { get; set; }

        /// <summary>What the last save did to other files: the ones whose references to this effect it moved to a renamed
        /// emitter's new id, and the ones outside the editable pack that still name an old id.</summary>
        public string Carried { get; private set; } = "";

        // Emitters renamed since the last save, old id to new. Saving carries them to other effects' references to this one,
        // so a rename that is undone or never saved changes no other file.
        readonly Dictionary<string, string> _renames = [];

        public void Renamed(string id, string newId)
        {
            var carried = _renames.Where(x => x.Value == id).Select(x => x.Key).ToList();
            foreach (var old in carried)
                _renames[old] = newId;
            if (carried.Count == 0)
                _renames[id] = newId;
        }

        public void Load(PackFile file)
        {
            File = file;
            _renames.Clear();
            Document = null;
            LoadError = null;
            HasUnsavedChanges = false;
            try
            {
                var document = VfxDocument.Load(file.DataSource.ReadData());
                if (!document.IsEffect)
                {
                    LoadError = $"{file.Name} is not an effect: it has no <vfx> and no <vfx_references>. {ReopenHint}";
                    return;
                }
                document.Changed += () => HasUnsavedChanges = true;
                Document = document;
            }
            catch (VfxParseException e)
            {
                LoadError = $"{file.Name} did not parse: {e.Message}. {ReopenHint}";
            }
            catch (DecoderFallbackException e)
            {
                LoadError = $"{file.Name} is not valid text in its encoding: {e.Message}. {ReopenHint}";
            }
        }

        /// <summary>Vanilla's layout of an effect with nothing in it, as Terry writes one.</summary>
        const string EmptyEffect = "<shader_library file=\"fx/particle_vfx_library.hlsl\"/><vfx revision=\"2\" construction_type=\"2\" enable_in_ted=\"false\" camera_following=\"false\" ref_spawn_speed=\"-1.00\">\r\n" +
            "\t<!-- EMITTERS-->\r\n\t<emitters></emitters>\r\n\t<!-- BEHAVIOUR_CHAINS-->\r\n\t<behaviour_chains></behaviour_chains>\r\n" +
            "\t<!-- EFFECT RENDER STACKS-->\r\n\t<effect_render_stacks></effect_render_stacks>\r\n" +
            "</vfx><vfx_references></vfx_references><!-- EDITOR DATA--><editor_data>\r\n\t<effect_components></effect_components>\r\n</editor_data>";

        /// <summary>A new effect with no file yet, in UTF-16 as CA's are. Save asks where it goes.</summary>
        public void New()
        {
            File = null;
            LoadError = null;
            Document = VfxDocument.Load([0xFF, 0xFE, .. Encoding.Unicode.GetBytes(EmptyEffect)]);
            Document.Changed += () => HasUnsavedChanges = true;
            HasUnsavedChanges = true;
        }

        /// <summary>The name an EFFECT tag or a reference uses for this file: its path under vfx\ without ".xml".</summary>
        public string EffectName
        {
            get
            {
                var path = File == null ? "" : packFileService.GetFullPath(File).Replace('/', '\\');
                if (path.StartsWith("vfx\\", StringComparison.OrdinalIgnoreCase))
                    path = path[4..];
                return (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? path[..^4] : path).Replace('\\', '/');
            }
        }

        public bool Save()
        {
            if (File == null)
                return SaveAs();
            if (!Store(fileSaveService.Save(packFileService.GetFullPath(File), Document!.Save(), prompOnConflict: false)))
                return false;
            Carried = CarryRenames();
            return true;
        }

        // A copy under another name keeps the renames: nothing references the new name yet, so they have nowhere to go.
        public bool SaveAs() => Store(fileSaveService.SaveAs(".xml", Document!.Save()));

        /// <summary>Points the override rows that other effects' references to this one hold at the renamed emitters' new ids,
        /// in the editable pack's files. A file in another pack is listed instead, as only its own pack can change it. CA's
        /// files are read only when this effect is one of CA's, since no other can be referenced from them.</summary>
        string CarryRenames()
        {
            // A rename since undone, or renamed back, has its old id in the file again, unless another rename has taken that id.
            var ids = Document!.Section("emitters")?.ChildrenNamed("emitter").Select(e => e.Attr("id") ?? "").ToHashSet() ?? [];
            var renames = _renames.Where(x => x.Key != x.Value && (!ids.Contains(x.Key) || _renames.ContainsValue(x.Key)))
                .ToDictionary(x => x.Key, x => x.Value);
            _renames.Clear();
            if (renames.Count == 0)
                return "";

            var ownPath = packFileService.GetFullPath(File!);
            var self = Key(EffectName);
            var editable = packFileService.GetEditablePack();
            var containers = packFileService.GetAllPackfileContainers();
            var vanilla = containers.Any(c => c.IsCaPackFile && packFileService.FindFile(ownPath, c) != null);
            var paths = containers.Where(c => vanilla || !c.IsCaPackFile)
                .SelectMany(c => packFileService.FindAllWithExtention(".xml", c)).Select(x => x.FileName)
                .Where(p => p.StartsWith("vfx\\", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var leaf = self[(self.LastIndexOf('/') + 1)..^4];

            var (moved, elsewhere) = (new List<string>(), new List<string>());
            foreach (var path in paths)
            {
                var own = editable == null ? null : packFileService.FindFile(path, editable);
                if ((own ?? packFileService.FindFile(path)) is not { } file)
                    continue;
                VfxDocument document;
                try
                {
                    var bytes = file.DataSource.ReadData();
                    var text = bytes is [0xFF, 0xFE, ..] ? Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2) : Encoding.UTF8.GetString(bytes);
                    if (!text.Contains(leaf, StringComparison.OrdinalIgnoreCase))
                        continue;
                    document = VfxDocument.Load(bytes);
                }
                catch (Exception)   // not an effect this editor reads, or a pack moved on disk
                {
                    continue;
                }
                var rows = document.Section("vfx_references")?.ChildrenNamed("vfx")
                    .Where(r => Key(r.Attr("vfx_ref") ?? "") == self)
                    .SelectMany(r => r.ChildrenNamed("emitter"))
                    .Where(r => renames.ContainsKey(r.Attr("name") ?? ""))
                    .ToList() ?? [];
                if (rows.Count == 0)
                    continue;
                if (own == null)
                {
                    elsewhere.Add(path);
                    continue;
                }
                document.Apply(rows.Select(r => document.SetAttribute(r, "name", renames[r.Attr("name")!])));
                packFileService.SaveFile(own, document.Save());
                moved.Add(path);
            }

            return (moved.Count == 0 ? "" : $"; references moved to the new ids in {string.Join(", ", moved)}")
                + (elsewhere.Count == 0 ? "" : $"; outside your pack, {string.Join(", ", elsewhere)} still name{(elsewhere.Count == 1 ? "s" : "")} an old id");
        }

        static string Key(string effectName) => VfxResolver.Key(effectName.Replace('\\', '/'));

        bool Store(PackFile? saved)
        {
            if (saved == null)
                return false;
            (File, Carried) = (saved, "");
            HasUnsavedChanges = false;
            return true;
        }

        /// <summary>The emitter ids of an effect in the loaded packs, for a new reference's override rows; null when the
        /// effect is not found or does not read.</summary>
        public IReadOnlyList<string>? EmitterIdsOf(string effectName)
        {
            var file = packFileService.FindFile(VfxResolver.PathOf(effectName));
            if (file == null)
                return null;
            try
            {
                var document = VfxDocument.Load(file.DataSource.ReadData());
                return document.Section("emitters")?.ChildrenNamed("emitter").Select(e => e.Attr("id") ?? "").ToList() ?? [];
            }
            catch (Exception)   // a pack moved on disk too, not only a file that does not parse
            {
                return null;
            }
        }
    }
}
