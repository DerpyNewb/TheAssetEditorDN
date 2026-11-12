using GameWorld.Core.Services;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Components.Rendering
{
    internal class RenderTargetHelper
    {
        public static RenderTarget2D GetRenderTarget(GraphicsDevice device, RenderTarget2D existingRenderTarget, float imageUpScale, IGraphicsResourceCreator graphicsResourceCreator, int? widthOverride = null, int? heightOverride = null,
            int multiSampleCount = 8, DepthFormat depthFormat = DepthFormat.Depth24)
        {
            var width = (int)((widthOverride ?? device.Viewport.Width) * imageUpScale);
            var height = (int)((heightOverride ?? device.Viewport.Height) * imageUpScale);

            if (existingRenderTarget == null)
            {
                return graphicsResourceCreator.CreateRenderTarget2D(width, height, false, SurfaceFormat.Color, depthFormat, multiSampleCount, RenderTargetUsage.PreserveContents);
            }

            if (existingRenderTarget.Width == width && existingRenderTarget.Height == height)
                return existingRenderTarget;

            graphicsResourceCreator.DisposeTracked(existingRenderTarget);
            return graphicsResourceCreator.CreateRenderTarget2D(width, height, false, SurfaceFormat.Color, depthFormat, multiSampleCount, RenderTargetUsage.PreserveContents);
        }
    }
}
