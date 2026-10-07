using Chisel.Models;
using Chisel.Models.Data;
using Chisel.Models.Morph;
using Force.DeepCloner;
using MessagePack;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine
{
    public static class AssetManager
    {
        private static Dictionary<string, object> loadedAssets = new Dictionary<string, object>();

        public static T LoadAsset<T>(string path, string name)
        {
            if (loadedAssets.ContainsKey(name)) return (T)loadedAssets[name];

            loadedAssets.Add(name,MainEngine.Instance.Content.Load<T>(path));
            return (T)loadedAssets[name];
        }

        /// <summary>
        /// Loads a material's shader by name, preferring a GLSL shader
        /// (Content/Shaders/&lt;name&gt;/&lt;name&gt;.csh) over the legacy
        /// content-pipeline .fx (Content/Shaders/&lt;name&gt;.fx) when both
        /// exist.
        /// </summary>
        public static ShaderHandle LoadMaterialShader(string shaderName)
        {
            if (loadedAssets.TryGetValue(shaderName, out var cached)) return (ShaderHandle)cached;

            ShaderHandle shader = ShaderBuilder.TryBuildMaterialShader(MainEngine.Instance.GraphicsDevice, shaderName, out var glsl)
                ? glsl
                : MainEngine.Instance.Content.Load<Effect>($"Shaders/{shaderName}");

            loadedAssets.Add(shaderName, shader);
            return shader;
        }
        /// <summary>
        /// Returns a shallow clone of a cached <see cref="CModel"/>. The returned instance shares
        /// the underlying VertexBuffers and textures with the cache.
        /// </summary>
        public static CModel GetModelInstance(string path, string name)
        {
            if (loadedAssets.ContainsKey(name)) return CreateNewInstance((CModel)loadedAssets[name]);

            loadedAssets.Add(name, CCMDLHandler.LoadFromCCMDL(File.ReadAllBytes(path),MainEngine.Instance.GraphicsDevice));
            return (CModel)loadedAssets[name];
        }
        public static void AddAsset(string name, object add)
        {
            if (loadedAssets.ContainsKey(name)) return;

            loadedAssets.Add(name,add);
        }

        public static void RemoveAsset(string name) {  loadedAssets.Remove(name); }
        public static object GetAsset(string name)
        {
            if (!loadedAssets.ContainsKey(name)) return null;

            return loadedAssets[name];
        }
        private static CModel CreateNewInstance(CModel cached)
        {
            CModel instance = DeepClonerExtensions.ShallowClone(cached);

            instance.Bodygroups = cached.Bodygroups.Select(bg =>
            {
                var bgInstance = DeepClonerExtensions.ShallowClone(bg);

                if (bgInstance.MorphTargets?.Count > 0)
                {
                    bgInstance.MorphApplicator = new CMorphApplicator(
                        MainEngine.Instance.GraphicsDevice, bgInstance.MeshData.Vertices);
                }

                return bgInstance;
            }).ToList();

            if (cached.EyeDefs != null)
            {
                instance.EyeDefs = cached.EyeDefs
                    .Select(eye => DeepClonerExtensions.ShallowClone(eye))
                    .ToList();
            }

            return instance;
        }
    }
}
