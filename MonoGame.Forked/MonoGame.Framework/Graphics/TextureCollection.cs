// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;

namespace Microsoft.Xna.Framework.Graphics
{
    /// <summary>
    /// Represents a collection of <see cref="Texture"/> objects. This class cannot be inherited.
    /// </summary>
    public sealed partial class TextureCollection
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly Texture[] _textures;
        private ShaderStage _stage;
        private int _dirty;

        internal int HighestUsedSlot { get; private set; } = -1;
        internal void SetHighestUsedSlot(int slot)
        {
            HighestUsedSlot = slot;
        }

        internal TextureCollection(GraphicsDevice graphicsDevice, int maxTextures, ShaderStage stage)
        {
            _graphicsDevice = graphicsDevice;
            _textures = new Texture[maxTextures];
            _stage = stage;
            _dirty = int.MaxValue;
            PlatformInit();
        }

        public Texture this[int index]
        {
            get { return _textures[index]; }
            set
            {
                if (_stage == ShaderStage.Vertex && !_graphicsDevice.GraphicsCapabilities.SupportsVertexTextures)
                    throw new NotSupportedException("Vertex textures are not supported on this device.");

                if (_textures[index] == value)
                    return;

                _textures[index] = value;
                _dirty |= 1 << index;

                if (value != null && index > HighestUsedSlot)
                    HighestUsedSlot = index;
            }
        }

        internal void Clear()
        {
            for (var i = 0; i < _textures.Length; i++)
                _textures[i] = null;

            PlatformClear();
            _dirty = int.MaxValue;
            HighestUsedSlot = -1;
        }

        internal void Dirty()
        {
            _dirty = int.MaxValue;
        }

        internal void SetTextures(GraphicsDevice device)
        {
            if (_stage == ShaderStage.Vertex && !device.GraphicsCapabilities.SupportsVertexTextures)
                return;

            PlatformSetTextures(device);
        }
    }
}
