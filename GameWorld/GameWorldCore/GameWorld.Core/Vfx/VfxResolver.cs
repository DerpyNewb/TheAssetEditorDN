using System.Xml;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Vfx
{
    /// <summary>An enabled emitter of an effect, with what every reference on the way to it said.</summary>
    public record ResolvedEmitter(VfxEmitter Emitter, VfxBehaviourChain? Chain, string? SceneViewType, string File,
        Matrix ReferenceTransform, float StartOffset, float SpeedMultiplier, bool InfiniteLife, VfxEmitterOverride Override);

    public record VfxResolveResult(bool Found, List<ResolvedEmitter> Emitters, List<string> Problems);

    /// <summary>
    /// Loads vfx/&lt;name&gt;.xml through the pack file service (so project packs and folders count the
    /// same as CA's), follows its references with a cycle guard and applies their overrides; an outer
    /// reference's override wins. Range values (scale, start time) take one draw each from the random.
    /// </summary>
    public class VfxResolver
    {
        private readonly IPackFileService _packFileService;

        public VfxResolver(IPackFileService packFileService) => _packFileService = packFileService;

        public static string PathOf(string vfxName)
        {
            var name = vfxName.Trim();
            return "vfx/" + (name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? name : name + ".xml");
        }

        public VfxResolveResult Resolve(string vfxName, Random random)
        {
            var result = new VfxResolveResult(false, [], []);
            var top = Load(vfxName, result.Problems);
            if (top == null)
                return result;

            Collect(vfxName.Trim(), top, Matrix.Identity, 0, 1, false, new Dictionary<string, VfxEmitterOverride>(), [Key(vfxName)], random, result);
            return result with { Found = true };
        }

        static string Key(string vfxName) => PathOf(vfxName).ToLowerInvariant();

        VfxFile? Load(string vfxName, List<string> problems)
        {
            var file = _packFileService.FindFile(PathOf(vfxName));
            if (file == null)
                return null;
            try
            {
                return VfxFile.Parse(file.DataSource.ReadData());
            }
            catch (XmlException e)
            {
                problems.Add($"{PathOf(vfxName)} did not parse: {e.Message}");
                return null;
            }
            catch (Exception e)   // a pack rebuilt or moved on disk while it is loaded; nothing throws into the viewport
            {
                problems.Add($"{PathOf(vfxName)} could not be read: {e.Message}");
                return null;
            }
        }

        void Collect(string name, VfxFile file, Matrix transform, float startOffset, float speed, bool infinite,
            IReadOnlyDictionary<string, VfxEmitterOverride> outer, HashSet<string> stack, Random random, VfxResolveResult result)
        {
            foreach (var emitter in file.Emitters)
            {
                var o = outer.GetValueOrDefault(emitter.Id) ?? VfxEmitterOverride.None;
                var switchedOn = !emitter.Properties.TryGetValue("Enable", out var enable) || enable.IsTrue;
                if (!o.Enable || !switchedOn)
                    continue;
                file.Chains.TryGetValue(emitter.Id, out var chain);
                file.SceneViewTypes.TryGetValue(emitter.Id, out var sceneView);
                result.Emitters.Add(new ResolvedEmitter(emitter, chain, sceneView, name, transform, startOffset, speed, infinite, o));
            }

            foreach (var reference in file.References)
            {
                var key = Key(reference.Target);
                if (stack.Contains(key))
                {
                    result.Problems.Add($"{name}: reference cycle through {reference.Target}, skipped");
                    continue;
                }
                var target = Load(reference.Target, result.Problems);
                if (target == null)
                {
                    result.Problems.Add($"{name}: referenced effect {PathOf(reference.Target)} not found, skipped");
                    continue;
                }

                var overrides = new Dictionary<string, VfxEmitterOverride>(reference.Overrides);
                foreach (var (emitterName, o) in outer)
                    overrides[emitterName] = o;

                var scale = MathHelper.Lerp(reference.ScaleMin, reference.ScaleMax, (float)random.NextDouble());
                var start = MathHelper.Lerp(reference.StartTimeMin, reference.StartTimeMax, (float)random.NextDouble());
                var local = VfxConventions.ReferenceMatrix(scale, reference.RotationDegrees, reference.Position);

                stack.Add(key);
                Collect(reference.Target, target, local * transform, startOffset + start, speed * reference.SimulationSpeed,
                    infinite || reference.InfiniteLife, overrides, stack, random, result);
                stack.Remove(key);
            }
        }
    }
}
