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
}
