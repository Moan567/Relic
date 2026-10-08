using System;
using System.Collections;
using System.Collections.Generic;
using MonoGame.OpenGL;

namespace Microsoft.Xna.Framework.Graphics
{
    public enum GLSLParameterType
    {
        Bool,
        Int32,
        Single,
        Vector2,
        Vector3,
        Vector4,
        Matrix,
        Matrix3x3,
        Texture2D,
        TextureCube,
    }

    public sealed class GLSLEffectParameter
    {
        public string Name { get; }
        public GLSLParameterType Type { get; }

        public int ArraySize { get; }

        internal object Data;

        public SamplerState SamplerState { get; set; }

        private readonly Dictionary<int, int> _locationCache = new Dictionary<int, int>();

        private float[] _matrixBuffer;
        private float[] _vectorBuffer;

        internal GLSLEffectParameter(string name, GLSLParameterType type, int arraySize)
        {
            Name = name;
            Type = type;
            ArraySize = arraySize;
            SamplerState = SamplerState.LinearWrap;
        }

        public void SetValue(bool value) => Data = value;
        public void SetValue(int value) => Data = Type == GLSLParameterType.Single ? (object)(float)value : value;
        public void SetValue(float value) => Data = Type == GLSLParameterType.Int32 ? (object)(int)value : value;
        public void SetValue(float[] value) => Data = value;
        public void SetValue(Vector2 value) => Data = value;
        public void SetValue(Vector3 value) => Data = Type == GLSLParameterType.Vector4 ? (object)new Vector4(value, 1f) : value;
        public void SetValue(Vector4 value) => Data = Type == GLSLParameterType.Vector3 ? (object)new Vector3(value.X, value.Y, value.Z) : value;
        public void SetValue(Vector4[] value) => Data = value;
        public void SetValue(Vector3[] value) => Data = value;
        public void SetValue(Color value) => Data = value.ToVector4();
        public void SetValue(Matrix value) => Data = value;
        public void SetValue(Matrix[] value) => Data = value;
        public void SetValue(Texture2D value) => Data = value;
        public void SetValue(TextureCube value) => Data = value;

        private int _lastProgram = -1;
        private int _lastLocation = -1;

        internal int GetLocation(int program)
        {
            if (program == _lastProgram)
                return _lastLocation;

            if (!_locationCache.TryGetValue(program, out var location))
            {
                location = GL.GetUniformLocation(program, Name);
                GraphicsExtensions.CheckGLError();
                _locationCache[program] = location;
            }

            _lastProgram = program;
            _lastLocation = location;
            return location;
        }

        internal void Apply(int program)
        {
            if (Type == GLSLParameterType.Texture2D || Type == GLSLParameterType.TextureCube)
                return;

            if (Data == null)
                return;

            var location = GetLocation(program);
            if (location == -1)
                return; // Not present in this particular linked program (or optimized out).

            switch (Type)
            {
                case GLSLParameterType.Bool:
                    GL.Uniform1(location, (bool)Data ? 1 : 0);
                    break;

                case GLSLParameterType.Int32:
                    GL.Uniform1(location, (int)Data);
                    break;

                case GLSLParameterType.Single:
                    if (Data is float[] floatArray)
                        GL.Uniform1(location, floatArray.Length, floatArray);
                    else
                        GL.Uniform1(location, (float)Data);
                    break;

                case GLSLParameterType.Vector2:
                    {
                        var v = (Vector2)Data;
                        GL.Uniform2(location, v.X, v.Y);
                        break;
                    }

                case GLSLParameterType.Vector3:
                    if (Data is Vector3[] vec3Array)
                    {
                        var flat = new float[vec3Array.Length * 3];
                        for (var i = 0; i < vec3Array.Length; i++)
                        {
                            flat[i * 3 + 0] = vec3Array[i].X;
                            flat[i * 3 + 1] = vec3Array[i].Y;
                            flat[i * 3 + 2] = vec3Array[i].Z;
                        }
                        GL.Uniform3(location, vec3Array.Length, flat);
                    }
                    else
                    {
                        var v = (Vector3)Data;
                        GL.Uniform3(location, v.X, v.Y, v.Z);
                    }
                    break;

                case GLSLParameterType.Vector4:
                    if (Data is Vector4[] vecArray)
                    {
                        if (_vectorBuffer == null || _vectorBuffer.Length != vecArray.Length * 4)
                            _vectorBuffer = new float[vecArray.Length * 4];
                        for (var i = 0; i < vecArray.Length; i++)
                        {
                            _vectorBuffer[i * 4 + 0] = vecArray[i].X;
                            _vectorBuffer[i * 4 + 1] = vecArray[i].Y;
                            _vectorBuffer[i * 4 + 2] = vecArray[i].Z;
                            _vectorBuffer[i * 4 + 3] = vecArray[i].W;
                        }
                        GL.Uniform4(location, vecArray.Length, _vectorBuffer);
                    }
                    else
                    {
                        var v = (Vector4)Data;
                        GL.Uniform4(location, v.X, v.Y, v.Z, v.W);
                    }
                    break;

                case GLSLParameterType.Matrix:
                    if (Data is Matrix[] matArray)
                    {
                        if (_matrixBuffer == null || _matrixBuffer.Length != matArray.Length * 16)
                            _matrixBuffer = new float[matArray.Length * 16];
                        for (var i = 0; i < matArray.Length; i++)
                            WriteMatrix(_matrixBuffer, i * 16, matArray[i]);
                        GL.UniformMatrix4(location, matArray.Length, false, _matrixBuffer);
                    }
                    else
                    {
                        _matrixBuffer ??= new float[16];
                        WriteMatrix(_matrixBuffer, 0, (Matrix)Data);
                        GL.UniformMatrix4(location, 1, false, _matrixBuffer);
                    }
                    break;

                case GLSLParameterType.Matrix3x3:
                    if (Data is float[] mat3 && mat3.Length == 9)
                        GL.UniformMatrix3(location, 1, false, mat3);
                    break;
            }

            GraphicsExtensions.CheckGLError();
        }

        private static void WriteMatrix(float[] dest, int offset, in Matrix m)
        {
            // Column-major
            dest[offset + 0] = m.M11; dest[offset + 1] = m.M21; dest[offset + 2] = m.M31; dest[offset + 3] = m.M41;
            dest[offset + 4] = m.M12; dest[offset + 5] = m.M22; dest[offset + 6] = m.M32; dest[offset + 7] = m.M42;
            dest[offset + 8] = m.M13; dest[offset + 9] = m.M23; dest[offset + 10] = m.M33; dest[offset + 11] = m.M43;
            dest[offset + 12] = m.M14; dest[offset + 13] = m.M24; dest[offset + 14] = m.M34; dest[offset + 15] = m.M44;
        }
    }

    public sealed class GLSLEffectParameterCollection : IEnumerable<GLSLEffectParameter>
    {
        private readonly List<GLSLEffectParameter> _parameters;
        private readonly Dictionary<string, GLSLEffectParameter> _byName;

        internal GLSLEffectParameterCollection(IEnumerable<GLSLEffectParameter> parameters)
        {
            _parameters = new List<GLSLEffectParameter>(parameters);
            _byName = new Dictionary<string, GLSLEffectParameter>();
            foreach (var p in _parameters)
                _byName[p.Name] = p;
        }

        public int Count => _parameters.Count;

        public GLSLEffectParameter this[int index] => _parameters[index];

        public GLSLEffectParameter this[string name] =>
            _byName.TryGetValue(name, out var p) ? p : null;

        public IEnumerator<GLSLEffectParameter> GetEnumerator() => _parameters.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
