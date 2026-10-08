// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using MonoGame.OpenGL;

namespace Microsoft.Xna.Framework.Graphics
{
    internal partial class ConstantBuffer
    {
        private ShaderProgram _shaderProgram = null;
        private int _location;

        internal int HashKey { get; private set; }

        private void PlatformInitialize()
        {
            var data = new byte[_parameters.Length];
            for (var i = 0; i < _parameters.Length; i++)
            {
                unchecked
                {
                    data[i] = (byte)(_parameters[i] | _offsets[i]);
                }
            }

            HashKey = MonoGame.Framework.Utilities.Hash.ComputeHash(data);
        }

        private void PlatformClear()
        {
            _shaderProgram = null;
        }

        public unsafe void PlatformApply(GraphicsDevice device, ShaderProgram program)
        {
            if (_shaderProgram != program)
            {
                var location = program.GetUniformLocation(_name);
                if (location == -1)
                    return;

                _shaderProgram = program;
                _location = location;
                _dirty = true;
            }

            if (!program.WasLastConstantBufferAt(_location, this))
                _dirty = true;

            if (!_dirty)
                return;

            fixed (byte* bytePtr = _buffer)
            {
                GL.Uniform4(_location, _buffer.Length / 16, (float*)bytePtr);
                GraphicsExtensions.CheckGLError();
            }

            _dirty = false;
        }
    }
}
