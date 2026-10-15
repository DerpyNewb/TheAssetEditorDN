using GameWorld.Core.Commands;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Shared.Core.Events;

namespace GameWorld.Core.Commands.Vertex
{

    public class TransformVertexCommand : IAeUndoCommandCommand
    {
        List<MeshObject> _geometryList;
        public Vector3 PivotPoint;
        public Matrix Transform { get; set; }
        public bool InvertWindingOrder { get; set; } = false;

        SelectionManager _selectionManager;
        ISelectionState _oldSelectionState;

        public void Configure(List<MeshObject> geometryList, Vector3 pivotPoint)
        {
            _geometryList = geometryList;
            PivotPoint = pivotPoint;
        }

        public string HintText { get => "Transform"; }
        public bool IsMutation { get => true; }


        public TransformVertexCommand(SelectionManager selectionManager)
        {
            _selectionManager = selectionManager;
        }

        public void Execute()
        {
            _oldSelectionState = _selectionManager.GetStateCopy();
            // Nothing to do, vertexes already updated
        }

        public void Undo() => Apply(inverse: true);

        // The drag again, as the gizmo made it: the same weighted transform per vertex that Undo takes off.
        public void Redo() => Apply(inverse: false);

        void Apply(bool inverse)
        {
            Transform.Decompose(out var scale, out var rot, out var trans);

            for (var meshIndex = 0; meshIndex < _geometryList.Count; meshIndex++)
            {
                var geo = _geometryList[meshIndex];
                if (_oldSelectionState.Mode == GeometrySelectionMode.Vertex)
                {
                    var vState = _oldSelectionState as VertexSelectionState;
                    for (var vertIndex = 0; vertIndex < vState.VertexWeights.Count; vertIndex++)
                    {
                        if (vState.VertexWeights[vertIndex] != 0)
                        {
                            var weight = vState.VertexWeights[vertIndex];
                            var vertexScale = Vector3.Lerp(Vector3.One, scale, weight);
                            var vertRot = Quaternion.Slerp(Quaternion.Identity, rot, weight);
                            var vertTrnas = trans * weight;

                            var weightedUndoTransform = Matrix.CreateScale(vertexScale) * Matrix.CreateFromQuaternion(vertRot) * Matrix.CreateTranslation(vertTrnas);
                            var finalMatrix = Matrix.CreateTranslation(-PivotPoint) * (inverse ? Matrix.Invert(weightedUndoTransform) : weightedUndoTransform) * Matrix.CreateTranslation(PivotPoint);

                            geo.TransformVertex(vertIndex, finalMatrix);
                        }
                    }
                }
                else
                {
                    var undoMatrix = Matrix.CreateTranslation(-PivotPoint) * (inverse ? Matrix.Invert(Transform) : Transform) * Matrix.CreateTranslation(PivotPoint);
                    for (var v = 0; v < geo.VertexCount(); v++)
                        geo.TransformVertex(v, undoMatrix);

                    if (InvertWindingOrder)
                    {
                        var indexes = geo.GetIndexBuffer();
                        for (var index = 0; index < indexes.Count; index += 3)
                        {
                            var temp = indexes[index + 2];
                            indexes[index + 2] = indexes[index + 0];
                            indexes[index + 0] = temp;
                        }
                        geo.SetIndexBuffer(indexes);
                    }
                }

                geo.RebuildVertexBuffer();
            }

            _selectionManager.SetState(_oldSelectionState);
        }

    }
}
