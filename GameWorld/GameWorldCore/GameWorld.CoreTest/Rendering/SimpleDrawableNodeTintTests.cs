using System.Reflection;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.RenderItems;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.WpfWindow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering
{
    // SuperView turns an event tag's marker yellow while it fires by tinting the marker's node.
    [Apartment(ApartmentState.STA)]
    public class SimpleDrawableNodeTintTests
    {
        AssetEditorTestRunner _runner;
        IServiceScope _scope;
        RenderEngineComponent _engine;

        [SetUp]
        public void Setup()
        {
            _runner = new AssetEditorTestRunner();
            _scope = _runner.ServiceProvider.CreateScope();
            var game = _scope.ServiceProvider.GetRequiredService<IWpfGame>();
            game.AddComponent(_scope.ServiceProvider.GetRequiredService<ArcBallCamera>());
            _engine = game.AddComponent(_scope.ServiceProvider.GetRequiredService<RenderEngineComponent>());
        }

        [TearDown]
        public void TearDown() => _scope.Dispose();

        // The lines the engine was handed this frame, cleared after reading.
        List<Color> LineColours()
        {
            var lines = (List<VertexPositionColor>)typeof(RenderEngineComponent).GetField("_renderLines", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_engine)!;
            var colours = lines.Select(x => x.Color).ToList();
            _engine.Update(new GameTime());
            return colours;
        }

        [Test]
        public void A_tint_recolours_every_line_and_label_and_clearing_it_restores_them()
        {
            var node = new SimpleDrawableNode("marker");
            var label = new WorldTextRenderItem(_engine, "FirePos", Vector3.Zero, Color.Red);
            node.AddItem(label);
            node.AddItem(LineHelper.AddCircle(Vector3.Zero, 0.3f, Color.Red));

            node.Render(_engine, Matrix.Identity);
            Assert.That(LineColours(), Is.Not.Empty.And.All.EqualTo(Color.Red));
            Assert.That(label.Colour, Is.EqualTo(Color.Red));

            node.Tint = Color.Yellow;
            node.Render(_engine, Matrix.Identity);
            Assert.That(LineColours(), Is.All.EqualTo(Color.Yellow));
            Assert.That(label.Colour, Is.EqualTo(Color.Yellow));

            node.Tint = null;
            node.Render(_engine, Matrix.Identity);
            Assert.That(LineColours(), Is.All.EqualTo(Color.Red));
            Assert.That(label.Colour, Is.EqualTo(Color.Red));
        }

        // A black guide draws in the background's guide colour, white on the dark one; a tint replaces that too.
        [Test]
        public void A_tint_wins_over_the_guide_colour_a_black_line_gets()
        {
            var node = new SimpleDrawableNode("marker");
            node.AddItem(LineHelper.AddCircle(Vector3.Zero, 0.3f, Color.Black));

            node.Render(_engine, Matrix.Identity);
            Assert.That(LineColours(), Is.All.EqualTo(_engine.GuideColour));

            node.Tint = Color.Yellow;
            node.Render(_engine, Matrix.Identity);
            Assert.That(LineColours(), Is.All.EqualTo(Color.Yellow));
        }
    }
}
