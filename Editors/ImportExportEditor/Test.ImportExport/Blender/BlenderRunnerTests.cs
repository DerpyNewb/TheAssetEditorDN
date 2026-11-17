using System.Diagnostics;
using System.IO;
using Editors.ImportExport.Blender;
using Shared.Core.Settings;

namespace Test.ImportExport.Blender;

public class BlenderRunnerTests
{
    internal static BlenderRunner RealRunner(TimeSpan? timeout = null)
    {
        var runner = new BlenderRunner(new ApplicationSettingsService(GameTypeEnum.Warhammer3));
        if (runner.FindBlender() == null)
            Assert.Ignore("Blender is not installed on this machine.");
        if (timeout != null)
            runner.Timeout = timeout.Value;
        return runner;
    }

    static string TempScript(string body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ae_test_{Guid.NewGuid():N}.py");
        File.WriteAllText(path, body);
        return path;
    }

    static BlenderJob DummyJob => new("rmv2_to_blend", "in.rigid_model_v2", "out.blend");

    [Test]
    public void FindsBlenderFromSettingsFirst()
    {
        var dir = Directory.CreateTempSubdirectory("ae_blender_");
        var fake = Path.Combine(dir.FullName, "blender.exe");
        File.WriteAllText(fake, "");
        var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
        settings.CurrentSettings.BlenderPath = fake;

        Assert.That(new BlenderRunner(settings).FindBlender(), Is.EqualTo(fake));
    }

    [Test]
    public void PrefersTheBlenderThatHasTheAddonOverANewerOne()
    {
        var candidates = new[] { (@"C:\steam\blender.exe", "5.3"), (@"C:\pf\Blender 5.2\blender.exe", "5.2") };

        var chosen = BlenderRunner.ChooseBlender(candidates, version => version == "5.2");

        Assert.That(chosen, Is.EqualTo(@"C:\pf\Blender 5.2\blender.exe"));
    }

    [Test]
    public void FallsBackToTheFirstBlenderWhenNoneHasTheAddon()
    {
        var candidates = new[] { (@"C:\steam\blender.exe", "5.3"), (@"C:\pf\Blender 5.2\blender.exe", "5.2") };

        Assert.That(BlenderRunner.ChooseBlender(candidates, _ => false), Is.EqualTo(@"C:\steam\blender.exe"));
    }

    [Test]
    [CancelAfter(60_000)]
    public void TimeoutKillsBlenderAndFails()
    {
        var runner = RealRunner(TimeSpan.FromSeconds(3));
        runner.ScriptPath = TempScript("import time\ntime.sleep(60)\n");
        var before = Process.GetProcessesByName("blender").Select(p => p.Id).ToHashSet();

        var clock = Stopwatch.StartNew();
        var result = runner.Run(DummyJob, BlenderRunner.NewJobFolder("timeout"));

        Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)), "Run waited for Blender instead of stopping it");
        Assert.That(result.Ok, Is.False);
        Assert.That(result.Error, Does.Contain("timed out"));
        var leftover = Process.GetProcessesByName("blender").Where(p => !before.Contains(p.Id)).ToList();
        Assert.That(leftover, Is.Empty, "the timed-out Blender is still running");
    }

    [Test]
    [CancelAfter(120_000)]
    public void MissingAddonGivesInstallMessage()
    {
        var runner = RealRunner();
        // an empty user resources folder: Blender loads no user extensions, so the add-on is really absent
        runner.ExtraEnvironment["BLENDER_USER_RESOURCES"] = Directory.CreateTempSubdirectory("ae_noaddon_").FullName;

        var result = runner.Run(DummyJob, BlenderRunner.NewJobFolder("noaddon"));

        Assert.That(result.Ok, Is.False);
        Assert.That(result.Error, Is.EqualTo(BlenderRunner.MissingAddonMessage));
        Assert.That(BlenderRunner.MissingAddonMessage, Is.EqualTo(
            "Install ChaosRobie's io_scene_rmv2 extension in Blender: https://github.com/robert-d-schultz/rmv2_anim_blender_import_export"));
    }

    [Test]
    [CancelAfter(120_000)]
    public void MissingResultFileReportsStderr()
    {
        var runner = RealRunner();
        runner.ScriptPath = TempScript("import sys\nsys.stderr.write('boom\\n')\nsys.exit(3)\n");

        var result = runner.Run(DummyJob, BlenderRunner.NewJobFolder("stderr"));

        Assert.That(result.Ok, Is.False);
        Assert.That(result.Error, Does.Contain("boom"));
    }

    [Test]
    public void AnExeThatCannotStartIsAFailedResultNotAnException()
    {
        var dir = Directory.CreateTempSubdirectory("ae_blender_");
        var fake = Path.Combine(dir.FullName, "blender.exe");
        File.WriteAllText(fake, "not a program");
        var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
        settings.CurrentSettings.BlenderPath = fake;

        var result = new BlenderRunner(settings).Run(DummyJob, BlenderRunner.NewJobFolder("badexe"));

        Assert.That(result.Ok, Is.False);
        Assert.That(result.Error, Does.Contain(fake));
    }

    [Test]
    [CancelAfter(120_000)]
    public void ABrokenResultFileIsAFailedResultNotAnException()
    {
        var runner = RealRunner();
        runner.ScriptPath = TempScript("import sys, pathlib\np = pathlib.Path(sys.argv[sys.argv.index('--') + 1]).parent / 'result.json'\np.write_text('{\"ok\": tr')\n");

        var result = runner.Run(DummyJob, BlenderRunner.NewJobFolder("badjson"));

        Assert.That(result.Ok, Is.False);
        Assert.That(result.Error, Does.Contain("result"));
    }

    [Test]
    [CancelAfter(120_000)]
    public void BlenderWarningsOnStdoutReachTheResult()
    {
        var runner = RealRunner();
        runner.ScriptPath = TempScript(
            "import sys, json, pathlib\nprint('Warning: 12 vertices had more than 4 weights')\n" +
            "p = pathlib.Path(sys.argv[sys.argv.index('--') + 1]).parent / 'result.json'\n" +
            "p.write_text(json.dumps({'ok': True, 'files': [], 'warnings': ['from the script']}))\n");

        var result = runner.Run(DummyJob, BlenderRunner.NewJobFolder("warn"));

        Assert.That(result.Ok, Is.True, result.Error);
        Assert.That(result.Warnings, Is.EquivalentTo(new[] { "from the script", "12 vertices had more than 4 weights" }));
    }

    [Test]
    public void TheReportNamesEachFileWithItsLodCount()
    {
        var result = new BlenderResult(true, null, [new BlenderOutput("x", "karl body.rigid_model_v2", 4, null)], ["a warning"]);

        var list = BlenderResultReport.Build("Import from Blender", result);

        Assert.That(list.Errors.Select(e => (e.ErrorType, e.Description)), Is.EqualTo(new[]
        {
            ("Ok", "karl body.rigid_model_v2: 4 LODs"),
            ("Warning", "a warning"),
        }));
    }

    static int JobFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "AssetEditor", "BlenderJobs");
        return Directory.Exists(root) ? Directory.GetDirectories(root).Length : 0;
    }

    [Test]
    [CancelAfter(180_000)]
    public void ASuccessfulExportLeavesNoJobFolderBehind()
    {
        var runner = RealRunner();
        var pfs = RmvToBlenderExportTests.KarlPack();
        var input = pfs.FindFile(RmvToBlenderExportTests.KarlHammer)!;
        var before = JobFolders();

        var result = new Editors.ImportExport.Exporting.Exporters.RmvToBlender.RmvToBlenderExporter(pfs, runner)
            .Export(input, Path.Combine(RmvToBlenderExportTests.OutFolder("clean"), "hammer.blend"));

        Assert.That(result.Ok, Is.True, result.Error);
        Assert.That(JobFolders(), Is.EqualTo(before));
    }

    [Test]
    [CancelAfter(180_000)]
    public void ASuccessfulImportLeavesNoJobFolderBehind()
    {
        var runner = RealRunner();
        var pfs = RmvToBlenderExportTests.KarlPack();
        var blend = Path.Combine(RmvToBlenderExportTests.OutFolder("clean_in"), "hammer.blend");
        Assert.That(new Editors.ImportExport.Exporting.Exporters.RmvToBlender.RmvToBlenderExporter(pfs, runner)
            .Export(pfs.FindFile(RmvToBlenderExportTests.KarlHammer)!, blend).Ok, Is.True);
        var dest = new Shared.Core.PackFiles.PackFileService(null);
        var container = dest.CreateNewPackFileContainer("dest", PackFileVersion.PFH5, Shared.Core.PackFiles.Models.PackFileCAType.MOD);
        var before = JobFolders();

        var outcome = new Editors.ImportExport.Importing.Importers.BlenderToRmv.BlenderImporter(dest, runner).Import(blend, container, "test", null, 0, false);

        Assert.That(outcome.Result.Ok, Is.True, outcome.Result.Error);
        Assert.That(JobFolders(), Is.EqualTo(before));
    }
}
