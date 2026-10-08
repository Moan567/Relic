// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Xna.Framework.Graphics
{
    internal partial class ConstantBuffer : GraphicsResource
    {
        private readonly byte[] _buffer;

        private readonly int[] _parameters;

        private readonly int[] _offsets;

        private readonly string _name;

        private ulong _stateKey;

        private bool _dirty;
        private bool Dirty
        {
            get { return _dirty; }
        }

        public ConstantBuffer(ConstantBuffer cloneSource)
        {
            GraphicsDevice = cloneSource.GraphicsDevice;

            _name = cloneSource._name;
            _parameters = cloneSource._parameters;
            _offsets = cloneSource._offsets;

            _buffer = (byte[])cloneSource._buffer.Clone();
            PlatformInitialize();
        }

        public ConstantBuffer(GraphicsDevice device,
                              int sizeInBytes,
                              int[] parameterIndexes,
                              int[] parameterOffsets,
                              string name)
        {
            GraphicsDevice = device;

            _buffer = new byte[sizeInBytes];

            _parameters = parameterIndexes;
            _offsets = parameterOffsets;

            _name = name;

            PlatformInitialize();
        }

        internal void Clear()
        {
            PlatformClear();
        }

        private int SetParameter(int offset, EffectParameter param)
        {
            return SetParameter(ref MemoryMarshal.GetReference(new Span<byte>(_buffer)), offset, param);
        }

        private static int SetParameter(ref byte bufferBase, int offset, EffectParameter param)
        {
            const int elementSize = 4;
            const int rowSize = elementSize * 4;

            var rowsUsed = 0;

            var elements = param.Elements;
            if (elements.Count > 0)
            {
                for (var i = 0; i < elements.Count; i++)
                {
                    var rowsUsedSubParam = SetParameter(ref bufferBase, offset, elements[i]);
                    offset += rowsUsedSubParam * rowSize;
                    rowsUsed += rowsUsedSubParam;
                }
            }
            else if (param.Data != null)
            {
                switch (param.ParameterType)
                {
                    case EffectParameterType.Single:
                    case EffectParameterType.Int32:
                    case EffectParameterType.Bool:
                        // HLSL assumes matrices are column-major, whereas in-memory we use row-major.
                        // TODO: HLSL can be told to use row-major. We should handle that too.
                        if (param.ParameterClass == EffectParameterClass.Matrix)
                        {
                            rowsUsed = param.ColumnCount;
                            SetData(ref bufferBase, offset, param.ColumnCount, param.RowCount, param.Data);
                        }
                        else
                        {
                            rowsUsed = param.RowCount;
                            SetData(ref bufferBase, offset, param.RowCount, param.ColumnCount, param.Data);
                        }
                        break;
                    default:
                        throw new NotSupportedException("Not supported!");
                }
            }

            return rowsUsed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetData(ref byte bufferBase, int offset, int rows, int columns, object data)
        {
            const int elementSize = 4;
            const int rowSize = elementSize * 4;

            ref byte destPtr = ref Unsafe.Add(ref bufferBase, offset);

            if (data is float[] floatData)
            {
                ref byte srcPtr = ref Unsafe.As<float, byte>(ref MemoryMarshal.GetReference(new Span<float>(floatData)));
                CopyRows(ref destPtr, ref srcPtr, rows, columns, rowSize, elementSize);
            }
            else if (data is int[] intData)
            {
                ref byte srcPtr = ref Unsafe.As<int, byte>(ref MemoryMarshal.GetReference(new Span<int>(intData)));
                CopyRows(ref destPtr, ref srcPtr, rows, columns, rowSize, elementSize);
            }
            else if (data is bool[] boolData)
            {
                ref byte srcPtr = ref Unsafe.As<bool, byte>(ref MemoryMarshal.GetReference(new Span<bool>(boolData)));
                CopyRows(ref destPtr, ref srcPtr, rows, columns, rowSize, elementSize);
            }
            else
            {
                throw new NotSupportedException("Unsupported EffectParameter data type: " + data.GetType());
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CopyRows(ref byte destPtr, ref byte srcPtr, int rows, int columns, int rowSize, int elementSize)
        {
            if (rows == 1 && columns == 1)
            {
                Unsafe.CopyBlock(ref destPtr, ref srcPtr, (uint)elementSize);
            }
            else if (rows == 1 || (rows == 4 && columns == 4))
            {
                Unsafe.CopyBlock(ref destPtr, ref srcPtr, (uint)(rows * columns * elementSize));
            }
            else
            {
                var stride = columns * elementSize;
                for (var y = 0; y < rows; y++)
                {
                    Unsafe.CopyBlock(ref Unsafe.Add(ref destPtr, rowSize * y), ref Unsafe.Add(ref srcPtr, stride * y), (uint)(columns * elementSize));
                }
            }
        }

        public void Update(EffectParameterCollection parameters)
        {
            if (_stateKey > EffectParameter.NextStateKey)
            {
                _stateKey = 0;
            }

            ref byte bufferBase = ref MemoryMarshal.GetReference(new Span<byte>(_buffer));

            for (var p = 0; p < _parameters.Length; p++)
            {
                var index = _parameters[p];
                var param = parameters[index];

                if (param.StateKey < _stateKey)
                {
                    continue;
                }

                var offset = _offsets[p];
                _dirty = true;

                SetParameter(ref bufferBase, offset, param);
            }

            _stateKey = EffectParameter.NextStateKey;
        }
    }
}
