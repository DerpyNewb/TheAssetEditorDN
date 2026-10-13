using GameWorld.Core.Components.Navigation;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Test.Rendering
{
    // The gizmo's sun marker points the way the sun lies on screen, so a model's lit side faces it: the way to the sun the models
    // are lit from (ParticleRenderItem.Sun), seen through the app's camera, whose projection mirrors x.
    internal class SunMarkerTests
    {
        static CommonShaderParameters Scene(float yaw, float pitch, float lightX, float lightY)
        {
            var camera = Vector3.Transform(Vector3.Backward * 5, Matrix.CreateFromYawPitchRoll(yaw, pitch, 0));
            var view = Matrix.CreateLookAt(camera, Vector3.Zero, Vector3.Up);
            var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, 0.01f, 100) * Matrix.CreateScale(-1, 1, 1);
            return new CommonShaderParameters(view, projection, camera, Vector3.Zero, 0, lightX, lightY, 1, [], Vector3.One);
        }

        // Down the screen is +y, as the gizmo draws.
        static Vector2 OnScreen(CommonShaderParameters p, Vector3 point)
        {
            var clip = Vector4.Transform(new Vector4(point, 1), p.View * p.Projection);
            return new Vector2(clip.X / clip.W, -clip.Y / clip.W);
        }

        [TestCase(0.8f, 0.32f, 0f, 0f)]
        [TestCase(0.8f, 0.32f, 0f, 1.2f)]
        [TestCase(-2f, -0.5f, 0.7f, 2.5f)]
        [TestCase(3f, 0.9f, -1f, 0.4f)]
        public void SunMarker_PointsWhereTheSunLiesOnScreen(float yaw, float pitch, float lightX, float lightY)
        {
            var p = Scene(yaw, pitch, lightX, lightY);
            var onScreen = OnScreen(p, ParticleRenderItem.Sun(p).ToSun * 0.01f) - OnScreen(p, Vector3.Zero);
            var (marker, _) = ViewportGizmo.SunMarker(p, Vector2.Zero, 35);
            Assert.That(Vector2.Dot(Vector2.Normalize(marker), Vector2.Normalize(onScreen)), Is.GreaterThan(0.999));
        }

        // Solid when the sun is on the camera's side, lighting the face seen; hollow when it is behind the model.
        [Test]
        public void SunMarker_SaysWhetherTheSunIsBehindTheModel()
        {
            var front = ViewportGizmo.SunMarker(Scene(0, 0, 0, 0), Vector2.Zero, 35).TowardCamera;
            var behind = ViewportGizmo.SunMarker(Scene(0, 0, 0, MathHelper.Pi), Vector2.Zero, 35).TowardCamera;
            Assert.That((front, behind), Is.EqualTo((true, false)));
        }
    }
}
