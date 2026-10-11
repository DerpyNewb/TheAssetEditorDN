using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;

namespace Editors.AnimationMeta.SuperView.Visualisation.Instances
{
    // An EFFECT tag: its locator on the tag's bone, and its particles on the main animation's clock from
    // the tag's Start Time, the way AnimatedPropInstance runs props.
    public class VfxEffectInstance : IMetaDataInstance
    {
        private readonly ILogger _logger = Logging.Create<VfxEffectInstance>();
        private readonly SceneNode _locator;
        private readonly Func<float, Matrix> _boneAt;
        private readonly float _startTime;
        private readonly VfxEffectNode? _particles;
        private bool _hasError;

        // Null, so the playback controls leave this alone; Update drives it.
        public AnimationPlayer Player => null;
        public ParticleSimulator? Simulator { get; }

        public VfxEffectInstance(SceneNode locator, Func<float, Matrix> boneAt, float startTime, VfxEffectNode? particles = null, ParticleSimulator? simulator = null)
        {
            _locator = locator;
            _boneAt = boneAt;
            _startTime = startTime;
            _particles = particles;
            Simulator = simulator;
        }

        public void Update(float currentTime)
        {
            if (_hasError)
                return;

            try
            {
                _locator.ModelMatrix = _boneAt(currentTime);
                Simulator?.AdvanceTo(currentTime - _startTime);
            }
            catch (Exception e)
            {
                // Nothing throws into the viewport: one log line, and this effect stops updating.
                _logger.Here().Error($"{_locator.Name} stopped updating - {e.Message}");
                _hasError = true;
            }
        }

        public void CleanUp()
        {
            _locator.Parent.RemoveObject(_locator);
            if (_particles == null)
                return;
            _particles.Parent.RemoveObject(_particles);
            _particles.RenderItem.Dispose();
        }
    }
}
