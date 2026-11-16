using System.IO;
using GameWorld.Core.Vfx;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx
{
    // Scratch, never commit: how strong vanilla's DEFERRED_DYNAMIC_LIGHT emitters are, by their alpha and colour keys.
    public class LightAlphaScratch
    {
        static float Peak(VfxCurve? c, int outputs = 1)
        {
            if (c == null) return float.NaN;
            var keys = c.Outputs.Take(outputs).SelectMany(o => o.Min.Concat(o.Random ? o.Max : [])).Select(k => k.Value).ToList();
            return keys.Count == 0 ? float.NaN : keys.Max();
        }

        [Test, Explicit]
        public void Survey()
        {
            var (runner, containers) = VanillaPacks.Load("vfx_desc.pack");
            var resolver = new VfxResolver(runner.PackFileService);
            var files = containers[0].GetAllFiles().Where(x => x.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && x.Key.Count(c => c == '\\' || c == '/') == 1).OrderBy(x => x.Key).ToList();
            var rows = new List<(string Name, string Emitter, float Initial, float Curve, float Colour, float Scale)>();
            foreach (var (path, _) in files)
            {
                var name = Path.GetFileNameWithoutExtension(path);
                VfxResolveResult resolved;
                try { resolved = resolver.Resolve(name, new Random(1)); } catch { continue; }
                foreach (var e in resolved.Emitters)
                {
                    var s = EmitterSettings.From(e);
                    if (!s.Light) continue;
                    rows.Add((name, e.Emitter.Id, Peak(s.InitialAlpha), Peak(s.Alpha), Peak(s.Colour, 3), Peak(s.InitialScale)));
                }
            }
            var o = TestContext.Out;
            o.WriteLine($"{rows.Count} light emitters in {rows.Select(x => x.Name).Distinct().Count()} effects");
            float A(float x) => float.IsNaN(x) ? 1 : x;
            var strength = rows.Select(r => A(r.Initial) * A(r.Curve)).ToList();
            foreach (var (lo, hi) in new[] { (0f, 0.5f), (0.5f, 1.0001f), (1.0001f, 2f), (2f, 5f), (5f, 10f), (10f, 25f), (25f, 1e9f) })
                o.WriteLine($"  alpha peak in [{lo},{hi}): {strength.Count(x => x >= lo && x < hi)}");
            o.WriteLine($"  initial alpha missing: {rows.Count(r => float.IsNaN(r.Initial))}; alpha curve missing: {rows.Count(r => float.IsNaN(r.Curve))}");
            o.WriteLine("colour peak buckets:");
            foreach (var (lo, hi) in new[] { (0f, 1.0001f), (1.0001f, 2f), (2f, 5f), (5f, 20f), (20f, 1e9f) })
                o.WriteLine($"  [{lo},{hi}): {rows.Count(r => A(r.Colour) >= lo && A(r.Colour) < hi)}");
            o.WriteLine("strongest 40 by alpha x colour:");
            foreach (var r in rows.OrderByDescending(r => A(r.Initial) * A(r.Curve) * A(r.Colour)).Take(40))
                o.WriteLine($"  {r.Name} / {r.Emitter}: initial {r.Initial}, curve {r.Curve}, colour {r.Colour}, scale {r.Scale}");
            o.WriteLine("taurus / bale:");
            foreach (var r in rows.Where(r => r.Name.Contains("taurus") || r.Name.Contains("bale")))
                o.WriteLine($"  {r.Name} / {r.Emitter}: initial {r.Initial}, curve {r.Curve}, colour {r.Colour}, scale {r.Scale}");
        }
    }
}
