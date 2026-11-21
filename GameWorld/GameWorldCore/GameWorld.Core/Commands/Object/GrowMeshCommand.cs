using GameWorld.Core.Animation;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Shared.Core.Events;

namespace GameWorld.Core.Commands.Object
{
    // The BMI tool: pushes each vertex out along its normal by factor x the weight it gives the chosen bones.
    public class GrowMeshCommand : IAeUndoCommandCommand
    {
        // Vertices closer than this share one normal, so a UV seam or hard edge moves as one and does not tear open.
        const float SharedPositionDistance = 0.01f;

        Rmv2MeshNode _node;
        float _factor;
        HashSet<int> _bonesAffectedScale;
        GameSkeleton _gameSkeleton;

        MeshObject _backup;

        public void Configure(GameSkeleton skeleton, Rmv2MeshNode node, float factor, List<int> bonesAffectedScale)
        {
            _node = node;
            _factor = factor;
            _gameSkeleton = skeleton;
            _bonesAffectedScale = bonesAffectedScale.ToHashSet();
        }


        public string HintText { get => "Grow mesh"; }
        public bool IsMutation { get => true; }

        public void Execute()
        {
            _backup = _node.Geometry.Clone();

            var mesh = _node.Geometry;
            var vertices = mesh.VertexArray;
            var normals = SharedNormals(vertices);

            for (var i = 0; i < vertices.Length; i++)
            {
                var bones = vertices[i].GetBoneIndexs();
                var weights = vertices[i].GetBoneWeights();

                float boneWeight = 0;
                for (var boneIndex = 0; boneIndex < bones.Length; boneIndex++)
                {
                    if (_bonesAffectedScale.Contains(bones[boneIndex]))
                        boneWeight += weights[boneIndex];
                }

                if (boneWeight != 0)
                    mesh.TransformVertex(i, Matrix.CreateTranslation(normals[i] * _factor * boneWeight));
            }

            mesh.RebuildVertexBuffer();
        }

        // Every vertex's normal summed with those of the vertices within SharedPositionDistance of it. A grid of cells that
        // size finds them, as the neighbours can only be in the 27 cells around a vertex's own; comparing every pair took
        // minutes on a 20,000-vertex mesh.
        static Vector3[] SharedNormals(VertexPositionNormalTextureCustom[] vertices)
        {
            static (int, int, int) Cell(Vector3 p) => ((int)MathF.Floor(p.X / SharedPositionDistance), (int)MathF.Floor(p.Y / SharedPositionDistance), (int)MathF.Floor(p.Z / SharedPositionDistance));

            var cells = new Dictionary<(int, int, int), List<int>>();
            for (var i = 0; i < vertices.Length; i++)
            {
                var cell = Cell(vertices[i].Position3());
                if (!cells.TryGetValue(cell, out var list))
                    cells[cell] = list = [];
                list.Add(i);
            }

            var maxDistanceSquared = SharedPositionDistance * SharedPositionDistance;
            var normals = new Vector3[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                var position = vertices[i].Position3();
                var (x, y, z) = Cell(position);
                var sum = Vector3.Zero;
                for (var dx = -1; dx <= 1; dx++)
                    for (var dy = -1; dy <= 1; dy++)
                        for (var dz = -1; dz <= 1; dz++)
                        {
                            if (!cells.TryGetValue((x + dx, y + dy, z + dz), out var list))
                                continue;
                            foreach (var j in list)
                            {
                                if ((vertices[j].Position3() - position).LengthSquared() < maxDistanceSquared)
                                    sum += vertices[j].Normal;
                            }
                        }

                normals[i] = Vector3.Normalize(sum);
            }

            return normals;
        }

        public void Undo()
        {
            _node.Geometry = _backup;
        }
    }
}
