using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.Events;

namespace GameWorld.Core.Commands.Object
{
    // Runs an edit on a copy of each mesh's geometry and puts the copy in place, as one undo step. Undo hands back the
    // geometry and pivot each mesh had, so the data comes back exactly as it was.
    public class EditMeshesCommand : IAeUndoCommandCommand
    {
        List<Rmv2MeshNode> _meshes = [];
        Action<Rmv2MeshNode> _edit = _ => { };
        List<(MeshObject Geometry, Vector3 Pivot)> _before = [];

        public string HintText { get; private set; } = "Edit meshes";
        public bool IsMutation => true;

        public void Configure(List<Rmv2MeshNode> meshes, string hintText, Action<Rmv2MeshNode> edit)
        {
            _meshes = meshes;
            HintText = hintText;
            _edit = edit;
        }

        public void Execute()
        {
            _before = _meshes.Select(x => (x.Geometry, x.PivotPoint)).ToList();
            foreach (var mesh in _meshes)
            {
                mesh.Geometry = mesh.Geometry.Clone();
                _edit(mesh);
                mesh.Geometry.RebuildIndexBuffer();
                mesh.Geometry.RebuildVertexBuffer();
            }
        }

        public void Undo()
        {
            for (var i = 0; i < _meshes.Count; i++)
                (_meshes[i].Geometry, _meshes[i].PivotPoint) = _before[i];
        }
    }
}
