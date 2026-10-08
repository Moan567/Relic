using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MonoGame.OpenGL;

namespace Microsoft.Xna.Framework.Graphics
{
    public sealed class GLSLEffectPass
    {
        public string Name { get; }

        public BlendState BlendState { get; set; }
        public DepthStencilState DepthStencilState { get; set; }
        public RasterizerState RasterizerState { get; set; }

        internal readonly Shader VertexShader;
        internal readonly Shader PixelShader;

        private readonly GLSLEffect _effect;
        private readonly List<(string name, int slot)> _vertexSamplers;
        private readonly List<(string name, int slot)> _pixelSamplers;

        private ShaderProgram _lastProgram;

        internal GLSLEffectPass(
            GLSLEffect effect,
            string name,
            Shader vertexShader,
            Shader pixelShader,
            List<(string, int)> vertexSamplers,
            List<(string, int)> pixelSamplers)
        {
            _effect = effect;
            Name = name;
            VertexShader = vertexShader;
            PixelShader = pixelShader;
            _vertexSamplers = vertexSamplers;
            _pixelSamplers = pixelSamplers;
        }

        public void Apply()
        {
            var device = _effect.GraphicsDevice;

            device.VertexShader = VertexShader;
            device.PixelShader = PixelShader;

            var program = device.GetOrLinkShaderProgram(VertexShader, PixelShader);
            if (program.Program == -1)
                throw new InvalidOperationException(
                    $"GLSLEffect pass '{Name}' failed to link its GLSL program. Check the debug output for the GLSL compiler/linker log.");

            GL.UseProgram(program.Program);
            GraphicsExtensions.CheckGLError();

            if (_lastProgram != program)
            {
                foreach (var (name, slot) in _vertexSamplers)
                    BindSamplerUnit(program.Program, name, slot);
                foreach (var (name, slot) in _pixelSamplers)
                    BindSamplerUnit(program.Program, name, slot);

                _lastProgram = program;
            }

            foreach (var parameter in _effect.Parameters)
                parameter.Apply(program.Program);

            BindTextures(_vertexSamplers, device.VertexTextures, device.VertexSamplerStates);
            BindTextures(_pixelSamplers, device.Textures, device.SamplerStates);

            if (RasterizerState != null)
                device.RasterizerState = RasterizerState;
            if (BlendState != null)
                device.BlendState = BlendState;
            if (DepthStencilState != null)
                device.DepthStencilState = DepthStencilState;
        }

        private static void BindSamplerUnit(int program, string uniformName, int slot)
        {
            var location = GL.GetUniformLocation(program, uniformName);
            GraphicsExtensions.CheckGLError();
            if (location == -1)
                return;

            GL.Uniform1(location, slot);
            GraphicsExtensions.CheckGLError();
        }

        private void BindTextures(List<(string name, int slot)> samplers, TextureCollection textures, SamplerStateCollection samplerStates)
        {
            foreach (var (name, slot) in samplers)
            {
                var parameter = _effect.Parameters[name];
                textures[slot] = parameter?.Data as Texture;

                if (parameter?.SamplerState != null)
                    samplerStates[slot] = parameter.SamplerState;
            }
        }
    }

    public sealed class GLSLEffectPassCollection : IEnumerable<GLSLEffectPass>
    {
        private readonly List<GLSLEffectPass> _passes;
        private readonly Dictionary<string, GLSLEffectPass> _byName;

        internal GLSLEffectPassCollection(List<GLSLEffectPass> passes)
        {
            _passes = passes;
            _byName = new Dictionary<string, GLSLEffectPass>();
            foreach (var p in passes)
                _byName[p.Name] = p;
        }

        public int Count => _passes.Count;
        public GLSLEffectPass this[int index] => _passes[index];
        public GLSLEffectPass this[string name] => _byName[name];

        public IEnumerator<GLSLEffectPass> GetEnumerator() => _passes.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class GLSLEffectTechnique
    {
        public string Name { get; }
        public GLSLEffectPassCollection Passes { get; }

        internal GLSLEffectTechnique(string name, GLSLEffectPassCollection passes)
        {
            Name = name;
            Passes = passes;
        }
    }

    public sealed class GLSLEffectTechniqueCollection : IEnumerable<GLSLEffectTechnique>
    {
        private readonly List<GLSLEffectTechnique> _techniques;
        private readonly Dictionary<string, GLSLEffectTechnique> _byName;

        internal GLSLEffectTechniqueCollection(List<GLSLEffectTechnique> techniques)
        {
            _techniques = techniques;
            _byName = new Dictionary<string, GLSLEffectTechnique>();
            foreach (var t in techniques)
                _byName[t.Name] = t;
        }

        public int Count => _techniques.Count;
        public GLSLEffectTechnique this[int index] => _techniques[index];
        public GLSLEffectTechnique this[string name] => _byName[name];

        public IEnumerator<GLSLEffectTechnique> GetEnumerator() => _techniques.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class GLSLTechniqueSource
    {
        public string TechniqueName { get; }
        public string PassName { get; }
        public string VertexSource { get; }
        public string FragmentSource { get; }

        public string VertexIdentity { get; }
        public string FragmentIdentity { get; }

        public GLSLTechniqueSource(string techniqueName, string passName, string vertexSource, string fragmentSource,
            string vertexIdentity = null, string fragmentIdentity = null)
        {
            TechniqueName = techniqueName;
            PassName = passName;
            VertexSource = vertexSource;
            FragmentSource = fragmentSource;
            VertexIdentity = vertexIdentity;
            FragmentIdentity = fragmentIdentity;
        }
    }

    public sealed class GLSLEffect : GraphicsResource
    {
        public static GlslIncludeResolver Includes { get; } = new GlslIncludeResolver();

        public GLSLEffectParameterCollection Parameters { get; }
        public GLSLEffectTechniqueCollection Techniques { get; private set; }
        public GLSLEffectTechnique CurrentTechnique { get; set; }

        private GLSLEffect(GraphicsDevice device, GLSLEffectParameterCollection parameters)
        {
            GraphicsDevice = device ?? throw new ArgumentNullException(nameof(device));
            Parameters = parameters;
        }

        public static GLSLEffect FromSource(
            GraphicsDevice device,
            string vertexSource,
            string fragmentSource,
            string techniqueName = "Default",
            string passName = "Pass1")
        {
            return FromSources(device, new[]
            {
                new GLSLTechniqueSource(techniqueName, passName, vertexSource, fragmentSource),
            });
        }

        public static GLSLEffect FromFiles(
            GraphicsDevice device,
            string vertexShaderPath,
            string fragmentShaderPath,
            string techniqueName = "Default",
            string passName = "Pass1")
        {
            var vertexFullPath = System.IO.Path.GetFullPath(vertexShaderPath);
            var fragmentFullPath = System.IO.Path.GetFullPath(fragmentShaderPath);

            return FromSources(device, new[]
            {
                new GLSLTechniqueSource(
                    techniqueName, passName,
                    System.IO.File.ReadAllText(vertexFullPath),
                    System.IO.File.ReadAllText(fragmentFullPath),
                    vertexIdentity: "file:" + vertexFullPath,
                    fragmentIdentity: "file:" + fragmentFullPath),
            });
        }

        public static GLSLEffect FromEmbeddedResource(
            GraphicsDevice device,
            Assembly assembly,
            string vertexResourceName,
            string fragmentResourceName,
            string techniqueName = "Default",
            string passName = "Pass1")
        {
            return FromSources(device, new[]
            {
                new GLSLTechniqueSource(
                    techniqueName, passName,
                    ReadEmbeddedResource(assembly, vertexResourceName),
                    ReadEmbeddedResource(assembly, fragmentResourceName),
                    vertexIdentity: "res:" + assembly.GetName().Name + ":" + vertexResourceName,
                    fragmentIdentity: "res:" + assembly.GetName().Name + ":" + fragmentResourceName),
            });
        }

        private static string ReadEmbeddedResource(Assembly assembly, string resourceName)
        {
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    var available = string.Join(", ", assembly.GetManifestResourceNames());
                    throw new ArgumentException(
                        $"GLSLEffect: embedded resource '{resourceName}' was not found in assembly " +
                        $"'{assembly.GetName().Name}'. Resources available: {available}", nameof(resourceName));
                }
                using (var reader = new System.IO.StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }

        public static GLSLEffect FromSources(GraphicsDevice device, IReadOnlyList<GLSLTechniqueSource> sources)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            if (sources == null || sources.Count == 0)
                throw new ArgumentException("At least one technique source is required.", nameof(sources));

            var expanded = new List<GLSLTechniqueSource>(sources.Count);
            foreach (var source in sources)
            {
                var vertexIdentity = source.VertexIdentity ?? ("mem:" + source.TechniqueName + "/" + source.PassName + ".vert");
                var fragmentIdentity = source.FragmentIdentity ?? ("mem:" + source.TechniqueName + "/" + source.PassName + ".frag");

                expanded.Add(new GLSLTechniqueSource(
                    source.TechniqueName,
                    source.PassName,
                    GlslPreprocessor.Expand(source.VertexSource, vertexIdentity, Includes),
                    GlslPreprocessor.Expand(source.FragmentSource, fragmentIdentity, Includes),
                    vertexIdentity,
                    fragmentIdentity));
            }
            sources = expanded;

            var parametersByName = new Dictionary<string, GLSLEffectParameter>();
            foreach (var source in sources)
            {
                foreach (var glsl in new[] { source.VertexSource, source.FragmentSource })
                {
                    foreach (var uniform in GlslShaderReflection.FindUniforms(glsl))
                    {
                        if (!parametersByName.ContainsKey(uniform.Name))
                            parametersByName[uniform.Name] = new GLSLEffectParameter(uniform.Name, uniform.Type, uniform.ArraySize);
                    }
                }
            }

            var effect = new GLSLEffect(device, new GLSLEffectParameterCollection(parametersByName.Values));

            var byTechnique = new Dictionary<string, List<GLSLTechniqueSource>>();
            foreach (var source in sources)
            {
                if (!byTechnique.TryGetValue(source.TechniqueName, out var list))
                    byTechnique[source.TechniqueName] = list = new List<GLSLTechniqueSource>();
                list.Add(source);
            }

            var techniques = new List<GLSLEffectTechnique>();
            foreach (var kvp in byTechnique)
            {
                var passes = new List<GLSLEffectPass>();

                foreach (var source in kvp.Value)
                {
                    var vertexShader = BuildShader(device, ShaderStage.Vertex, source.VertexSource,
                        source.VertexIdentity, out var vertexSamplers);

                    var pixelShader = BuildShader(device, ShaderStage.Pixel, source.FragmentSource,
                        source.FragmentIdentity, out var pixelSamplers);

                    passes.Add(new GLSLEffectPass(
                        effect,
                        source.PassName,
                        vertexShader,
                        pixelShader,
                        vertexSamplers,
                        pixelSamplers));
                }

                techniques.Add(new GLSLEffectTechnique(kvp.Key, new GLSLEffectPassCollection(passes)));
            }

            effect.Techniques = new GLSLEffectTechniqueCollection(techniques);
            effect.CurrentTechnique = effect.Techniques[0];
            return effect;
        }

        private static Shader BuildShader(
            GraphicsDevice device,
            ShaderStage stage,
            string source,
            string sourceFileForErrors,
            out List<(string name, int slot)> samplers)
        {
            samplers = new List<(string, int)>();
            foreach (var uniform in GlslShaderReflection.FindUniforms(source))
            {
                if (uniform.Type == GLSLParameterType.Texture2D || uniform.Type == GLSLParameterType.TextureCube)
                    samplers.Add((uniform.Name, samplers.Count));
            }

            var attributes = new List<VertexAttribute>();
            if (stage == ShaderStage.Vertex)
            {
                foreach (var attr in GlslShaderReflection.FindAttributes(source))
                {
                    attributes.Add(new VertexAttribute
                    {
                        name = attr.Name,
                        usage = attr.Usage,
                        index = attr.Index,
                    });
                }
            }

            var samplerInfos = Array.Empty<SamplerInfo>(); // Texture-unit binding is handled by GLSLEffectPass itself.

            return new Shader(device, stage, sourceFileForErrors, "main", source, samplerInfos, attributes.ToArray());
        }
    }
}
