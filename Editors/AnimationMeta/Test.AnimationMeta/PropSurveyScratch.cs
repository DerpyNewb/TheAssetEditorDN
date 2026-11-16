using System.IO;
using System.Reflection;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta
{
    // Scratch, not committed: every vanilla PROP / ANIMATED_PROP tag's BoneId, AttachMethod and OverrideProp, counted.
    public class PropSurveyScratch
    {
        [Test, Explicit]
        public void Scratch_PropSurvey()
        {
            var dataFolder = Environment.GetEnvironmentVariable("WH3_DATA")!;
            var runner = new AssetEditorTestRunner();
            var loader = (IPackFileContainerLoader)runner.ServiceProvider.GetService(typeof(IPackFileContainerLoader))!;
            var parser = new MetaDataFileParser(new MetaDataDatabase());
            var counts = new Dictionary<string, (int N, string Example)>();
            void Count(string key, string example)
            {
                var (n, e) = counts.GetValueOrDefault(key, (0, example));
                counts[key] = (n + 1, e);
            }
            foreach (var packName in new[] { "anim.pack", "anim2.pack", "anim_3.pack" })
            {
                var container = loader.CreateFromPackFile(PackFileContainerType.Normal, Path.Combine(dataFolder, packName), true);
                foreach (var (path, file) in container.GetAllFiles().Where(x => x.Key.EndsWith(".meta")))
                {
                    ParsedMetadataFile parsed;
                    try { parsed = parser.ParseFile(file.DataSource.ReadData()); } catch { continue; }
                    foreach (var tag in parsed.Attributes.OfType<IAnimatedPropMeta>())
                    {
                        int Get(string name) => (int)(tag.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)?.GetValue(tag)
                            ?? tag.GetType().GetProperties().First(p => p.Name == name).GetValue(tag)!);
                        var a = (ParsedMetadataAttribute)tag;
                        var attach = Get("AttachMethod");
                        var over = Get("OverrideProp");
                        Count($"{a.Name}_{a.Version} attach={attach} override={over}", $"{path} bone={tag.BoneId} anim={tag.AnimationName} pos={tag.Position}");
                        Count($"bone={tag.BoneId} override={over}", path);
                        Count($"{a.Name} hasAnim={!string.IsNullOrEmpty(tag.AnimationName)} attach={attach}", path);
                    }
                }
            }
            foreach (var (key, v) in counts.OrderBy(x => x.Key))
                TestContext.Out.WriteLine($"SURVEY {key,-50} {v.N,6}  {v.Example}");
        }

        // META_DIR: every .meta under it, its prop tags printed.
        [Test, Explicit]
        public void Scratch_PropTagsOnDisk()
        {
            var parser = new MetaDataFileParser(new MetaDataDatabase());
            foreach (var path in Directory.EnumerateFiles(Environment.GetEnvironmentVariable("META_DIR")!, "*.meta", SearchOption.AllDirectories))
            {
                ParsedMetadataFile parsed;
                try { parsed = parser.ParseFile(File.ReadAllBytes(path)); } catch { continue; }
                foreach (var tag in parsed.Attributes.OfType<IAnimatedPropMeta>())
                {
                    int Get(string name) => (int)tag.GetType().GetProperties().First(p => p.Name == name).GetValue(tag)!;
                    TestContext.Out.WriteLine($"DISK {Path.GetFileName(path)} {((ParsedMetadataAttribute)tag).DisplayName} attach={Get("AttachMethod")} override={Get("OverrideProp")} bone={tag.BoneId} pos={tag.Position} rot={tag.Orientation} scale={tag.Scale} model={tag.ModelName} anim={tag.AnimationName}");
                }
            }
        }
    }
}
