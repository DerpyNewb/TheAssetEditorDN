using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.Events;
using Shared.GameFormats.RigidModel;

namespace Editors.KitbasherEditor.ChildEditors.PinTool.Commands
{
    /// <summary>Weights every vertex of each mesh 100% to one bone of the model's skeleton, as one undo step.</summary>
    public class BindMeshToBoneCommand : IAeUndoCommandCommand
    {
        List<Rmv2MeshNode> _meshes = [];
        int _boneIndex;
        string _skeletonName = "";
        List<(MeshObject Geometry, int MatrixIndex)>? _originals;

        public string HintText => "Bind meshes to bone";
        public bool IsMutation => true;

        public void Configure(IEnumerable<Rmv2MeshNode> meshes, int boneIndex, string skeletonName)
        {
            _meshes = meshes.ToList();
            _boneIndex = boneIndex;
            _skeletonName = skeletonName;
        }

        public void Execute()
        {
            if (_meshes.Count == 0)
                throw new InvalidOperationException("BindMeshToBoneCommand not configured before Execute");

            _originals = _meshes.Select(x => (x.Geometry.Clone(), x.AnimationMatrixOverride)).ToList();
            foreach (var (geometry, _) in _originals)
                geometry.RemoveGraphicsCardResources();

            foreach (var mesh in _meshes)
            {
                // A static mesh has no weights to set, so it takes the format the model's other meshes skin with.
                if (mesh.Geometry.VertexFormat == UiVertexFormat.Static)
                    mesh.Geometry.ChangeVertexType(SkinnedFormatOf(mesh), false);
                mesh.Geometry.UpdateSkeletonName(_skeletonName);

                for (var i = 0; i < mesh.Geometry.VertexCount(); i++)
                {
                    mesh.Geometry.SetVertexBlendIndex(i, new Vector4(_boneIndex, 0, 0, 0));
                    mesh.Geometry.SetVertexWeights(i, new Vector4(1, 0, 0, 0));
                }

                // MatrixIndex pins a static mesh rigidly to a bone; a skinned mesh follows its weights instead.
                mesh.AnimationMatrixOverride = -1;
                mesh.Geometry.RebuildVertexBuffer();
            }
        }

        public void Undo()
        {
            if (_originals == null)
                return;

            for (var i = 0; i < _meshes.Count; i++)
            {
                _meshes[i].Geometry.RemoveGraphicsCardResources();
                _originals[i].Geometry.EnsureGraphicsResourcesCreated();
                _meshes[i].Geometry = _originals[i].Geometry;
                _meshes[i].AnimationMatrixOverride = _originals[i].MatrixIndex;
            }
        }

        static UiVertexFormat SkinnedFormatOf(Rmv2MeshNode mesh)
        {
            var model = mesh.GetParentModel();
            if (model == null)
                return UiVertexFormat.Weighted;
            return SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(model)
                .Select(x => x.Geometry.VertexFormat)
                .FirstOrDefault(x => x is UiVertexFormat.Weighted or UiVertexFormat.Cinematic, UiVertexFormat.Weighted);
        }
    }
}
