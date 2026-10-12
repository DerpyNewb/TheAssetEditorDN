using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Rendering.Materials.Shaders;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx
{
    /// <summary>A model's LOD 0 meshes, each with its placement in the model, and the box around them.</summary>
    public record VfxModel(IReadOnlyList<(MeshObject Geometry, IShader Material, Matrix Local)> Meshes, BoundingBox Box);

    /// <summary>
    /// The models continuous_rigid particles draw, loaded once per editor through the editor's own mesh loader, which returns
    /// null for a model not in the packs or that did not load (and says so itself). Owns the geometry it loaded; the
    /// materials' shaders belong to the app.
    /// </summary>
    public sealed class VfxMeshCache : IDisposable
    {
        static readonly VfxModel s_none = new([], new BoundingBox());
        static readonly Matrix[] s_bindPose = Enumerable.Repeat(Matrix.Identity, 256).ToArray();
        readonly Func<string, SceneNode?> _load;
        readonly Dictionary<string, VfxModel> _models = [];

        public VfxMeshCache(Func<string, SceneNode?> load) => _load = load;

        static string Key(string path) => path.Replace('/', '\\').ToLowerInvariant();

        public VfxModel Get(string path)
        {
            var key = Key(path);
            if (_models.TryGetValue(key, out var known))
                return known;

            var root = _load(path);
            var models = root == null ? [] : root is Rmv2ModelNode single ? [single] : SceneNodeHelper.GetChildrenOfType<Rmv2ModelNode>(root);
            var meshes = models
                .SelectMany(model => model.GetLodNodes().OrderBy(lod => lod.LodValue).Take(1))
                .SelectMany(lod => SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(lod))
                .Select(BindPose)
                .Select(mesh => (mesh.Geometry, (IShader)mesh.Material, Matrix.CreateTranslation(mesh.PivotPoint) * mesh.ModelMatrix))
                .ToList();
            return _models[key] = meshes.Count == 0 ? s_none
                : new VfxModel(meshes, meshes.Select(x => x.Geometry.BoundingBox).Aggregate(BoundingBox.CreateMerged));
        }

        // A mesh node fills its material's bone transforms when it renders itself; drawn from here it never does, and a
        // material with none set throws. The model holds its bind pose.
        static Rmv2MeshNode BindPose(Rmv2MeshNode mesh)
        {
            if (mesh.Material.TryGetCapability<AnimationCapability>() is { } animation)
                (animation.AnimationTransforms, animation.AnimationWeightCount, animation.ApplyAnimation) = (s_bindPose, mesh.Geometry.WeightCount, false);
            return mesh;
        }

        /// <summary>A model changed in a pack: let go of it, so the next draw loads the new one.</summary>
        public void Forget(string path)
        {
            if (_models.Remove(Key(path), out var model))
                foreach (var mesh in model.Meshes)
                    mesh.Geometry.Dispose();
        }

        public void Dispose()
        {
            foreach (var mesh in _models.Values.SelectMany(x => x.Meshes))
                mesh.Geometry.Dispose();
            _models.Clear();
        }
    }
}
