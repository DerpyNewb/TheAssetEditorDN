using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services.SceneSaving.Lod.MeshOptimizerIntegration;
using Shared.Core.ErrorHandling;

namespace GameWorld.Core.Services.SceneSaving.Lod.Strategies
{
    public class MeshOptimizerLodGeneration : LodGeneratorBase, ILodGenerationStrategy
    {
        public LodStrategy StrategyId => LodStrategy.MeshOptimizer;
        public string Name => "Default";
        public string Description => "meshoptimizer: keeps UV seams, open edges, skin weights and tangents";
        public bool IsAvailable => true;

        public MeshOptimizerLodGeneration(IScopedLogger scopedLogger) : base(scopedLogger)
        {
        }

        public void Generate(MainEditableNode mainNode, List<LodGenerationSettings> settings)
        {
            CreateLodsForRootNode(mainNode, settings);
        }

        protected override void ReduceMesh(Rmv2MeshNode rmv2MeshNode, float deductionRatio)
        {
            var reducedMesh = MeshOptimizerSimplifier.GetReducedMeshCopy(rmv2MeshNode.Geometry, deductionRatio);
            rmv2MeshNode.Geometry.RemoveGraphicsCardResources();
            rmv2MeshNode.Geometry = reducedMesh;
        }
    }
}
