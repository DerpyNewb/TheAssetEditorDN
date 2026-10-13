using GameWorld.Core.Components;
using Microsoft.Xna.Framework;

namespace Editors.UnitSimulator.Personalities
{
    /// <summary>Runs PersonalityList.PlaceAll each frame between the animation players, which pose the mount's skeleton, and the scene
    /// objects, which copy their Offset to their nodes: an update order of its own, since the game sorts its updates with an unstable
    /// sort and two components of one order can run either way round.</summary>
    public class PersonalityPlacement : BaseComponent
    {
        readonly PersonalityList _list;

        public PersonalityPlacement(PersonalityList list)
        {
            _list = list;
            UpdateOrder = (int)ComponentUpdateOrderEnum.Animation + 1;
        }

        public override void Update(GameTime gameTime) => _list.PlaceAll();
    }
}
