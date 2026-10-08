// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using MonoGame.OpenGL;

namespace Microsoft.Xna.Framework.Graphics
{
    public partial class RenderTargetCube
    {
        private static Action<RenderTargetCube> DisposeAction =
            (t) => t.GraphicsDevice.PlatformDeleteRenderTarget(t);

        int IRenderTarget.GLTexture
        {
            get { return glTexture; }
        }

        TextureTarget IRenderTarget.GLTarget
        {
            get { return glTarget; }
        }

        int IRenderTarget.GLColorBuffer { get; set; }
        int IRenderTarget.GLDepthBuffer { get; set; }
        int IRenderTarget.GLStencilBuffer { get; set; }
        int IRenderTarget.GLDepthTexture { get; set; }

        private DepthTextureView depthTextureView;

        public Texture2D DepthTexture
        {
            get
            {
                var depthGLTexture = ((IRenderTarget)this).GLDepthTexture;
                if (depthGLTexture == 0)
                    return null;

                if (depthTextureView == null || depthTextureView.glTexture != depthGLTexture)
                    depthTextureView = new DepthTextureView(GraphicsDevice, size, size, depthGLTexture);

                return depthTextureView;
            }
        }

        TextureTarget IRenderTarget.GetFramebufferTarget(RenderTargetBinding renderTargetBinding)
        {
            return TextureTarget.TextureCubeMapPositiveX + renderTargetBinding.ArraySlice;
        }

        private void PlatformConstruct(
            GraphicsDevice graphicsDevice, bool mipMap, DepthFormat preferredDepthFormat, int preferredMultiSampleCount, RenderTargetUsage usage)
        {
            Threading.BlockOnUIThread(() =>
            {
                graphicsDevice.PlatformCreateRenderTarget(
                    this, size, size, mipMap, this.Format, preferredDepthFormat, preferredMultiSampleCount, usage);
            });
        }

        /// <summary/>
        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed)
            {
                if (GraphicsDevice != null)
                {
                    Threading.BlockOnUIThread(DisposeAction, this);
                }
            }

            base.Dispose(disposing);
        }

        private sealed class DepthTextureView : Texture2D
        {
            internal DepthTextureView(GraphicsDevice graphicsDevice, int width, int height, int depthGLTexture)
                : base(graphicsDevice, width, height, false, SurfaceFormat.Single, SurfaceType.SwapChainRenderTarget)
            {
                glTexture = depthGLTexture;
                glTarget = TextureTarget.Texture2D;
            }

            protected override void Dispose(bool disposing)
            {
            }
        }
    }
}
