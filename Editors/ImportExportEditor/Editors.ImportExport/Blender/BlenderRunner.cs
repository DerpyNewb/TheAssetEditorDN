using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shared.Core.ErrorHandling;
using Shared.Core.Settings;

namespace Editors.ImportExport.Blender
{
    /// <summary>
    /// Runs Blender headless on ae_convert.py, which drives ChaosRobie's io_scene_rmv2 add-on
    /// (GPL-3.0; it is only called by operator name, never copied).
    /// </summary>
    public class BlenderRunner
    {
        public const string MissingAddonMessage =
            "Install ChaosRobie's io_scene_rmv2 extension in Blender: https://github.com/robert-d-schultz/rmv2_anim_blender_import_export";

        static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

        readonly ILogger _logger = Logging.Create<BlenderRunner>();
        readonly ApplicationSettingsService _settings;

        public BlenderRunner(ApplicationSettingsService settings) => _settings = settings;

        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
        public string ScriptPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "Resources", "Blender", "ae_convert.py");
        public IDictionary<string, string> ExtraEnvironment { get; } = new Dictionary<string, string>();

        public static string NewJobFolder(string stem)
        {
            var dir = Path.Combine(Path.GetTempPath(), "AssetEditor", "BlenderJobs", $"{stem}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        string? _found;

        /// <summary>
        /// The configured path, else the installed Blender whose user config holds io_scene_rmv2.
        /// The newest install is not good enough: Steam updates Blender to a version whose config
        /// folder is empty (5.3 beside a 5.2 that has the add-on, measured 2026-11-17).
        /// </summary>
        public string? FindBlender()
        {
            var configured = _settings.CurrentSettings.BlenderPath;
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                return configured;
            if (_found != null)
                return _found;

            var candidates = new List<(string Exe, string Version)>();
            foreach (var library in SteamLibraries())
            {
                var exe = Path.Combine(library, "steamapps", "common", "Blender", "blender.exe");
                if (File.Exists(exe))
                    candidates.Add((exe, ReadVersion(exe)));
            }
            var foundation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Blender Foundation");
            if (Directory.Exists(foundation))
            {
                foreach (var dir in Directory.GetDirectories(foundation, "Blender*").OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    var exe = Path.Combine(dir, "blender.exe");
                    if (File.Exists(exe))
                        candidates.Add((exe, ReadVersion(exe)));
                }
            }

            _found = ChooseBlender(candidates, AddonInstalledFor);
            return _found;
        }

        public static string? ChooseBlender(IEnumerable<(string Exe, string Version)> candidates, Func<string, bool> addonInstalledFor)
        {
            var list = candidates.ToList();
            return list.FirstOrDefault(c => addonInstalledFor(c.Version)).Exe ?? list.FirstOrDefault().Exe;
        }

        static bool AddonInstalledFor(string majorMinor)
        {
            var config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Blender Foundation", "Blender", majorMinor);
            if (Directory.Exists(Path.Combine(config, "scripts", "addons", "io_scene_rmv2")))
                return true;
            var extensions = Path.Combine(config, "extensions");
            return Directory.Exists(extensions) && Directory.GetDirectories(extensions).Any(repo => Directory.Exists(Path.Combine(repo, "io_scene_rmv2")));
        }

        /// <summary>"5.2" from "Blender 5.2.3 LTS"; empty when it cannot be read.</summary>
        static string ReadVersion(string exe)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, "--version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using var p = Process.Start(psi)!;
                var text = p.StandardOutput.ReadToEnd();
                p.WaitForExit(15000);
                var m = Regex.Match(text, @"Blender (\d+\.\d+)");
                return m.Success ? m.Groups[1].Value : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        static IEnumerable<string> SteamLibraries()
        {
            var steam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            yield return steam;
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
                yield break;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                yield return m.Groups[1].Value.Replace(@"\\", @"\");
        }

        public BlenderResult Run(BlenderJob job, string jobFolder)
        {
            var blender = FindBlender();
            if (blender == null)
                return BlenderResult.Fail("Blender was not found. Set its path in Settings (Blender path).");

            var jobFile = Path.Combine(jobFolder, "job.json");
            var resultFile = Path.Combine(jobFolder, "result.json");
            File.WriteAllText(jobFile, JsonSerializer.Serialize(job, JsonOptions));
            File.Delete(resultFile);

            var psi = new ProcessStartInfo(blender)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in new[] { "--background", "--python", ScriptPath, "--", jobFile })
                psi.ArgumentList.Add(arg);
            foreach (var (key, value) in ExtraEnvironment)
                psi.Environment[key] = value;

            var stderr = new Queue<string>();
            using var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) _logger.Here().Information($"[blender] {e.Data}"); };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    return;
                _logger.Here().Warning($"[blender] {e.Data}");
                lock (stderr)
                {
                    stderr.Enqueue(e.Data);
                    if (stderr.Count > 20)
                        stderr.Dequeue();
                }
            };

            _logger.Here().Information($"Running Blender job {job.Mode}: {jobFile}");
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(Timeout))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return BlenderResult.Fail($"Blender timed out after {Timeout.TotalSeconds:0} s and was stopped.");
            }
            process.WaitForExit(); // flush the async readers

            if (!File.Exists(resultFile))
            {
                string tail;
                lock (stderr)
                    tail = string.Join(Environment.NewLine, stderr);
                return BlenderResult.Fail($"Blender exited with code {process.ExitCode} and wrote no result.{Environment.NewLine}{tail}");
            }

            var result = JsonSerializer.Deserialize<BlenderResult>(File.ReadAllText(resultFile), JsonOptions);
            return result ?? BlenderResult.Fail("Blender wrote an empty result.");
        }
    }
}
