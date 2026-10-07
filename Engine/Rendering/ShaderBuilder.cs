using Engine.Rendering;
using Engine.Scripting.ValueScript;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Engine
{
    public static class ShaderBuilder
    {
        private static Assembly EngineAssembly => typeof(ShaderBuilder).Assembly;
        public const string LibraryRoot = "Engine.Content.GLSLShaders";
        private static bool _librariesRegistered;

        public static void RegisterEmbeddedLibraries()
        {
            if (_librariesRegistered) return;
            GLSLEffect.Includes.AddEmbeddedResources(EngineAssembly, LibraryRoot);
            _librariesRegistered = true;
        }

        private static string ResourceNameFor(string relativePath) =>
            LibraryRoot + "." + relativePath.Replace('/', '.').Replace('\\', '.');

        private static string ReadEmbedded(string relativePath)
        {
            var resourceName = ResourceNameFor(relativePath);
            using var stream = EngineAssembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"ShaderBuilder: embedded GLSL resource not found: {resourceName}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        public sealed class Builder
        {
            private readonly List<GLSLTechniqueSource> _sources = new();
            private readonly Dictionary<string, string> _sourceCache = new();

            private string Read(string relativePath) =>
                _sourceCache.TryGetValue(relativePath, out var cached)
                    ? cached
                    : (_sourceCache[relativePath] = ReadEmbedded(relativePath));

            public Builder Technique(string techniqueName, string vertPath, string fragPath)
                => Pass(techniqueName, "Pass1", vertPath, fragPath);

            public Builder Pass(string techniqueName, string passName, string vertPath, string fragPath)
            {
                _sources.Add(new GLSLTechniqueSource(
                    techniqueName, passName, Read(vertPath), Read(fragPath),
                    vertexIdentity: "res:" + EngineAssembly.GetName().Name + ":" + ResourceNameFor(vertPath),
                    fragmentIdentity: "res:" + EngineAssembly.GetName().Name + ":" + ResourceNameFor(fragPath)));
                return this;
            }

            public GLSLEffect Build(GraphicsDevice device, string defaultTechnique = null)
            {
                RegisterEmbeddedLibraries();
                var effect = GLSLEffect.FromSources(device, _sources);
                if (defaultTechnique != null)
                    effect.CurrentTechnique = effect.Techniques[defaultTechnique];
                return effect;
            }
        }

        public static Builder New() => new Builder();

        public static GLSLEffect BuildWorldShader(GraphicsDevice device)
        {
            var effect = New()
                .Technique("High", "World/WorldDefault.vert", "World/WorldDefault_High.frag")
                .Technique("High_AlphaClip", "World/WorldDefault.vert", "World/WorldDefault_High.frag")
                .Technique("Med", "World/WorldDefault.vert", "World/WorldDefault_Med.frag")
                .Technique("Med_AlphaClip", "World/WorldDefault.vert", "World/WorldDefault_Med.frag")
                .Technique("Low", "World/WorldDefault.vert", "World/WorldDefault_Low.frag")
                .Technique("Low_AlphaClip", "World/WorldDefault.vert", "World/WorldDefault_Low.frag")
                .Technique("DepthOnly", "World/WorldDefault_DepthOnly.vert", "World/WorldDefault_DepthOnly.frag")
                .Build(device, "High");

            RegisterWorldTextureSampling(effect);
            RegisterLightmapSampling(effect);
            return effect;
        }

        public static GLSLEffect BuildModelShader(GraphicsDevice device)
        {
            var effect = New()
                .Technique("High", "Models/ModelDefault.vert", "Models/ModelDefault_High.frag")
                .Technique("High_AlphaClip", "Models/ModelDefault.vert", "Models/ModelDefault_High.frag")
                .Technique("Med", "Models/ModelDefault.vert", "Models/ModelDefault_Med.frag")
                .Technique("Med_AlphaClip", "Models/ModelDefault.vert", "Models/ModelDefault_Med.frag")
                .Technique("Low", "Models/ModelDefault_Low.vert", "Models/ModelDefault_Low.frag")
                .Technique("Low_AlphaClip", "Models/ModelDefault_Low.vert", "Models/ModelDefault_Low.frag")
                .Technique("ShadowBlack", "Models/ModelDefault_Low.vert", "Models/ModelDefault_ShadowBlack.frag")
                .Build(device, "High");

            RegisterWorldTextureSampling(effect);
            return effect;
        }

        public static GLSLEffect BuildSkinnedModelShader(GraphicsDevice device)
        {
            var effect = New()
                .Technique("High", "Models/SkinnedModelDefault.vert", "Models/ModelDefault_High.frag")
                .Technique("High_AlphaClip", "Models/SkinnedModelDefault.vert", "Models/ModelDefault_High.frag")
                .Technique("Med", "Models/SkinnedModelDefault.vert", "Models/ModelDefault_Med.frag")
                .Technique("Med_AlphaClip", "Models/SkinnedModelDefault.vert", "Models/ModelDefault_Med.frag")
                .Technique("Low", "Models/SkinnedModelDefault_Low.vert", "Models/ModelDefault_Low.frag")
                .Technique("Low_AlphaClip", "Models/SkinnedModelDefault_Low.vert", "Models/ModelDefault_Low.frag")
                .Technique("ShadowBlack", "Models/SkinnedModelDefault_Low.vert", "Models/ModelDefault_ShadowBlack.frag")
                .Build(device, "High");

            RegisterWorldTextureSampling(effect);
            return effect;
        }

        public static GLSLEffect BuildPropModelShader(GraphicsDevice device) => New()
            .Pass("BasicColorDrawing", "P0", "Models/PropModel_Main.vert", "Models/PropModel_Main.frag")
            .Pass("BasicColorDrawing", "P1", "Models/PropModel_PrePass.vert", "Models/PropModel_PrePass.frag")
            .Build(device, "BasicColorDrawing");

        public static GLSLEffect BuildEyeShader(GraphicsDevice device)
        {
            var effect = New()
                .Technique("High", "Eyes/EyeShader.vert", "Eyes/EyeShader.frag")
                .Build(device, "High");

            var eyeTextureSampler = new SamplerState
            {
                Filter = TextureFilter.Linear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
            };

            if (effect.Parameters["MainTex"] != null) effect.Parameters["MainTex"].SamplerState = eyeTextureSampler;
            if (effect.Parameters["DataTex"] != null) effect.Parameters["DataTex"].SamplerState = eyeTextureSampler;

            return effect;
        }

        public static GLSLEffect BuildEyeGenerationShader(GraphicsDevice device) => New()
            .Technique("EyeGen", "Eyes/EyeGeneration.vert", "Eyes/EyeGeneration.frag")
            .Build(device, "EyeGen");

        public static GLSLEffect BuildParticlesShader(GraphicsDevice device)
        {
            var effect = New()
                .Technique("Particles", "Particles/Particles.vert", "Particles/Particles.frag")
                .Technique("Decals", "Particles/Decals.vert", "Particles/Decals.frag")
                .Build(device, "Particles");

            RegisterLightmapSampling(effect);
            return effect;
        }

        public static GLSLEffect BuildTerrainShader(GraphicsDevice device)
        {
            var effect = New()
                .Technique("BasicColorDrawing", "Terrain/TerrainDefault.vert", "Terrain/TerrainDefault.frag")
                .Build(device, "BasicColorDrawing");

            RegisterWorldTextureSampling(effect);
            RegisterLightmapSampling(effect);
            return effect;
        }

        public static GLSLEffect BuildSkyboxShader(GraphicsDevice device)
        {
            var effect = New()
                .Technique("Skybox", "Skybox/Skybox.vert", "Skybox/Skybox.frag")
                .Build(device, "Skybox");

            if (effect.Parameters["SkyBoxTexture"] != null)
            {
                effect.Parameters["SkyBoxTexture"].SamplerState = SamplerState.LinearClamp;
            }

            return effect;
        }

        public static GLSLEffect BuildRTShadowsShader(GraphicsDevice device) => New()
            .Technique("Textured", "RTShadows/RTShadows.vert", "RTShadows/RTShadows.frag")
            .Build(device, "Textured");

        public static GLSLEffect BuildLightGroupCompositeShader(GraphicsDevice device) => New()
            .Technique("Composite", "LightGroupComposite/LightGroupComposite.vert", "LightGroupComposite/LightGroupComposite.frag")
            .Build(device, "Composite");

        public static GLSLEffect BuildDepthShader(GraphicsDevice device) => New()
            .Technique("BasicColorDrawing", "DepthShader/DepthShader.vert", "DepthShader/DepthShader.frag")
            .Build(device, "BasicColorDrawing");

        public static GLSLEffect BuildEquirectToCubeShader(GraphicsDevice device) => New()
            .Technique("EquirectToCube", "EquirectToCube/EquirectToCube.vert", "EquirectToCube/EquirectToCube.frag")
            .Build(device, "EquirectToCube");

        private static readonly string[] WorldTextureParamNames = { "BrushTex", "BrushSpec", "BrushNorm", "MainTex", "SpecTex", "NormalTex" };
        private static readonly string[] LightmapParamNames = { "LightmapB1", "LightmapB2", "LightmapB3" };

        private static readonly List<GLSLEffect> worldTextureSamplingTargets = new();
        private static readonly List<GLSLEffect> lightmapSamplingTargets = new();

        private static void RegisterWorldTextureSampling(GLSLEffect effect)
        {
            worldTextureSamplingTargets.Add(effect);
            ApplyWorldTextureSampling(effect);
        }

        private static void RegisterLightmapSampling(GLSLEffect effect)
        {
            lightmapSamplingTargets.Add(effect);
            ApplyLightmapSampling(effect);
        }

        private static void ApplyWorldTextureSampling(GLSLEffect effect)
        {
            foreach (var name in WorldTextureParamNames)
                if (effect.Parameters[name] != null)
                    effect.Parameters[name].SamplerState = RenderEngine.WorldTextureSamplerState;
        }

        private static void ApplyLightmapSampling(GLSLEffect effect)
        {
            foreach (var name in LightmapParamNames)
                if (effect.Parameters[name] != null)
                    effect.Parameters[name].SamplerState = RenderEngine.LightmapTextureSamplerState;
        }

        public static void RefreshSamplerStates()
        {
            foreach (var effect in worldTextureSamplingTargets) ApplyWorldTextureSampling(effect);
            foreach (var effect in lightmapSamplingTargets) ApplyLightmapSampling(effect);
        }

        // .csh manifests - used by custom shaders and by
        private static ScriptBlock ParseManifest(string text)
        {
            var tokens = new ScriptTokenizer().Tokenize(text);
            var parsed = new ScriptParser().Parse(tokens);
            parsed = InheritanceResolver.Resolve(parsed);
            parsed = VariableResolver.Resolve(parsed);
            return parsed;
        }

        private static GLSLEffect BuildFromManifest(GraphicsDevice device, ScriptBlock root, Func<string, string> read, Func<string, string> identity)
        {
            string defaultVert = root.FindFirst("vert")?.Value.StringValue;
            var sources = new List<GLSLTechniqueSource>();

            foreach (var techEntry in root.Entries)
            {
                if (techEntry.Key == null || techEntry.Value.Kind != ScriptValueKind.Block) continue;
                if (techEntry.Key.Equals("vert", StringComparison.OrdinalIgnoreCase)) continue;

                var techBlock = techEntry.Value.Block;
                var techVert = techBlock.FindFirst("vert")?.Value.StringValue ?? defaultVert;
                var techFrag = techBlock.FindFirst("frag")?.Value.StringValue;

                if (techFrag != null)
                {
                    sources.Add(new GLSLTechniqueSource(techEntry.Key, "Pass1",
                        read(techVert), read(techFrag), identity(techVert), identity(techFrag)));
                    continue;
                }

                foreach (var passEntry in techBlock.Entries)
                {
                    if (passEntry.Value.Kind != ScriptValueKind.Block) continue;
                    var passBlock = passEntry.Value.Block;
                    var passVert = passBlock.FindFirst("vert")?.Value.StringValue ?? techVert;
                    var passFrag = passBlock.FindFirst("frag")?.Value.StringValue;
                    sources.Add(new GLSLTechniqueSource(techEntry.Key, passEntry.Key,
                        read(passVert), read(passFrag), identity(passVert), identity(passFrag)));
                }
            }

            if (sources.Count == 0)
                throw new InvalidOperationException("ShaderBuilder: manifest defined no techniques.");

            RegisterEmbeddedLibraries();
            var effect = GLSLEffect.FromSources(device, sources);
            effect.CurrentTechnique = effect.Techniques[0];

            var samplersBlock = root.FindFirst("samplers");
            if (samplersBlock?.Value.Kind == ScriptValueKind.Block)
            {
                foreach (var entry in samplersBlock.Value.Block.Entries)
                {
                    if (entry.Key == null) continue;

                    var state = ParseSamplerState(entry.Value);
                    if (state == null) continue;

                    var param = effect.Parameters[entry.Key];
                    if (param != null)
                    {
                        param.SamplerState = state;
                    }
                }
            }

            return effect;
        }

        public static string ContentShadersRoot =>
            Path.Combine(AppContext.BaseDirectory, MainEngine.Instance.Content.RootDirectory, "Shaders");

        // custom shader: material.shaderName "Foo" -> Content/Shaders/Foo/Foo.csh.
        // Returns false if missing
        public static bool TryBuildMaterialShader(GraphicsDevice device, string shaderName, out GLSLEffect effect)
        {
            var shaderDir = Path.Combine(ContentShadersRoot, shaderName);
            var manifestPath = Path.Combine(shaderDir, $"{shaderName}.csh");

            if (!File.Exists(manifestPath))
            {
                effect = null;
                return false;
            }

            var root = ParseManifest(File.ReadAllText(manifestPath));
            effect = BuildFromManifest(device, root,
                rel => File.ReadAllText(Path.Combine(shaderDir, rel)),
                rel => "file:" + Path.GetFullPath(Path.Combine(shaderDir, rel)));
            return true;
        }

        public static GLSLEffect BuildContentShader(GraphicsDevice device, string name)
        {
            if (!TryBuildMaterialShader(device, name, out var effect))
                throw new InvalidOperationException($"ShaderBuilder: no Content/Shaders/{name}/{name}.csh found.");
            return effect;
        }

        private static readonly Dictionary<string, SamplerState> SamplerPresets = new(StringComparer.OrdinalIgnoreCase)
        {
            { "PointClamp", SamplerState.PointClamp },
            { "PointWrap", SamplerState.PointWrap },
            { "LinearClamp", SamplerState.LinearClamp },
            { "LinearWrap", SamplerState.LinearWrap },
            { "AnisotropicClamp", SamplerState.AnisotropicClamp },
            { "AnisotropicWrap", SamplerState.AnisotropicWrap },
        };

        private static TextureFilter ParseFilter(string s) => s?.ToLowerInvariant() switch
        {
            "point" => TextureFilter.Point,
            "linear" => TextureFilter.Linear,
            "anisotropic" => TextureFilter.Anisotropic,
            "linearmippoint" => TextureFilter.LinearMipPoint,
            "pointmiplinear" => TextureFilter.PointMipLinear,
            _ => TextureFilter.Linear,
        };

        private static TextureAddressMode ParseAddress(string s) => s?.ToLowerInvariant() switch
        {
            "clamp" => TextureAddressMode.Clamp,
            "wrap" => TextureAddressMode.Wrap,
            "mirror" => TextureAddressMode.Mirror,
            "border" => TextureAddressMode.Border,
            _ => TextureAddressMode.Wrap,
        };

        private static SamplerState ParseSamplerState(ScriptValue value)
        {
            if (value.Kind == ScriptValueKind.String)
            {
                return SamplerPresets.TryGetValue(value.StringValue, out var preset) ? preset : null;
            }

            if (value.Kind == ScriptValueKind.Block)
            {
                var block = value.Block;
                var address = block.FindFirst("address")?.Value.StringValue;
                var addressU = block.FindFirst("addressU")?.Value.StringValue ?? address;
                var addressV = block.FindFirst("addressV")?.Value.StringValue ?? address;

                var state = new SamplerState
                {
                    Filter = ParseFilter(block.FindFirst("filter")?.Value.StringValue),
                    AddressU = ParseAddress(addressU),
                    AddressV = ParseAddress(addressV),
                    AddressW = ParseAddress(addressU),
                };

                if (int.TryParse(block.FindFirst("anisotropy")?.Value.StringValue, out var aniso))
                {
                    state.MaxAnisotropy = aniso;
                }

                return state;
            }

            return null;
        }
    }
}