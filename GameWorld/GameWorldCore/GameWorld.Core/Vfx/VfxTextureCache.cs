using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Vfx
{
    /// <summary>
    /// The textures of every effect in one editor, each loaded once by path and kept until the editor closes.
    /// A SuperView rebuild (every click in its tag list) makes new render items; without this each one decoded
    /// its textures again, about half a second for the firebox's three (measured 2026-10-11). Render items
    /// never dispose what this holds.
    /// </summary>
    // ponytail: keyed by path alone, since the editor shares one graphics device; key by device too if it ever does not.
    public sealed class VfxTextureCache : IDisposable
    {
        private readonly Func<GraphicsDevice, string, Texture2D?> _load;
        private readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.OrdinalIgnoreCase);

        public VfxTextureCache(Func<GraphicsDevice, string, Texture2D?> load) => _load = load;

        /// <summary>The texture at a path, or null when it did not load (the loader logs why).</summary>
        public Texture2D? Load(GraphicsDevice device, string path)
        {
            if (!_textures.TryGetValue(path, out var texture))
                _textures[path] = texture = _load(device, path);
            return texture;
        }

        public void Dispose()
        {
            foreach (var texture in _textures.Values)
                texture?.Dispose();
            _textures.Clear();
        }
    }
}
