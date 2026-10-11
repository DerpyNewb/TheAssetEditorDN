using GameWorld.Core.Components;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles;

namespace Editors.VfxEditor.Preview
{
    /// <summary>
    /// Plays the open effect at the origin (spec section 7). Show replaces what plays: the effect resolved at a tier, with
    /// the editor's unsaved bytes standing in for its file and its references read from the packs. Lines holds what could
    /// not be found and every emitter left out and why, including what the texture loader and the simulator report later.
    /// Nothing throws into the viewport.
    /// </summary>
    public sealed class VfxPreviewComponent : BaseComponent, IDisposable
    {
        const int Seed = 1;
        readonly IPackFileService _packFileService;
        readonly SceneManager _sceneManager;
        readonly VfxTextureCache _textures;
        readonly List<string> _lines = [];
        ParticleSimulator? _simulator;
        VfxEffectNode? _node;

        public VfxPreviewComponent(IPackFileService packFileService, SceneManager sceneManager)
        {
            _packFileService = packFileService;
            _sceneManager = sceneManager;
            _textures = new VfxTextureCache(ParticleRenderItem.PackTextureLoader(packFileService, _lines.Add));
        }

        public float Time { get; private set; }
        public bool IsPlaying { get; set; } = true;
        public bool Loop { get; set; } = true;
        public float LoopLength { get; set; } = 4;
        public float Speed { get; set; } = 1;
        /// <summary>Fly the effect like a projectile (ProjectileFlight), once per loop; read when Show builds the simulation.</summary>
        public bool Projectile { get; set; }
        public float ProjectileSpeed { get; set; } = 60;
        public int ProjectileAxis { get; set; }
        public IReadOnlyList<string> Lines => _lines;
        public event Action? TimeChanged;

        public void Show(string effectName, byte[] unsaved, int tier)
        {
            Clear();
            _lines.Clear();
            var resolver = new VfxResolver(_packFileService, new Dictionary<string, byte[]> { [VfxResolver.Key(effectName)] = unsaved }, tier);
            var resolved = resolver.Resolve(effectName, new Random(Seed));
            _lines.AddRange(resolved.Problems);
            var settings = resolved.Emitters.Select(EmitterSettings.From).ToList();
            foreach (var emitter in settings)
            {
                _lines.AddRange(emitter.Skipped.Select(gap => $"{emitter.Name}: {gap}"));
                if (emitter.Texture is { } texture && _packFileService.FindFile(texture) == null)
                    _lines.Add($"{emitter.Name}: texture {texture} is not in the loaded packs");
            }
            if (settings.Count == 0)
                _lines.Add("no emitter is switched on at this tier");
            Func<float, Matrix> effectWorldAt = Projectile
                ? ProjectileFlight.WorldAt(ProjectileFlight.Axes[ProjectileAxis].Axis, ProjectileSpeed, LoopLength)
                : _ => Matrix.Identity;
            _simulator = new ParticleSimulator(settings, Seed, effectWorldAt, log: _lines.Add);
            _node = _sceneManager.RootNode.AddObject(new VfxEffectNode("VfxPreview", new ParticleRenderItem(_simulator, _textures)));
            SetTime(Time);
        }

        public override void Update(GameTime gameTime)
        {
            if (_simulator == null || !IsPlaying)
                return;
            var next = Time + (float)gameTime.ElapsedGameTime.TotalSeconds * Speed;
            SetTime(Loop && next > LoopLength ? 0 : next);
        }

        public void SetTime(float time)
        {
            Time = Math.Max(0, time);
            try
            {
                _simulator?.AdvanceTo(Time);
            }
            catch (Exception e)
            {
                _lines.Add($"the preview stopped: {e.Message}");
                _simulator = null;
            }
            TimeChanged?.Invoke();
        }

        void Clear()
        {
            _simulator = null;
            if (_node == null)
                return;
            _node.Parent?.RemoveObject(_node);
            _node.RenderItem.Dispose();
            _node = null;
        }

        public void Dispose()
        {
            Clear();
            _textures.Dispose();
        }
    }
}
