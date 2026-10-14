using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.RenderItems;
using Microsoft.Xna.Framework;

namespace Editors.UnitSimulator.Personalities
{
    /// <summary>Runs PersonalityList.PlaceAll each frame between the animation players, which pose the mount's skeleton, and the scene
    /// objects, which copy their Offset to their nodes: an update order of its own, since the game sorts its updates with an unstable
    /// sort and two components of one order can run either way round. Draws a marker with the bone's name at each bone a part sits on.</summary>
    public class PersonalityPlacement : BaseComponent
    {
        readonly PersonalityList _list;
        readonly RenderEngineComponent? _renderEngine;

        public PersonalityPlacement(PersonalityList list, RenderEngineComponent? renderEngine = null)
        {
            _list = list;
            _renderEngine = renderEngine;
            UpdateOrder = (int)ComponentUpdateOrderEnum.Animation + 1;
        }

        public override void Update(GameTime gameTime) => _list.PlaceAll();

        // In Draw: the render engine empties its lists in its own Update.
        public override void Draw(GameTime gameTime)
        {
            if (_renderEngine == null)
                return;
            foreach (var (bone, world) in _list.Markers())
            {
                _renderEngine.AddRenderLines(LineHelper.CreateCube(Matrix.CreateScale(0.06f) * world, Color.Yellow));
                _renderEngine.AddRenderItem(RenderBuckedId.Font, new WorldTextRenderItem(_renderEngine, bone, world.Translation, Color.Yellow));
            }
        }
    }
}
