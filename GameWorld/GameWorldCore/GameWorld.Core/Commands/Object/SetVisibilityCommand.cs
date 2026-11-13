using GameWorld.Core.SceneNodes;
using Shared.Core.Events;

namespace GameWorld.Core.Commands.Object
{
    // Shows or hides scene nodes as one undo step. Undo gives each node back the visibility it had, so a node that was
    // already in the asked-for state stays as it was.
    public class SetVisibilityCommand : IAeUndoCommandCommand
    {
        List<ISceneNode> _nodes = [];
        List<(ISceneNode Node, bool WasVisible)> _before = [];
        bool _visible;

        public string HintText => _visible ? "Objects Shown" : "Objects Hidden";
        public bool IsMutation => false;

        public void Configure(List<ISceneNode> nodes, bool visible)
        {
            _nodes = nodes;
            _visible = visible;
        }

        public void Execute()
        {
            _before = _nodes.Select(x => (x, x.IsVisible)).ToList();
            foreach (var node in _nodes)
                node.IsVisible = _visible;
        }

        public void Undo()
        {
            foreach (var (node, wasVisible) in _before)
                node.IsVisible = wasVisible;
        }
    }
}
