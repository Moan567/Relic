using Chisel.Collision;
using Chisel.Utils;
using Engine.Entities;
using Engine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Utils
{
    public abstract class Displayable : IDisposable
    {
        public readonly static List<Displayable> Displayables = new List<Displayable>();

        public static int ShadowQualityBias = 0;

        static (int, bool)[] ShadowQualities = new (int, bool)[]
        {
            (128, true),
            (64, true),
            (1, false),
        };

        public ShaderHandle Shader;
        public int MaterialID;
        public Matrix Transform;

        protected Vector3 ambientLightColor;
        protected float diffuseIntensity;

        public bool InDir = false;
        public bool DrawShadow = true;
        public Texture2D ShadowTexture;

        internal (int res, bool rendertarget) PreviousShadowQuality = ShadowQualities[0];
        internal (int res, bool rendertarget) CurrentShadowQuality = ShadowQualities[0];
        internal bool ForceShadowReprojection;

        protected float shadowDistance;
        protected int shadowDecalIndex = -1;
        protected OrientedBoundingBox shadowBounds;
        protected Vector3 shadowFloorPoint;
        protected Vector3 lastShadowPos = new Vector3(float.NegativeInfinity);
        protected Vector3[] currentIndirectSH = new Vector3[9];
        private Vector3[] currentIndirectSHBlend = new Vector3[9];
        private LightNodeBundle.LightNode[] nodes;
        private Vector3 lastPos; 

        private Vector3 lastRayOrigin = new Vector3(float.NegativeInfinity);
        private Vector3 lastLightDir = new Vector3(float.NegativeInfinity);

        private const int MaxStaticLights = 4;
        private readonly Vector4[] lightPositions = new Vector4[MaxStaticLights];
        private readonly Vector4[] lightColors = new Vector4[MaxStaticLights];
        private readonly Vector4[] lightAngles = new Vector4[MaxStaticLights];
        private Light[] topLights;
        private float[] topKeys;

        private const int MaxRealtimeLightsPerObject = 4;
        protected readonly Vector4[] realtimeLightPositionsCache = new Vector4[MaxRealtimeLightsPerObject];
        protected readonly Vector4[] realtimeLightColorsCache = new Vector4[MaxRealtimeLightsPerObject];
        protected readonly Vector4[] realtimeLightSpotDataCache = new Vector4[MaxRealtimeLightsPerObject];
        protected int realtimeLightCountCache;
        private Light[] topRealtimeLights;
        private float[] topRealtimeKeys;

        private int staticLightCount;
        public static bool FlipWinding = false;

        protected RasterizerState rasterizerState;
        protected RasterizerState rasterizerStateFlipped;

        public Displayable(ShaderHandle shader, int material)
        {
            this.Shader = shader;
            this.MaterialID = material;
            this.Shader?.Param("MainTex").SetValue(GlobalMapData.LoadedMaterials[material].Texture);
            this.Shader?.Param("SpecTex").SetValue(GlobalMapData.LoadedMaterials[material].Specular);
            this.Shader?.Param("shine").SetValue(GlobalMapData.LoadedMaterials[material].Reflectivity);

            shadowBounds = new OrientedBoundingBox(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));

            rasterizerState = new RasterizerState();
            rasterizerState.FillMode = RenderEngine.CurrentWireframeDisplayMode == 0 ? FillMode.Solid : FillMode.WireFrame;
            rasterizerState.CullMode = CullMode.CullCounterClockwiseFace;

            rasterizerStateFlipped = new RasterizerState();
            rasterizerStateFlipped.FillMode = rasterizerState.FillMode;
            rasterizerStateFlipped.CullMode = CullMode.CullClockwiseFace;

            ShadowTexture = RenderEngine.BlobShadowTexture;
        }

        /// <summary>
        /// Sets up shader variables for realtime and static light interaction before rendering. If you forget to call this
        /// before rendering, you may experience unwanted lighting artifacts.
        /// </summary>
        /// <param name="position">The position of this <see cref="Displayable"/> in world-space.</param>
        public virtual void CheckForLights(Vector3 position, float ambientIntensity = 1f)
        {
            InDir = true;

            topRealtimeLights ??= new Light[MaxRealtimeLightsPerObject];
            topRealtimeKeys ??= new float[MaxRealtimeLightsPerObject];

            var realtimeLights = MainEngine.CurrentRealtimeLights.GetValues();
            int keptRT = 0;
            for (int i = 0; i < realtimeLights.Length; i++)
            {
                var light = realtimeLights[i];
                float distSq = Vector3.DistanceSquared(position, light.Position);
                float key = distSq > light.Range * light.Range ? float.PositiveInfinity : distSq;

                if (keptRT < MaxRealtimeLightsPerObject)
                {
                    int insertAt = keptRT++;
                    while (insertAt > 0 && topRealtimeKeys[insertAt - 1] > key)
                    {
                        topRealtimeKeys[insertAt] = topRealtimeKeys[insertAt - 1];
                        topRealtimeLights[insertAt] = topRealtimeLights[insertAt - 1];
                        insertAt--;
                    }
                    topRealtimeKeys[insertAt] = key;
                    topRealtimeLights[insertAt] = light;
                }
                else if (key < topRealtimeKeys[MaxRealtimeLightsPerObject - 1])
                {
                    int insertAt = MaxRealtimeLightsPerObject - 1;
                    while (insertAt > 0 && topRealtimeKeys[insertAt - 1] > key)
                    {
                        topRealtimeKeys[insertAt] = topRealtimeKeys[insertAt - 1];
                        topRealtimeLights[insertAt] = topRealtimeLights[insertAt - 1];
                        insertAt--;
                    }
                    topRealtimeKeys[insertAt] = key;
                    topRealtimeLights[insertAt] = light;
                }
            }

            realtimeLightCountCache = keptRT;
            for (int i = 0; i < keptRT; i++)
            {
                var light = topRealtimeLights[i];
                realtimeLightPositionsCache[i] = new Vector4(light.Position, light.Range);
                realtimeLightColorsCache[i] = new Vector4(light.Color.ToVector3(), light.Intensity);
                realtimeLightSpotDataCache[i] = new Vector4(light.Rotation, MathHelper.ToRadians(light.Type == Light.LightType.Point ? -1 : light.Angle));
            }

            if (nodes == null || Vector3.DistanceSquared(position, lastPos) > 0.01f)
            {
                lastPos = position;
                var nodeBundle = LightNodeTraversal.GetClosestNodeBundle(position);

                if (nodeBundle == null) return;

                nodes = nodeBundle.GetClosest(CMath.ClampToBoundingBox(position, nodeBundle.Box));
            }

            if (nodes.Length == 0) return;

            var node = nodes[0];
            var data = node.Data;

            topLights ??= new Light[MaxStaticLights];
            topKeys ??= new float[MaxStaticLights];

            Light sun = default;
            int kept = 0;
            for (int i = 0; i < MainEngine.ActiveStaticLights.Count; i++)
            {
                var light = MainEngine.ActiveStaticLights[i];
                if (light.Type == Light.LightType.Directional)
                {
                    sun = light;
                    continue;
                }

                float key;
                if (light.Intensity <= 0f)
                {
                    key = float.NegativeInfinity;
                }
                else
                {
                    float distSq = Vector3.DistanceSquared(lastPos, light.Position);
                    key = distSq > light.Range * light.Range ? float.PositiveInfinity : distSq;
                }

                if (kept < MaxStaticLights)
                {
                    int insertAt = kept++;
                    while (insertAt > 0 && topKeys[insertAt - 1] > key)
                    {
                        topKeys[insertAt] = topKeys[insertAt - 1];
                        topLights[insertAt] = topLights[insertAt - 1];
                        insertAt--;
                    }
                    topKeys[insertAt] = key;
                    topLights[insertAt] = light;
                }
                else if (key < topKeys[MaxStaticLights - 1])
                {
                    int insertAt = MaxStaticLights - 1;
                    while (insertAt > 0 && topKeys[insertAt - 1] > key)
                    {
                        topKeys[insertAt] = topKeys[insertAt - 1];
                        topLights[insertAt] = topLights[insertAt - 1];
                        insertAt--;
                    }
                    topKeys[insertAt] = key;
                    topLights[insertAt] = light;
                }
            }

            bool sunBlocked = false;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i].LightNum == sun.ID) { sunBlocked = data[i].LightBlocked; break; }
            }
            InDir = !sunBlocked;

            staticLightCount = kept;
            for (int i = 0; i < kept; i++)
            {
                var light = topLights[i];
                float intensity = light.Intensity;
                Vector3 colorTint = Vector3.One;

                for (int d = 0; d < data.Length; d++)
                {
                    if (data[d].LightNum == light.ID) { if (data[d].LightBlocked) intensity = 0; break; }
                }

                if (!string.IsNullOrEmpty(light.TargetName))
                {
                    var groupName = LightGroupRuntime.GetCorrespondingGroup(light.TargetName);
                    if (!string.IsNullOrEmpty(groupName) && LightGroupRuntime.TryGetResolvedTint(groupName, out var groupTint, out var groupIntensity))
                    {
                        colorTint = groupTint;
                        intensity *= groupIntensity;
                    }
                    else
                    {
                        intensity = 0f;
                    }
                }

                lightPositions[i] = new Vector4(light.Position, light.Range);
                lightColors[i] = new Vector4(light.Color.ToVector3() * colorTint, intensity);
                lightAngles[i] = new Vector4(light.Rotation, light.Type == Light.LightType.SpotLight ? MathHelper.ToRadians(light.Angle) : -100);
            }

            float totalWeight = 0f;
            Array.Clear(currentIndirectSHBlend);
            unsafe
            {
                fixed (Vector3* sh = currentIndirectSH)
                {
                    fixed (Vector3* blend = currentIndirectSHBlend)
                    {
                        foreach (var nClose in nodes)
                        {
                            if (nClose.IndirectCoefficients == null) continue;
                            float weight = 1f / (Vector3.Distance(position, nClose.Pos) + 0.001f);
                            totalWeight += weight;
                            fixed (Vector3* coeffs = nClose.IndirectCoefficients)
                                for (int s = 0; s < 9; s++)
                                    blend[s] += coeffs[s] * weight;
                        }
                        if (totalWeight > 0f)
                            for (int s = 0; s < 9; s++)
                                sh[s] = blend[s] / totalWeight * ambientIntensity;
                    }

                    var activeGroups = LightGroupRuntime.ActiveGroupSamples;
                    for (int g = 0; g < activeGroups.Count; g++)
                    {
                        var sample = activeGroups[g];
                        if (sample.intensity <= 0f) continue;

                        Array.Clear(currentIndirectSHBlend);
                        float groupWeight = 0f;

                        fixed (Vector3* blend = currentIndirectSHBlend)
                        {
                            foreach (var nClose in nodes)
                            {
                                if (nClose.GroupIndirectCoefficients == null || sample.groupIndex >= nClose.GroupIndirectCoefficients.Length) continue;
                                var coeffs = nClose.GroupIndirectCoefficients[sample.groupIndex];
                                if (coeffs == null) continue;

                                float weight = 1f / (Vector3.Distance(position, nClose.Pos) + 0.001f);
                                groupWeight += weight;
                                for (int s = 0; s < 9; s++)
                                    blend[s] += coeffs[s] * weight;
                            }

                            if (groupWeight > 0f)
                                for (int s = 0; s < 9; s++)
                                    sh[s] += blend[s] / groupWeight * sample.colorTint * sample.intensity * ambientIntensity;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Calculates a new point for the decal-shadow, and reprojects the decal if they're sufficiently far away from eachother.
        /// </summary>
        public virtual void Update(Vector3? overrideShadowProjectionPoint = null)
        {
            if (!DrawShadow) return;

            var lightDir = GetLightDir();
            var pos = overrideShadowProjectionPoint ?? Transform.Translation;

            bool moved = Vector3.DistanceSquared(pos, lastRayOrigin) >= 0.0001f || Vector3.DistanceSquared(lightDir, lastLightDir) >= 0.0001f;

            if (moved)
            {
                lastRayOrigin = pos;
                lastLightDir = lightDir;

                var hit = BSPRoot.TraceRay(new Ray(pos + lightDir * 0.05f, -lightDir), 512f, true);
                if (hit.Hit) shadowFloorPoint = hit.Point + lightDir * 0.5f;
                else shadowFloorPoint = pos;

                shadowDistance = Vector3.Distance(hit.Point, pos) / 2f;
                shadowBounds.Extents.Z = shadowDistance + 1.25f;
            }

            var shadowQuality = GetShadowQuality();

            PreviousShadowQuality = CurrentShadowQuality;
            ForceShadowReprojection |= shadowQuality.res != PreviousShadowQuality.res;
            CurrentShadowQuality = shadowQuality;

            if (!moved && !ForceShadowReprojection)
                return;

            if (Vector3.DistanceSquared(lastShadowPos, shadowFloorPoint) > 0.000625f || ForceShadowReprojection)
            {
                Reproject();
            }
        }
        public void Reproject()
        {
            if (DrawShadow)
            {
                var lightDir = GetLightDir();

                shadowBounds.Transformation = (Matrix.CreateWorld(shadowFloorPoint + lightDir * shadowDistance - lightDir * 1, lightDir, Vector3.Up));
                shadowDecalIndex = DecalGenerator.ProjectDecal(shadowBounds, ShadowTexture, shadowDecalIndex);
                DecalManager.RealtimeDecals[shadowDecalIndex].effect = DecalEffect.Multiply;
                lastShadowPos = shadowFloorPoint;
            }
        }

        internal (int res, bool rendertarget) GetShadowQuality()
        {
            int qual = ShadowQualityBias;

            const float falloffRange = 20*20;
            const float invfallof = 1f / falloffRange;
            qual += (int)(Vector3.DistanceSquared(shadowFloorPoint, RenderEngine.CameraPosition) * invfallof);

            qual = int.Clamp(qual, 0, (ShadowQualities.Length - 1));

            return ShadowQualities[qual];
        }

        /// <returns>Direction of the light</returns>
        protected Vector3 GetLightDir()
        {
            // fun little test
            //return -Vector3.Normalize(transform.Translation);

            //if (!GetShadowQuality().rendertarget) return Vector3.Normalize(Vector3.Up+Vector3.Forward*0.5f);

            return MainEngine.Instance.DirectionalLightDirection;
        }

        /// <summary>
        /// Initializes all shader parameters for rendering. Matrices, textures, material settings, lights, etc.
        /// </summary>
        /// <param name="world"></param>
        /// <param name="view"></param>
        /// <param name="projection"></param>
        public void PrepareShaderParamsForRendering(Matrix world, Matrix view, Matrix projection, bool ignoreTextures = false)
        {
            PrepareShaderParamsForRendering(world, view, projection, Shader, ignoreTextures);

            switch (RenderEngine.ShaderQuality)
            {
                case QualityLevel.Low:
                    Shader.SetTechnique("Low");
                    break;
                case QualityLevel.Medium:
                    Shader.SetTechnique("Med");
                    break;
                case QualityLevel.High:
                    Shader.SetTechnique("High");
                    break;
            }
        }
        public void PrepareShaderParamsForRendering(Matrix world, Matrix view, Matrix projection, ShaderHandle targetShader, bool ignoreTextures = false)
        {
            var p = targetShader;

            p.Param("DiffuseLightDirection").SetValue(Vector3.Normalize(MainEngine.Instance.DirectionalLightDirection));
            p.Param("DiffuseColor").SetValue(MainEngine.Instance.DirectionalLightColor.ToVector3());
            p.Param("screenSize").SetValue(MainEngine.Instance.GraphicsDevice.Viewport.Bounds.Size.ToVector2());

            p.Param("World").SetValue(world);
            p.Param("WorldInverseTranspose").SetValue(Matrix.CreateWorld(Vector3.Zero, world.Forward, world.Up));
            p.Param("View").SetValue(view);
            p.Param("Projection").SetValue(projection);

            if (!ignoreTextures)
            {
                p.Param("MainTex").SetValue(RenderEngine.CurrentWireframeDisplayMode == 0 && !RenderEngine.ShowBlankTexture ? GlobalMapData.LoadedMaterials[MaterialID].Texture : RenderEngine.WhiteTexture);
                p.Param("SpecTex").SetValue(RenderEngine.CurrentWireframeDisplayMode == 0 ? GlobalMapData.LoadedMaterials[MaterialID].Specular : RenderEngine.WhiteTexture);
                p.Param("NormalTex").SetValue(RenderEngine.CurrentWireframeDisplayMode == 0 ? GlobalMapData.LoadedMaterials[MaterialID].Normal : RenderEngine.PurpleTexture);
                p.Param("shine").SetValue(GlobalMapData.LoadedMaterials[MaterialID].Reflectivity);
            }

            if (MainEngine.ShaderRealtimeLightPositions != null)
            {
                p.Param("realtimeLightPositions").SetValue(realtimeLightPositionsCache);
                p.Param("realtimeLightColors").SetValue(realtimeLightColorsCache);
                p.Param("realtimeLightSpotData").SetValue(realtimeLightSpotDataCache);
                p.Param("realtimeLightCount").SetValue(realtimeLightCountCache);
            }
            if (EnvCubemap.Cubemaps != null && EnvCubemap.Cubemaps.Count > 0)
            {
                var cube = CubemapHandler.GetNearestCubemap(Transform.Translation);
                if (cube != null && cube.diffusionMaps != null) p.Param("cubemap").SetValue(cube.diffusionMaps[5]);
                else p.Param("cubemap").SetValue(Skybox.GetSkyTexture());
            }
            else p.Param("cubemap").SetValue(Skybox.GetSkyTexture());
            p.Param("cubemapSize").SetValue(128 >> (5));

            p.Param("cameraPos").SetValue(RenderEngine.CameraPosition);
            p.Param("cameraForward").SetValue(MainEngine.Instance.CameraForward);

            p.Param("static_lightaffectingcount").SetValue(staticLightCount);
            p.Param("static_lightpositions").SetValue(lightPositions);
            p.Param("static_lightcolors").SetValue(lightColors);
            p.Param("static_lightangles").SetValue(lightAngles);
            p.Param("indirectSH").SetValue(currentIndirectSH);

            p.Param("fogColor").SetValue(MainEngine.Instance.FogColor.ToVector4());
            p.Param("fogStart").SetValue(MainEngine.Instance.FogBeginDepth);
            p.Param("fogEnd").SetValue(MainEngine.Instance.FogEndDepth);
            p.Param("fogIntensity").SetValue(MainEngine.Instance.FogStrength);

            diffuseIntensity = CMath.MoveTowards(diffuseIntensity, InDir ? MainEngine.Instance.DirectionalLightStrength : 0, MainEngine.PreviousFrameDelta * 5);
            p.Param("DiffuseIntensity").SetValue(diffuseIntensity);
        }
        public void Dispose()
        {
            Displayables.Remove(this);
            DeleteShadow();

            GC.SuppressFinalize(this);
        }
        public void DeleteShadow()
        {
            if(shadowDecalIndex != -1) DecalManager.RealtimeDecals[shadowDecalIndex] = null;

            DecalManager.MarkDirty();

            shadowDecalIndex = -1;
        }
    }
}
 