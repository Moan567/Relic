// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
//
// Author: Kenneth James Pouncey

using MonoGame.OpenGL;
using System;

namespace Microsoft.Xna.Framework.Graphics
{
    public sealed partial class SamplerStateCollection
    {
        private void PlatformSetSamplerState(int index)
        {
        }

        private void PlatformClear()
        {
        }

        private void PlatformDirty()
        {
        }

        internal void PlatformSetSamplers(GraphicsDevice device)
        {
            var slotCount = Math.Min(_actualSamplers.Length, device.Textures.HighestUsedSlot + 1);

            for (var i = 0; i < slotCount; i++)
            {
                var sampler = _actualSamplers[i];
                var texture = device.Textures[i];

                if (sampler != null && texture != null && sampler != texture.glLastSamplerState)
                {
                    GL.ActiveTexture(TextureUnit.Texture0 + i);
                    GraphicsExtensions.CheckGLError();

                    sampler.Activate(device, texture.glTarget, texture.LevelCount > 1);
                    texture.glLastSamplerState = sampler;
                }
            }
        }
    }
}
