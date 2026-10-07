using Chisel.Particles;
using Chisel.Utils;
using Engine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using Rockwall;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Engine.Utils
{

    public static class ParticleManager
    {
        const int MaxSystems = 512;
        static ParticleSpawner[] activeSpawners = new ParticleSpawner[MaxSystems];
        static int systemIndex = -1;

        static readonly Dictionary<string, ParticleSystemBehavior> systemCache = new();

        static DynamicVertexBuffer vertexBuffer;
        static VertexPositionColorTexture[] vertexArray;
        const int MaxParticlesPerFrame = 10000;
        static bool bufferInitialized = false;

        static BlendState blendAlpha;
        static BlendState blendAdditive;
        static BlendState blendNonPremultiplied;

        static ShaderHandle particleEffect;

        private class MultiDrawGroup
        {
            public Texture2D Texture;
            public ParticleBlendMode BlendMode;
            public int[] Starts = new int[64];
            public int[] Counts = new int[64];
            public int RangeCount;

            public void AddOrExtend(int start, int count)
            {
                if (RangeCount > 0)
                {
                    int lastIdx = RangeCount - 1;
                    if (Starts[lastIdx] + Counts[lastIdx] == start)
                    {
                        Counts[lastIdx] += count;
                        return;
                    }
                }

                if (RangeCount == Starts.Length)
                {
                    Array.Resize(ref Starts, Starts.Length * 2);
                    Array.Resize(ref Counts, Counts.Length * 2);
                }
                Starts[RangeCount] = start;
                Counts[RangeCount] = count;
                RangeCount++;
            }

            public void Clear() => RangeCount = 0;
        }

        static readonly Dictionary<(Texture2D, ParticleBlendMode), MultiDrawGroup> multiDrawGroups = new();
        static readonly List<(Texture2D, ParticleBlendMode)> multiDrawGroupOrder = new();

        static MultiDrawGroup GetOrAddGroup(Texture2D texture, ParticleBlendMode blendMode)
        {
            var key = (texture, blendMode);
            if (!multiDrawGroups.TryGetValue(key, out var group))
            {
                group = new MultiDrawGroup { Texture = texture, BlendMode = blendMode };
                multiDrawGroups[key] = group;
            }

            if (group.RangeCount == 0)
            {
                multiDrawGroupOrder.Add(key);
            }

            return group;
        }

        static void EnsureBufferInitialized()
        {
            if (bufferInitialized) return;

            vertexArray = new VertexPositionColorTexture[MaxParticlesPerFrame * 6];
            vertexBuffer = new DynamicVertexBuffer(
                MainEngine.Instance.GraphicsDevice,
                typeof(VertexPositionColorTexture),
                MaxParticlesPerFrame * 6,
                BufferUsage.WriteOnly);

            blendAlpha = new BlendState
            {
                ColorSourceBlend = Blend.One,
                ColorDestinationBlend = Blend.InverseSourceAlpha,
                AlphaSourceBlend = Blend.One,
                AlphaDestinationBlend = Blend.InverseSourceAlpha,
                ColorWriteChannels = ColorWriteChannels.All,
                ColorWriteChannels1 = ColorWriteChannels.None,
                ColorWriteChannels2 = ColorWriteChannels.None,
                ColorWriteChannels3 = ColorWriteChannels.None,
            };

            blendAdditive = new BlendState
            {
                ColorSourceBlend = Blend.SourceAlpha,
                ColorDestinationBlend = Blend.One,
                AlphaSourceBlend = Blend.SourceAlpha,
                AlphaDestinationBlend = Blend.One,
                ColorWriteChannels = ColorWriteChannels.All,
                ColorWriteChannels1 = ColorWriteChannels.None,
                ColorWriteChannels2 = ColorWriteChannels.None,
                ColorWriteChannels3 = ColorWriteChannels.None,
            };

            blendNonPremultiplied = new BlendState
            {
                ColorSourceBlend = Blend.SourceAlpha,
                ColorDestinationBlend = Blend.InverseSourceAlpha,
                AlphaSourceBlend = Blend.SourceAlpha,
                AlphaDestinationBlend = Blend.InverseSourceAlpha,
                ColorWriteChannels = ColorWriteChannels.All,
                ColorWriteChannels1 = ColorWriteChannels.None,
                ColorWriteChannels2 = ColorWriteChannels.None,
                ColorWriteChannels3 = ColorWriteChannels.None,
            };

            particleEffect ??= ShaderBuilder.BuildParticlesShader(MainEngine.Instance.GraphicsDevice);
            //particleEffect ??= new BasicEffect(MainEngine.Instance.GraphicsDevice)
            //{
            //    TextureEnabled = true,
            //    VertexColorEnabled = true,
            //    LightingEnabled = false,
            //};

            bufferInitialized = true;
        }

        private static void ReserveSubsystemMaterials(ParticleSystemBehavior behavior)
        {
            if (behavior.subsystemBehaviors == null) return;

            foreach (var subsystem in behavior.subsystemBehaviors)
            {
                if (string.IsNullOrEmpty(subsystem.material)) continue;
                TextureMipGenerator.ReserveMaterial(subsystem.material);
            }
        }

        public static void PrefetchParticleSystem(string fromFile)
        {
            if (!systemCache.TryGetValue(fromFile, out var behavior))
            {
                behavior = JsonConvert.DeserializeObject<ParticleSystemBehavior>(
                               File.ReadAllText(fromFile));
                systemCache[fromFile] = behavior;
            }
            ReserveSubsystemMaterials(behavior);
        }

        public static int SpawnParticleSystem(Vector3 position, string fromFile, Matrix? spawnMatrix = null)
        {
            if (!systemCache.TryGetValue(fromFile, out var behavior))
            {
                behavior = JsonConvert.DeserializeObject<ParticleSystemBehavior>(
                               File.ReadAllText(fromFile));
                systemCache[fromFile] = behavior;
            }
            ReserveSubsystemMaterials(behavior);
            return SpawnParticleSystem(position, behavior, spawnMatrix);
        }

        public static int SpawnParticleSystem(Vector3 position, ParticleSystemBehavior behaviour, Matrix? spawnMatrix = null)
        {
            ReserveSubsystemMaterials(behaviour);
            systemIndex = (systemIndex + 1) % MaxSystems;
            activeSpawners[systemIndex] = new ParticleSpawner(position, behaviour, spawnMatrix);
            return systemIndex;
        }
        public static ParticleSpawner GetSpawner(int index) => activeSpawners[index];

        public static void UpdateSystems()
        {
            for (int i = 0; i < MaxSystems; i++)
            {
                if (activeSpawners[i] is null) continue;

                if (activeSpawners[i].system.isFinished && !activeSpawners[i].system.isLooping)
                {
                    activeSpawners[i] = null;
                    continue;
                }

                activeSpawners[i].Update();

                if (!activeSpawners[i].system.behavior.collidesWithWorld) continue;
                activeSpawners[i].UpdateCollision();
            }
        }
        public static void RenderSystems()
        {
            EnsureBufferInitialized();

            multiDrawGroupOrder.Clear();

            particleEffect.SetTechnique("Particles");

            int vertexCount = 0;

            Vector3 camFwd = RenderEngine.CameraForward;
            Vector3 camUp = Vector3.Up;
            Vector3 camRight = Vector3.Cross(camFwd, camUp);
            camUp = Vector3.Cross(camRight, camFwd);
            camRight.Normalize();
            camUp.Normalize();

            var gd = MainEngine.Instance.GraphicsDevice;

            void FlushAccumulatedToGpu()
            {
                if (vertexCount == 0) return;

                vertexBuffer.SetData(vertexArray, 0, vertexCount, SetDataOptions.Discard);
                gd.SetVertexBuffer(vertexBuffer);
                gd.DepthStencilState = DepthStencilState.DepthRead;

                foreach (var key in multiDrawGroupOrder)
                {
                    var group = multiDrawGroups[key];
                    if (group.RangeCount == 0) continue;

                    gd.BlendState = group.BlendMode switch
                    {
                        ParticleBlendMode.Additive => blendAdditive,
                        ParticleBlendMode.Alpha => blendNonPremultiplied,
                        _ => blendAlpha,
                    };

                    particleEffect.Param("World").SetValue(RenderEngine.WorldMatrix);
                    particleEffect.Param("View").SetValue(RenderEngine.ViewMatrix);
                    particleEffect.Param("Projection").SetValue(RenderEngine.ProjectionMatrix);
                    particleEffect.Param("MainTex").SetValue(group.Texture);
                    particleEffect.Param("AlphaClip").SetValue(false);
                    particleEffect.Param("UseVertColor").SetValue(true);

                    //particleEffect.View = RenderEngine.ViewMatrix;
                    //particleEffect.Projection = RenderEngine.ProjectionMatrix;
                    //particleEffect.World = RenderEngine.WorldMatrix;
                    //particleEffect.Texture = group.Texture;

                    particleEffect.RenderEachPass(() =>
                        gd.DrawPrimitivesMultiDraw(PrimitiveType.TriangleList, group.Starts, group.Counts, group.RangeCount));

                    group.Clear();
                }

                multiDrawGroupOrder.Clear();
                vertexCount = 0;
            }

            for (int i = 0; i < MaxSystems; i++)
            {
                if (activeSpawners[i] is null) continue;

                var spawner = activeSpawners[i];

                foreach (var subsystem in spawner.system.particleSubsystems)
                {
                    int matIdx = GlobalMapData.MaterialNameToIndex.TryGetValue(
                                           subsystem.behavior.material ?? "", out int m) ? m : 0;
                    Texture2D tex = (GlobalMapData.LoadedMaterials != null &&
                                        matIdx < GlobalMapData.LoadedMaterials.Length)
                                       ? GlobalMapData.LoadedMaterials[matIdx].Texture
                                       : RenderEngine.WhiteTexture;
                    ParticleBlendMode blendMode = subsystem.behavior.blendMode;

                    Vector3 dc = subsystem.behavior.ignoreLighting ? Vector3.One : spawner.diffuseColor;

                    var group = GetOrAddGroup(tex, blendMode);

                    for (int p = 0; p < subsystem.particles.Length; p++)
                    {
                        ref var particle = ref subsystem.particles[p];
                        if (!particle.alive) continue;

                        if (vertexCount >= vertexArray.Length - 6)
                        {
                            FlushAccumulatedToGpu();
                            group = GetOrAddGroup(tex, blendMode);
                        }

                        float perc = subsystem.behavior.lifetime > 0f
                                          ? MathHelper.Clamp(particle.life / subsystem.behavior.lifetime, 0f, 1f)
                                          : 0f;
                        float sizeScale = subsystem.behavior.sizeOverLifetime.Evaluate(perc);
                        float alpha = MathHelper.Clamp(
                                              subsystem.behavior.alphaOverLifetime.Evaluate(perc), 0f, 1f);
                        float halfSize = particle.size * sizeScale * 0.5f;

                        Color lerpedColor = Color.Lerp(particle.color, subsystem.behavior.endColor, perc);

                        byte finalR = (byte)(MathHelper.Clamp(lerpedColor.R / 255f * dc.X, 0f, 1f) * 255f);
                        byte finalG = (byte)(MathHelper.Clamp(lerpedColor.G / 255f * dc.Y, 0f, 1f) * 255f);
                        byte finalB = (byte)(MathHelper.Clamp(lerpedColor.B / 255f * dc.Z, 0f, 1f) * 255f);
                        byte finalA = (byte)(alpha * 255f);
                        Color vertexColor = new Color(finalR, finalG, finalB, finalA);

                        float u0, u1, v0uv, v1uv;
                        bool isSheet = subsystem.behavior.spriteSheetColumns > 0
                                       && subsystem.behavior.spriteSheetRows > 0
                                       && subsystem.behavior.spriteSheetTotalFrames > 0
                                       && subsystem.behavior.spriteFramesPerSecond > 0f;
                        if (isSheet)
                        {
                            int frame = (int)(particle.frameTime * subsystem.behavior.spriteFramesPerSecond)
                                        % subsystem.behavior.spriteSheetTotalFrames;
                            int col = frame % subsystem.behavior.spriteSheetColumns;
                            int row = frame / subsystem.behavior.spriteSheetColumns;
                            float uSz = 1f / subsystem.behavior.spriteSheetColumns;
                            float vSz = 1f / subsystem.behavior.spriteSheetRows;
                            u0 = col * uSz; u1 = u0 + uSz;
                            v0uv = row * vSz; v1uv = v0uv + vSz;
                        }
                        else
                        {
                            u0 = 0f; u1 = 1f; v0uv = 0f; v1uv = 1f;
                        }

                        Vector3 right, up;

                        switch (particle.renderType)
                        {
                            default:
                            case ParticleRenderType.FaceCamera:
                                {
                                    float rotRad = MathHelper.ToRadians(particle.rotation);
                                    float cosR = MathF.Cos(rotRad);
                                    float sinR = MathF.Sin(rotRad);
                                    right = (camRight * cosR - camUp * sinR) * halfSize;
                                    up = (camRight * sinR + camUp * cosR) * halfSize;
                                    break;
                                }
                            case ParticleRenderType.FaceVelocity:
                                {
                                    if (particle.velocity.LengthSquared() < 1e-6f)
                                        goto default;
                                    var vel = Vector3.Normalize(particle.velocity);
                                    var xform = Matrix.CreateConstrainedBillboard(
                                        particle.position, RenderEngine.CameraPosition,
                                        vel, RenderEngine.CameraForward, vel);
                                    right = xform.Right * halfSize;
                                    up = xform.Up * halfSize;
                                    break;
                                }

                            case ParticleRenderType.FaceX:
                                {
                                    var xform = Matrix.CreateTranslation(Vector3.UnitZ * 0.5f)
                                        * Matrix.CreateRotationZ(MathHelper.ToRadians(particle.rotation))
                                        * Matrix.CreateLookAt(Vector3.Zero, Vector3.UnitX, Vector3.UnitY)
                                        * Matrix.CreateTranslation(particle.position);
                                    right = xform.Right * halfSize;
                                    up = xform.Up * halfSize;
                                    break;
                                }
                            case ParticleRenderType.FaceY:
                                {
                                    var xform = Matrix.CreateTranslation(Vector3.UnitZ * 0.5f)
                                        * Matrix.CreateRotationZ(MathHelper.ToRadians(particle.rotation))
                                        * Matrix.CreateLookAt(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ)
                                        * Matrix.CreateTranslation(particle.position);
                                    right = xform.Right * halfSize;
                                    up = xform.Up * halfSize;
                                    break;
                                }
                            case ParticleRenderType.FaceZ:
                                {
                                    var xform = Matrix.CreateTranslation(Vector3.UnitZ * 0.5f)
                                        * Matrix.CreateRotationZ(MathHelper.ToRadians(particle.rotation))
                                        * Matrix.CreateLookAt(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY)
                                        * Matrix.CreateTranslation(particle.position);
                                    right = xform.Right * halfSize;
                                    up = xform.Up * halfSize;
                                    break;
                                }
                        }

                        Vector3 vbl = particle.position - right - up;
                        Vector3 vtl = particle.position - right + up;
                        Vector3 vbr = particle.position + right - up;
                        Vector3 vtr = particle.position + right + up;

                        int idx = vertexCount;
                        vertexArray[idx + 0] = new VertexPositionColorTexture(vbl, vertexColor, new Vector2(u1, v1uv));
                        vertexArray[idx + 1] = new VertexPositionColorTexture(vtl, vertexColor, new Vector2(u1, v0uv));
                        vertexArray[idx + 2] = new VertexPositionColorTexture(vbr, vertexColor, new Vector2(u0, v1uv));
                        vertexArray[idx + 3] = new VertexPositionColorTexture(vbr, vertexColor, new Vector2(u0, v1uv));
                        vertexArray[idx + 4] = new VertexPositionColorTexture(vtl, vertexColor, new Vector2(u1, v0uv));
                        vertexArray[idx + 5] = new VertexPositionColorTexture(vtr, vertexColor, new Vector2(u0, v0uv));

                        group.AddOrExtend(vertexCount, 6);

                        vertexCount += 6;
                    }
                }
            }

            FlushAccumulatedToGpu();

            gd.BlendState = BlendState.NonPremultiplied;
            gd.DepthStencilState = DepthStencilState.Default;
        }
    }

    public class ParticleSpawner
    {
        public ParticleSystem system;
        internal Vector3 diffuseColor;

        public ParticleSpawner(Vector3 position, string filePath, Matrix? spawnMatrix = null)
            : this(position, JsonConvert.DeserializeObject<ParticleSystemBehavior>(
                                 File.ReadAllText(filePath)), spawnMatrix)
        { }

        public ParticleSpawner(Vector3 position, ParticleSystemBehavior behavior, Matrix? spawnMatrix = null)
        {
            system = new ParticleSystem(behavior);
            if (spawnMatrix.HasValue) system.particleSpawnTransformation = spawnMatrix.Value;
            system.position = position;

            var bundle = LightNodeTraversal.GetClosestNodeBundle(position);
            if (bundle == null) return;

            var node = bundle.Traverse(position);
            var data = node.Data;
            if (data == null) return;

            var sun = MainEngine.ActiveStaticLights.Find(l => l.Type == Light.LightType.Directional);
            bool inDir = !Array.Find(data, v => v.LightNum == sun.ID).LightBlocked;

            Vector3 color = Vector3.Zero;
            for (int i = 0; i < 9; i++)
                color += node.IndirectCoefficients[i] * (1f / 9f);

            foreach (var d in data)
            {
                var light = MainEngine.ActiveStaticLights[d.LightNum];
                float dst = Vector3.Distance(position, light.Position);
                if (light.Range < dst) continue;
                color += light.Color.ToVector3() * light.Intensity * (1f - dst / light.Range);
            }

            if (inDir)
                color += MainEngine.Instance.DirectionalLightColor.ToVector3()
                         * MainEngine.Instance.DirectionalLightStrength;

            diffuseColor = color;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update() => system.Update(MainEngine.PreviousFrameDelta);

        public void UpdateCollision()
        {
            float bounce = system.behavior.bounce;
            float dampening = system.behavior.velocityDampening;
            float dt = MainEngine.PreviousFrameDelta;

            foreach (var subsystem in system.particleSubsystems)
            {
                for (int i = 0; i < subsystem.particles.Length; i++)
                {
                    ref var p = ref subsystem.particles[i];
                    if (!p.alive) continue;

                    var hit = BSPRoot.TraceRay(
                        new Ray(system.position,
                                Vector3.Normalize(p.position - system.position)),
                        Vector3.Distance(p.position, system.position));

                    if (!hit.Hit) continue;

                    float speed = p.velocity.LengthSquared();
                    if (speed < 1e-6f) continue;

                    float drop = speed * dampening * dt;
                    Vector3 dampened = CMath.ProjectOnPlane(
                        p.velocity * (MathF.Max(speed - drop, 0f) / speed),
                        hit.Normal);

                    p.velocity = hit.Normal * Vector3.Dot(p.velocity, -hit.Normal) * bounce
                                  + dampened;
                    p.position += hit.Normal * 0.01f;
                }
            }
        }
    }
}