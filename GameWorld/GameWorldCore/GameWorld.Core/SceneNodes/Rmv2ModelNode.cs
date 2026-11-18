namespace GameWorld.Core.SceneNodes
{
    public class Rmv2ModelNode : GroupNode
    {
        public Rmv2ModelNode(string name)
        {
            Name = name;
        }

        public List<Rmv2LodNode> GetLodNodes()
        {
            return Children
                .Where(x => x is Rmv2LodNode)
                .Select(x => x as Rmv2LodNode)
                .Cast<Rmv2LodNode>()
                .ToList();
        }

        public List<Rmv2MeshNode> GetMeshNodes(int lod)
        {
            var lodNodes = GetLodNodes();
            var lodNode = lodNodes[lod];
            var meshes = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(lodNode);
            return meshes;
        }

        public List<Rmv2MeshNode> GetMeshesInLod(int lodIndex, bool onlyVisible)
        {
            var lods = GetLodNodes();
            var orderedLods = lods.OrderBy(x => x.LodValue);

            var meshes = orderedLods
               .ElementAt(lodIndex)
               .GetAllModels(onlyVisible);

            return meshes;
        }


        /// <summary>The .rigid_model_v2 this was loaded from, in the pack. Empty for a model built in the editor.</summary>
        public string FilePath { get; set; } = "";

        protected Rmv2ModelNode() { }

        public override ISceneNode CreateCopyInstance() => new Rmv2ModelNode();

        public override void CopyInto(ISceneNode target)
        {
            ((Rmv2ModelNode)target).FilePath = FilePath;
            base.CopyInto(target);
        }
    }
}


