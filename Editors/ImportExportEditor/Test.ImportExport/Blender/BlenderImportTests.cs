using System.IO;
using Editors.ImportExport.Blender;
using Editors.ImportExport.Exporting.Exporters.RmvToBlender;
using Editors.ImportExport.Importing.Importers.BlenderToRmv;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.Vertex;

namespace Test.ImportExport.Blender;

public class BlenderImportTests
{
    static readonly string TestScripts = Path.Combine(TestContext.CurrentContext.TestDirectory, "Blender", "Scripts");

    static (PackFileService Pfs, IPackFileContainer Container) Destination()
    {
        var pfs = new PackFileService(null);
        var container = pfs.CreateNewPackFileContainer("dest", PackFileVersion.PFH5, PackFileCAType.MOD, setEditablePack: true);
        return (pfs, container);
    }

    static string WriteRmv2(string folder, string packPath)
    {
        var path = Path.Combine(folder, Path.GetFileName(packPath));
        File.WriteAllBytes(path, RmvToBlenderExportTests.KarlPack().FindFile(packPath)!.DataSource.ReadData());
        return path;
    }

    /// <summary>Builds a .blend with make_test_blend.py.</summary>
    static string MakeBlend(string kind, string output, string? a = null, string? b = null, string? name = null)
    {
        var runner = BlenderRunnerTests.RealRunner();
        runner.ScriptPath = Path.Combine(TestScripts, "make_test_blend.py");
        runner.ExtraEnvironment["AE_TEST_KIND"] = kind;
        runner.ExtraEnvironment["AE_TEST_OUT"] = output;
        if (a != null) runner.ExtraEnvironment["AE_TEST_A"] = a;
        if (b != null) runner.ExtraEnvironment["AE_TEST_B"] = b;
        if (name != null) runner.ExtraEnvironment["AE_TEST_NAME"] = name;
        var made = runner.Run(new BlenderJob("test", "", output), BlenderRunner.NewJobFolder("make"));
        Assert.That(made.Ok && File.Exists(output), Is.True, made.Error);
        return output;
    }

    static int UniqueTriangles(RmvModel m) =>
        m.Mesh.IndexList.Chunk(3).Select(t => string.Join(",", t.Order())).Distinct().Count();

    internal static HashSet<(int, int, int)> Positions(IEnumerable<RmvModel> meshes) =>
        meshes.SelectMany(m => m.Mesh.VertexList)
            .Select(v => ((int)MathF.Round(v.Position.X * 100), (int)MathF.Round(v.Position.Y * 100), (int)MathF.Round(v.Position.Z * 100)))
            .ToHashSet();

    static RmvFile Load(IPackFileService pfs, string packPath) =>
        ModelFactory.Create().Load(pfs.FindFile(packPath)!.DataSource.ReadData());

    static string ExportHammerBlend(BlenderRunner runner, out string originalPath)
    {
        var source = RmvToBlenderExportTests.KarlPack();
        var original = source.FindFile(RmvToBlenderExportTests.KarlHammer)!;
        originalPath = source.GetFullPath(original);
        var blend = Path.Combine(RmvToBlenderExportTests.OutFolder("hammer_rt"), "hammer.blend");
        Assert.That(new RmvToBlenderExporter(source, runner).Export(original, blend).Ok, Is.True);
        return blend;
    }

    [Test]
    [CancelAfter(300_000)]
    public void RoundTripGoesBackToTheOriginalPathWhenTheUserSaysYes()
    {
        var runner = BlenderRunnerTests.RealRunner();
        var blend = ExportHammerBlend(runner, out var originalPath);
        var (pfs, container) = Destination();
        string? asked = null;

        var outcome = new BlenderImporter(pfs, runner).Import(blend, container, "test", null, 0, false, path => { asked = path; return true; });

        Assert.That(asked, Is.EqualTo(originalPath));
        Assert.That(outcome.AddedPackPaths, Is.EqualTo(new[] { originalPath.ToLowerInvariant() }));
        Assert.That(pfs.FindFile(originalPath, container), Is.Not.Null);
    }

    [Test]
    [CancelAfter(300_000)]
    public void RoundTripStaysInTheChosenFolderWhenTheUserSaysNo()
    {
        var runner = BlenderRunnerTests.RealRunner();
        var blend = ExportHammerBlend(runner, out _);
        var (pfs, container) = Destination();

        var outcome = new BlenderImporter(pfs, runner).Import(blend, container, "test", null, 0, false, _ => false);

        Assert.That(outcome.AddedPackPaths, Has.Count.EqualTo(1));
        Assert.That(outcome.AddedPackPaths[0], Does.StartWith(@"test\"));
    }

    [Test]
    [CancelAfter(300_000)]
    public void KarlRoundTripKeepsLodsCountsAndSkeleton()
    {
        var runner = BlenderRunnerTests.RealRunner();
        var source = RmvToBlenderExportTests.KarlPack();
        var original = source.FindFile(RmvToBlenderExportTests.KarlBody)!;
        var blend = Path.Combine(RmvToBlenderExportTests.OutFolder("rt"), "emp_karl_franz.blend");
        Assert.That(new RmvToBlenderExporter(source, runner).Export(original, blend).Ok, Is.True);
        var (pfs, container) = Destination();

        var outcome = new BlenderImporter(pfs, runner).Import(blend, container, "test", null, 0, false);

        Assert.That(outcome.Result.Ok, Is.True, outcome.Result.Error);
        Assert.That(outcome.AddedPackPaths, Has.Count.EqualTo(1));
        var before = ModelFactory.Create().Load(original.DataSource.ReadData());
        var after = Load(pfs, outcome.AddedPackPaths[0]);
        Assert.That(after.Header.LodCount, Is.EqualTo(before.Header.LodCount));
        Assert.That(after.Header.SkeletonName, Is.EqualTo(before.Header.SkeletonName));
        for (var lod = 0; lod < before.Header.LodCount; lod++)
        {
            Assert.That(after.ModelList[lod].Length, Is.EqualTo(before.ModelList[lod].Length), $"mesh count, LOD {lod}");
            // CA's LOD3 repeats 2 triangles (same 3 indices twice); a Blender mesh cannot hold a face twice,
            // so unique triangles are what must survive.
            Assert.That(after.ModelList[lod].Sum(UniqueTriangles), Is.EqualTo(before.ModelList[lod].Sum(UniqueTriangles)), $"unique triangles, LOD {lod}");
            // Blender splits vertices at UV/normal seams (+131 of 9156 on LOD0, measured), so the vertex COUNT
            // moves; the distinct positions must not.
            Assert.That(Positions(after.ModelList[lod]), Is.EquivalentTo(Positions(before.ModelList[lod])), $"vertex positions, LOD {lod}");
        }
        Assert.That(SkinMismatches(before.ModelList[0], after.ModelList[0]), Is.Empty, "skin weights, LOD 0");
        Assert.That(outcome.RoundTripPackPath, Is.EqualTo(source.GetFullPath(original)));
    }

    /// <summary>
    /// Positions whose bone->weight map moved by more than 0.02 on any bone. Order-free on purpose: near-equal
    /// weights swap rank after the 8-bit re-quantisation (49 of LOD0's positions did), which is not a lost skin.
    /// </summary>
    static List<string> SkinMismatches(IEnumerable<RmvModel> before, IEnumerable<RmvModel> after)
    {
        static (int, int, int) Key(CommonVertex v) => ((int)MathF.Round(v.Position.X * 100), (int)MathF.Round(v.Position.Y * 100), (int)MathF.Round(v.Position.Z * 100));
        static Dictionary<int, float> Map(CommonVertex v)
        {
            var map = new Dictionary<int, float>();
            for (var i = 0; i < (v.BoneWeight?.Length ?? 0); i++)
                if (v.BoneWeight![i] > 0)
                    map[v.BoneIndex[i]] = map.GetValueOrDefault(v.BoneIndex[i]) + v.BoneWeight[i];
            return map;
        }
        static bool Close(Dictionary<int, float> a, Dictionary<int, float> b) =>
            a.Keys.Union(b.Keys).All(k => MathF.Abs(a.GetValueOrDefault(k) - b.GetValueOrDefault(k)) <= 0.02f);

        var afterByPos = after.SelectMany(m => m.Mesh.VertexList).GroupBy(Key).ToDictionary(g => g.Key, g => g.Select(Map).ToList());
        var mismatches = new List<string>();
        foreach (var v in before.SelectMany(m => m.Mesh.VertexList))
        {
            var want = Map(v);
            if (!afterByPos.TryGetValue(Key(v), out var got) || !got.Any(g => Close(want, g)))
                mismatches.Add($"{Key(v)}: {string.Join(" ", want.Select(kv => $"{kv.Key}={kv.Value:0.00}"))}");
        }
        return mismatches.Distinct().ToList();
    }

    [Test]
    [CancelAfter(300_000)]
    public void ImportsEveryRootCollectionOfAMultiModelBlend()
    {
        var runner = BlenderRunnerTests.RealRunner();
        var dir = RmvToBlenderExportTests.OutFolder("multi");
        var blend = MakeBlend("two_models", Path.Combine(dir, "two.blend"),
            WriteRmv2(dir, RmvToBlenderExportTests.KarlBody), WriteRmv2(dir, RmvToBlenderExportTests.KarlHammer));
        var (pfs, container) = Destination();

        var outcome = new BlenderImporter(pfs, runner).Import(blend, container, "test", null, 0, false);

        Assert.That(outcome.Result.Ok, Is.True, outcome.Result.Error);
        Assert.That(outcome.AddedPackPaths, Has.Count.EqualTo(2));
        Assert.That(outcome.AddedPackPaths.Select(p => Load(pfs, p).Header.LodCount), Has.All.GreaterThan(1));
    }

    [Test]
    [CancelAfter(300_000)]
    public void ImportsPlainBlendAsOneLodWithWarning()
    {
        var runner = BlenderRunnerTests.RealRunner();
        var blend = MakeBlend("cube", Path.Combine(RmvToBlenderExportTests.OutFolder("cube"), "cube.blend"));
        var (pfs, container) = Destination();

        var outcome = new BlenderImporter(pfs, runner).Import(blend, container, "test", null, 0, false);

        Assert.That(outcome.Result.Ok, Is.True, outcome.Result.Error);
        Assert.That(outcome.AddedPackPaths, Is.EqualTo(new[] { @"test\cube.rigid_model_v2" }));
        Assert.That(Load(pfs, outcome.AddedPackPaths[0]).Header.LodCount, Is.EqualTo(1));
        Assert.That(outcome.Result.Warnings, Has.Some.Contains("no RMV2 root collection"));
    }

    [Test]
    [CancelAfter(300_000)]
    public void LowercasesAndSurvivesSpacesInPaths()
    {
        var runner = BlenderRunnerTests.RealRunner();
        var dir = Directory.CreateDirectory(Path.Combine(RmvToBlenderExportTests.OutFolder("spaces"), "My Models")).FullName;
        var blend = MakeBlend("renamed", Path.Combine(dir, "Karl Body.blend"), WriteRmv2(dir, RmvToBlenderExportTests.KarlHammer), name: "Karl Body");
        var (pfs, container) = Destination();

        var outcome = new BlenderImporter(pfs, runner).Import(blend, container, "test", null, 0, false);

        Assert.That(outcome.Result.Ok, Is.True, outcome.Result.Error);
        Assert.That(outcome.AddedPackPaths, Is.EqualTo(new[] { @"test\karl body.rigid_model_v2" }));
    }

    sealed class GarbageRunner(string file) : BlenderRunner(new ApplicationSettingsService(GameTypeEnum.Warhammer3))
    {
        public override BlenderResult Run(BlenderJob job, string jobFolder) =>
            new(true, null, [new BlenderOutput(file, "broken.rigid_model_v2", 1, null)], []);
    }

    [Test]
    public void RefusesAFileThatDoesNotParse()
    {
        var garbage = Path.Combine(RmvToBlenderExportTests.OutFolder("garbage"), "broken.rigid_model_v2");
        File.WriteAllBytes(garbage, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        var (pfs, container) = Destination();

        var outcome = new BlenderImporter(pfs, new GarbageRunner(garbage)).Import("in.blend", container, "test", null, 0, false);

        Assert.That(outcome.AddedPackPaths, Is.Empty);
        Assert.That(outcome.Result.Ok, Is.False);
        Assert.That(outcome.Result.Error, Does.Contain("not a valid rigid_model_v2"));
        Assert.That(pfs.FindFile(@"test\broken.rigid_model_v2"), Is.Null);
    }
}
