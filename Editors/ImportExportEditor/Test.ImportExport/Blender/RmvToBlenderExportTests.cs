using System.IO;
using Editors.ImportExport.Blender;
using Editors.ImportExport.Exporting.Exporters.RmvToBlender;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel;
using Shared.TestUtility;
using Test.TestingUtility.TestUtility;

namespace Test.ImportExport.Blender;

public class RmvToBlenderExportTests
{
    internal const string KarlBody = @"variantmeshes\wh_variantmodels\hu1\emp\emp_karl_franz\emp_karl_franz.rigid_model_v2";
    internal const string KarlHammer = @"variantmeshes\wh_variantmodels\hu1\emp\emp_props\emp_karl_franz_hammer_2h_01.rigid_model_v2";

    internal static IPackFileService KarlPack() =>
        PackFileSerivceTestHelper.CreateFromPackFile(PathHelper.GetDataFile("Karl_and_celestialgeneral.pack"));

    internal static string OutFolder(string name) => Directory.CreateTempSubdirectory($"ae_{name}_").FullName;

    internal static uint LodCount(byte[] rmv2) => ModelFactory.Create().LoadOnlyHeaders(rmv2).Header.LodCount;

    [Test]
    [CancelAfter(180_000)]
    public void ExportsKarlBodyWithAllLodsAndSkeleton()
    {
        var runner = BlenderRunnerTests.RealRunner();
        var pfs = KarlPack();
        var input = pfs.FindFile(KarlBody)!;
        var output = Path.Combine(OutFolder("karl"), "emp_karl_franz.blend");

        var result = new RmvToBlenderExporter(pfs, runner).Export(input, output);

        Assert.That(result.Ok, Is.True, result.Error);
        Assert.That(File.Exists(output), Is.True);
        Assert.That(result.Files[0].Lods, Is.EqualTo(LodCount(input.DataSource.ReadData())));
        Assert.That(result.Files[0].PackPath, Is.EqualTo(pfs.GetFullPath(input)));
        Assert.That(result.Warnings, Has.None.Contains("skeleton"));
        var skeleton = Shared.GameFormats.Animation.AnimationFile.Create(pfs.FindFile(@"animations\skeletons\humanoid01.anim")!);
        Assert.That(result.Files[0].Bones, Is.EqualTo(skeleton.Bones.Length), "the .blend's armature does not carry the skeleton");
    }

    [Test]
    [CancelAfter(180_000)]
    public void ExportsStaticHammerWithNoSkeleton()
    {
        var runner = BlenderRunnerTests.RealRunner();
        var pfs = KarlPack();
        var input = pfs.FindFile(KarlHammer)!;
        var output = Path.Combine(OutFolder("hammer"), "hammer.blend");

        var result = new RmvToBlenderExporter(pfs, runner).Export(input, output);

        Assert.That(result.Ok, Is.True, result.Error);
        Assert.That(File.Exists(output), Is.True);
        Assert.That(result.Files[0].Lods, Is.EqualTo(LodCount(input.DataSource.ReadData())));
        Assert.That(result.Warnings, Has.None.Contains("skeleton"));
        Assert.That(result.Files[0].Bones, Is.EqualTo(0));
    }

    [Test]
    [CancelAfter(180_000)]
    public void ExportWarnsOnMissingTexturesAndStillWritesBlend()
    {
        var runner = BlenderRunnerTests.RealRunner();
        var bytes = KarlPack().FindFile(KarlBody)!.DataSource.ReadData();
        var pfs = new PackFileService(null);
        var container = pfs.CreateNewPackFileContainer("bare", PackFileVersion.PFH5, PackFileCAType.MOD);
        pfs.AddFilesToPack(container, [new NewPackFileEntry(Path.GetDirectoryName(KarlBody)!, PackFile.CreateFromBytes("emp_karl_franz.rigid_model_v2", bytes))]);
        var input = pfs.FindFile(KarlBody)!;
        var output = Path.Combine(OutFolder("bare"), "karl.blend");

        var result = new RmvToBlenderExporter(pfs, runner).Export(input, output);

        Assert.That(result.Ok, Is.True, result.Error);
        Assert.That(File.Exists(output), Is.True);
        Assert.That(result.Warnings, Has.Some.Contains(".dds"));
        Assert.That(result.Warnings, Has.Some.Contains("skeleton"), "the bare pack has no humanoid01.anim either");
    }
}
