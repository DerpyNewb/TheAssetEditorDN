using System.Collections;
using Editors.CscEditor.Data;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Esf;
using Test.TestingUtility.Shared;

namespace Test.E2EVerification
{
    // Scratch, not committed: every .csc in the game's packs through the editor's load and save, compared byte for byte and
    // decoded value for decoded value.
    internal class CscCorpusScratch
    {
        static EsfDocument Esf(byte[] bytes) => EsfReader.Read(new MemoryStream(bytes));

        static byte[] EsfBytes(EsfDocument doc)
        {
            using var stream = new MemoryStream();
            EsfWriter.Write(doc, stream);
            return stream.ToArray();
        }

        // The first place two decoded trees disagree, or null when they hold the same records and values.
        static string? Mismatch(EsfNode a, EsfNode b, string at)
        {
            if (a.Kind != b.Kind || a.Name != b.Name || a.Version != b.Version)
                return $"{at}: {a.Kind} {a.Name} v{a.Version} vs {b.Kind} {b.Name} v{b.Version}";
            if (!SameValue(a.Value, b.Value))
                return $"{at} ({a.Kind}): {Show(a.Value)} vs {Show(b.Value)}";
            var ga = a.Groups ?? [];
            var gb = b.Groups ?? [];
            if (ga.Count != gb.Count)
                return $"{at}: {ga.Count} groups vs {gb.Count}";
            for (var g = 0; g < ga.Count; g++)
            {
                if (ga[g].Count != gb[g].Count)
                    return $"{at}/{g}: {ga[g].Count} fields vs {gb[g].Count}";
                for (var i = 0; i < ga[g].Count; i++)
                    if (Mismatch(ga[g][i], gb[g][i], $"{at}/{g}.{i}") is { } m)
                        return m;
            }
            return null;
        }

        static bool SameValue(object? a, object? b) =>
            a is IEnumerable ea && a is not string && b is IEnumerable eb
                ? ea.Cast<object>().SequenceEqual(eb.Cast<object>())
                : Equals(a, b);

        // The scene as the editor reads it: every element's parent, children in order, and records; the header and the tail.
        static string? SceneMismatch(CscScene a, CscScene b)
        {
            static string? Fields(List<EsfNode> x, List<EsfNode> y, string at)
            {
                if (x.Count != y.Count)
                    return $"{at}: {x.Count} vs {y.Count}";
                for (var i = 0; i < x.Count; i++)
                    if (Mismatch(x[i], y[i], $"{at}.{i}") is { } m)
                        return m;
                return null;
            }
            if (Fields(a.HeaderFields, b.HeaderFields, "header") is { } h)
                return h;
            if (a.Elements.Count != b.Elements.Count)
                return $"elements {a.Elements.Count} vs {b.Elements.Count}";
            for (var i = 0; i < a.Elements.Count; i++)
            {
                var (ea, eb) = (a.Elements[i], b.Elements[i]);
                if (ea.Id != eb.Id || ea.Parent?.Id != eb.Parent?.Id)
                    return $"element {i}: id {ea.Id} parent {ea.Parent?.Id} vs id {eb.Id} parent {eb.Parent?.Id}";
                if (!ea.Children.Select(c => c.Id).SequenceEqual(eb.Children.Select(c => c.Id)))
                    return $"element {ea.Id}: children [{string.Join(",", ea.Children.Select(c => c.Id))}] vs [{string.Join(",", eb.Children.Select(c => c.Id))}]";
                if (Fields(ea.Records, eb.Records, $"element {ea.Id} records") is { } r)
                    return r;
            }
            return Fields(a.TailFields, b.TailFields, "tail");
        }

        static string Show(object? v) => v is IEnumerable e && v is not string ? $"[{string.Join(",", e.Cast<object>().Take(6))}...]" : $"{v}";

        [Test, Explicit]
        public void RoundTripTheGamesCscFiles()
        {
            var data = Environment.GetEnvironmentVariable("CSC_DATA") ?? @"F:\SteamLibrary\steamapps\common\Total War WARHAMMER III\data";
            var loader = new AssetEditorTestRunner().ServiceProvider.GetRequiredService<IPackFileContainerLoader>();
            var files = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(data, "*.pack").Order(StringComparer.OrdinalIgnoreCase))
                foreach (var (name, file) in loader.CreateFromPackFile(PackFileContainerType.Normal, path, true).GetAllFiles())
                    if (name.EndsWith(".csc", StringComparison.OrdinalIgnoreCase))
                        files[name] = file.DataSource.ReadData();

            int sameScene = 0, loadFailed = 0, identical = 0, sameAsEsf = 0, sameContent = 0, manifest = 0, esfIdentical = 0;
            var v6 = new SortedDictionary<string, int>();
            var notes = new List<string>();
            foreach (var (name, bytes) in files)
            {
                var original = Esf(bytes);
                var esfWritten = EsfBytes(original);
                if (esfWritten.AsSpan().SequenceEqual(bytes))
                    esfIdentical++;

                CscScene scene;
                try
                {
                    scene = CscScene.Load(bytes);
                }
                catch
                {
                    loadFailed++;
                    var folder = string.Join("\\", name.Split('\\').Take(3));
                    v6[folder] = v6.GetValueOrDefault(folder) + 1;
                    continue;
                }
                if (scene.ManifestParsed)
                    manifest++;

                var written = CscSceneWriter.Write(scene);
                if (written.AsSpan().SequenceEqual(bytes))
                    identical++;
                if (written.AsSpan().SequenceEqual(esfWritten))
                    sameAsEsf++;
                var mismatch = Mismatch(original.Root, Esf(written).Root, "ROOT");
                if (mismatch == null)
                    sameContent++;
                var sceneMismatch = SceneMismatch(scene, CscScene.Load(written));
                if (sceneMismatch == null)
                    sameScene++;
                else if (notes.Count < 10)
                    notes.Add($"SCENE {name}: {sceneMismatch}");
            }

            // CA's own placeholder, the starting point for a .csc of one's own: open it, add to it, save, read it back.
            if (files.TryGetValue(@"composite_scene\spells_and_abilities\empty.csc", out var emptyBytes))
            {
                var empty = CscScene.Load(emptyBytes);
                var model = CscElementFactory.Create(empty, CscElementKind.Model, @"variantmeshes\wh_variantmodels\hu1\chd\chd_props\thing.rigid_model_v2");
                empty.AddElement(model);
                var vfx = CscElementFactory.Create(empty, CscElementKind.Vfx, "chd_magma_burst");
                empty.AddElement(vfx, parent: model);
                model.Position!.Channels[1].AddKeyframe(0, 0);
                model.Position.Channels[1].AddKeyframe(2, 3);
                var back = CscScene.Load(CscSceneWriter.Write(empty));
                TestContext.Out.WriteLine($"empty.csc: {emptyBytes.Length} bytes, ROOT v{empty.RootVersion}, keyframe add/remove {empty.ManifestParsed}; " +
                    $"after adding a model with a VFX under it and saving: {back.Elements.Count} elements, VFX parent {back.Elements.FirstOrDefault(e => e.Kind == CscElementKind.Vfx)?.Parent?.Kind}, " +
                    $"model keyframes {back.Elements.First(e => e.Kind == CscElementKind.Model).Position?.Channels[1].Keyframes.Count}");
            }
            else
                TestContext.Out.WriteLine("empty.csc not found");

            var loaded = files.Count - loadFailed;
            TestContext.Out.WriteLine($"CSC files={files.Count}; the scene editor opens {loaded}, keyframe add/remove on {manifest}");
            TestContext.Out.WriteLine($"ESF reader+writer alone, byte-identical: {esfIdentical}");
            TestContext.Out.WriteLine($"Opened and saved unedited: byte-identical {identical}, same bytes as the ESF layer alone {sameAsEsf}, same decoded content {sameContent} of {loaded}, same scene as the editor reads it {sameScene} of {loaded}");
            TestContext.Out.WriteLine("Not opened (ROOT v6), by folder: " + string.Join("; ", v6.OrderByDescending(x => x.Value).Select(x => $"{x.Key}={x.Value}")));
            foreach (var note in notes)
                TestContext.Out.WriteLine(note);
        }
    }
}
