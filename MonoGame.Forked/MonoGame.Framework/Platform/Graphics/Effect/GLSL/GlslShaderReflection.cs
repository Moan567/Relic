using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Microsoft.Xna.Framework.Graphics
{
    internal struct GlslUniformInfo
    {
        public string Name;
        public GLSLParameterType Type;
        public int ArraySize; // 0 = scalar, >0 = fixed-size array
    }

    internal struct GlslAttributeInfo
    {
        public string Name;
        public VertexElementUsage Usage;
        public int Index;
    }

    internal static class GlslShaderReflection
    {
        // Matches: uniform <type> <name> [ arraySize ] ;
        private static readonly Regex UniformRegex = new Regex(
            @"^\s*uniform\s+(?<type>float|int|bool|vec2|vec3|vec4|mat3|mat4|sampler2D|samplerCube)\s+" +
            @"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(\[\s*(?<arr>\d+)\s*\])?\s*;",
            RegexOptions.Compiled | RegexOptions.Multiline);

        // Matches: (attribute|in) <type> in_<Usage><Index> ;
        private static readonly Regex AttributeRegex = new Regex(
            @"^\s*(attribute|in)\s+(vec2|vec3|vec4|float)\s+in_(?<usage>Position|Normal|Tangent|Binormal|" +
            @"TextureCoordinate|Color|BlendIndices|BlendWeight)(?<index>\d+)\s*;",
            RegexOptions.Compiled | RegexOptions.Multiline);

        public static List<GlslUniformInfo> FindUniforms(string glslSource)
        {
            var result = new List<GlslUniformInfo>();
            foreach (Match m in UniformRegex.Matches(StripComments(glslSource)))
            {
                if (!TryParseUniformType(m.Groups["type"].Value, out var type))
                    continue;

                var arraySize = 0;
                if (m.Groups["arr"].Success)
                    arraySize = int.Parse(m.Groups["arr"].Value);

                result.Add(new GlslUniformInfo
                {
                    Name = m.Groups["name"].Value,
                    Type = type,
                    ArraySize = arraySize,
                });
            }
            return result;
        }

        public static List<GlslAttributeInfo> FindAttributes(string glslSource)
        {
            var result = new List<GlslAttributeInfo>();
            foreach (Match m in AttributeRegex.Matches(StripComments(glslSource)))
            {
                var usage = (VertexElementUsage)Enum.Parse(typeof(VertexElementUsage), m.Groups["usage"].Value);
                var index = int.Parse(m.Groups["index"].Value);
                var fullName = "in_" + m.Groups["usage"].Value + m.Groups["index"].Value;

                result.Add(new GlslAttributeInfo
                {
                    Name = fullName,
                    Usage = usage,
                    Index = index,
                });
            }
            return result;
        }

        private static bool TryParseUniformType(string glslType, out GLSLParameterType type)
        {
            switch (glslType)
            {
                case "bool": type = GLSLParameterType.Bool; return true;
                case "int": type = GLSLParameterType.Int32; return true;
                case "float": type = GLSLParameterType.Single; return true;
                case "vec2": type = GLSLParameterType.Vector2; return true;
                case "vec3": type = GLSLParameterType.Vector3; return true;
                case "vec4": type = GLSLParameterType.Vector4; return true;
                case "mat3": type = GLSLParameterType.Matrix3x3; return true;
                case "mat4": type = GLSLParameterType.Matrix; return true;
                case "sampler2D": type = GLSLParameterType.Texture2D; return true;
                case "samplerCube": type = GLSLParameterType.TextureCube; return true;
                default:
                    type = default;
                    return false;
            }
        }

        private static string StripComments(string source)
        {
            source = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);
            source = Regex.Replace(source, @"//.*?$", "", RegexOptions.Multiline);
            return source;
        }
    }
}
