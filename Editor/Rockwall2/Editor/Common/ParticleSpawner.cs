using Relic.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using Rockwall;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Common;

public static class ParticleManager
{
    const int MaxSystems = 512;
    static ParticleSpawner?[] activeSpawners = new ParticleSpawner[MaxSystems];
    static int systemIndex = -1;

    static List<(ParticleSpawner spawner, ParticleSubsystemBehavior subBehavior, int material, float depth, Particle p)> toDraw = new();

    public static int SpawnParticleSystem(Vector3 position, string fromFile, Matrix? spawnMatrix = null)
    {
        systemIndex = (systemIndex + 1) % MaxSystems;
        activeSpawners[systemIndex] = new ParticleSpawner(position, fromFile, spawnMatrix);
        activeSpawners[systemIndex]!.system.isLooping = true;
        return systemIndex;
    }

    public static int SpawnParticleSystem(Vector3 position, ParticleSystemBehavior behaviour, Matrix? spawnMatrix = null)
    {
        systemIndex = (systemIndex + 1) % MaxSystems;
        activeSpawners[systemIndex] = new ParticleSpawner(position, behaviour, spawnMatrix);
        activeSpawners[systemIndex]!.system.isLooping = true;
        return systemIndex;
    }

    public static void Clear()
    {
        for (int i = 0; i < MaxSystems; i++)
            activeSpawners[i] = null;
    }

    public static ParticleSpawner? GetSpawner(int index) => activeSpawners[index];

    public static void UpdateSystems(float dt)
    {
        Parallel.For(0, MaxSystems, i =>
        {
            if (activeSpawners[i] is null) return;
            if (activeSpawners[i]!.system.isFinished && !activeSpawners[i]!.system.isLooping)
            {
                activeSpawners[i] = null;
                return;
            }
            activeSpawners[i]!.Update(dt);
        });
    }

    public static void RenderSystems(Vector3 cameraPos, Matrix world, Matrix view, Matrix projection, float dt)
    {
        toDraw.Clear();

        for (int i = 0; i < MaxSystems; i++)
        {
            if (activeSpawners[i] is null) continue;
            foreach (var entry in activeSpawners[i]!.CollectVisibleParticles(world,view,projection,dt))
                toDraw.Add((activeSpawners[i]!, entry.subBehavior, entry.material, 0f, entry.particle));
        }

        Vector3 camFwd = Matrix.Invert(view).Forward;
        for (int i = 0; i < toDraw.Count; i++)
        {
            var item = toDraw[i];
            float depth = Vector3.Dot(item.p.position - cameraPos, camFwd)
                          - item.p.size * 0.5f;
            toDraw[i] = (item.spawner, item.subBehavior, item.material, depth, item.p);
        }
        toDraw.Sort((a, b) => b.depth.CompareTo(a.depth));

        var gd = EditorHost.Instance.GraphicsDevice;
        gd.DepthStencilState = DepthStencilState.DepthRead;

        foreach (var item in toDraw)
        {
            // Set blend state per subsystem (additive fire, alpha smoke, etc.)
            gd.BlendState = item.subBehavior.blendMode switch
            {
                ParticleBlendMode.Additive => BlendState.Additive,
                ParticleBlendMode.Alpha => BlendState.NonPremultiplied,
                _ => BlendState.AlphaBlend
            };
            item.spawner.DrawParticle(item.subBehavior, item.material, item.p, world,view,projection);
        }

        gd.BlendState = BlendState.Opaque;
        gd.DepthStencilState = DepthStencilState.Default;
    }
}

public class ParticleSpawner
{
    public ParticleSystem system;
    private BasicEffect effect;

    private readonly VertexPositionColorTexture[] billboard = new VertexPositionColorTexture[6]
    {
        new(new Vector3(-0.5f,-0.5f,-0.5f), Color.White, new Vector2(1, 1)),
        new(new Vector3(-0.5f, 0.5f,-0.5f), Color.White, new Vector2(1, 0)),
        new(new Vector3( 0.5f,-0.5f,-0.5f), Color.White, new Vector2(0, 1)),
        new(new Vector3( 0.5f,-0.5f,-0.5f), Color.White, new Vector2(0, 1)),
        new(new Vector3(-0.5f, 0.5f,-0.5f), Color.White, new Vector2(1, 0)),
        new(new Vector3( 0.5f, 0.5f,-0.5f), Color.White, new Vector2(0, 0)),
    };

    public ParticleSpawner(Vector3 position, string filePath, Matrix? spawnMatrix = null)
    : this(position, JsonConvert.DeserializeObject<ParticleSystemBehavior>(
        File.ReadAllText(filePath),
        new JsonSerializerSettings { Converters = { new ColorJsonConverter() } })!, spawnMatrix)
    { }

    public ParticleSpawner(Vector3 position, ParticleSystemBehavior behaviour, Matrix? spawnMatrix = null)
    {
        system = new ParticleSystem(behaviour);
        if (spawnMatrix.HasValue) system.particleSpawnTransformation = spawnMatrix.Value;
        system.position = position;

        effect = new BasicEffect(EditorHost.Instance.GraphicsDevice)
        {
            TextureEnabled = true,
            VertexColorEnabled = true,
            LightingEnabled = false
        };
    }

    public void Update(float dt) => system.Update(dt);

    public record struct ParticleEntry(ParticleSubsystemBehavior subBehavior, int material, Particle particle);

    public List<ParticleEntry> CollectVisibleParticles(Matrix world, Matrix view, Matrix projection, float dt)
    {
        effect.View = view;
        effect.Projection = projection;

        var result = new List<ParticleEntry>();
        object addLock = new();

        Parallel.For(0, system.particleSubsystems.Length, v =>
        {
            if (!(ParticleEditor.Instance?.IsSubsystemActive(v) ?? true)) return;
            var subsystem = system.particleSubsystems[v];

            int matIdx = GlobalMapData.MaterialNameToIndex.TryGetValue(
                             subsystem.behavior.material ?? "", out int m) ? m : 0;

            for (int i = 0; i < subsystem.particles.Length; i++)
            {
                ref Particle p = ref subsystem.particles[i];
                if (!p.alive) continue;

                if (system.behavior.collidesWithWorld && p.position.Y < 0)
                {
                    float speed = p.velocity.LengthSquared();
                    if (speed > 0)
                    {
                        float drop = speed * system.behavior.velocityDampening * dt;
                        Vector3 dampened = p.velocity * (MathF.Max(speed - drop, 0f) / speed);
                        p.velocity = Vector3.Up * Vector3.Dot(p.velocity, -Vector3.Up)
                                                 * system.behavior.bounce + dampened;
                    }
                    p.position.Y = 0;
                }

                lock (addLock)
                    result.Add(new ParticleEntry(subsystem.behavior, matIdx, p));
            }
        });

        return result;
    }

    public void DrawParticle(ParticleSubsystemBehavior subBehavior, int materialIdx, Particle particle, Matrix world, Matrix view, Matrix projection)
    {
        float perc = subBehavior.lifetime > 0f
                          ? MathHelper.Clamp(particle.life / subBehavior.lifetime, 0f, 1f)
                          : 0f;
        float sizeScale = subBehavior.sizeOverLifetime.Evaluate(perc);
        float alpha = MathHelper.Clamp(subBehavior.alphaOverLifetime.Evaluate(perc), 0f, 1f);
        float effSize = particle.size * sizeScale;

        Color baseColor = Color.Lerp(particle.color, subBehavior.endColor, perc);
        baseColor.A = (byte)(alpha * 255f);

        for (int i = 0; i < billboard.Length; i++)
            billboard[i].Color = baseColor;

        bool isSheet = subBehavior.spriteSheetColumns > 0
                       && subBehavior.spriteSheetRows > 0
                       && subBehavior.spriteSheetTotalFrames > 0
                       && subBehavior.spriteFramesPerSecond > 0f;
        if (isSheet)
        {
            int frame = (int)(particle.frameTime * subBehavior.spriteFramesPerSecond)
                         % subBehavior.spriteSheetTotalFrames;
            int col = frame % subBehavior.spriteSheetColumns;
            int row = frame / subBehavior.spriteSheetColumns;
            float uSz = 1f / subBehavior.spriteSheetColumns;
            float vSz = 1f / subBehavior.spriteSheetRows;
            float u0 = col * uSz, u1 = u0 + uSz;
            float v0 = row * vSz, v1 = v0 + vSz;

            billboard[0].TextureCoordinate = new Vector2(u1, v1);
            billboard[1].TextureCoordinate = new Vector2(u1, v0);
            billboard[2].TextureCoordinate = new Vector2(u0, v1);
            billboard[3].TextureCoordinate = new Vector2(u0, v1);
            billboard[4].TextureCoordinate = new Vector2(u1, v0);
            billboard[5].TextureCoordinate = new Vector2(u0, v0);
        }
        else
        {
            billboard[0].TextureCoordinate = new Vector2(1, 1);
            billboard[1].TextureCoordinate = new Vector2(1, 0);
            billboard[2].TextureCoordinate = new Vector2(0, 1);
            billboard[3].TextureCoordinate = new Vector2(0, 1);
            billboard[4].TextureCoordinate = new Vector2(1, 0);
            billboard[5].TextureCoordinate = new Vector2(0, 0);
        }

        effect.Texture = (GlobalMapData.LoadedMaterials != null &&
                          materialIdx < GlobalMapData.LoadedMaterials.Length)
            ? GlobalMapData.LoadedMaterials[materialIdx].Texture
            : null;

        Matrix invView = Matrix.Invert(view);
        Vector3 camFwd = invView.Forward;
        Vector3 camPos = invView.Translation;
        Matrix transform;

        switch (particle.renderType)
        {
            case ParticleRenderType.FaceCamera:
            default:
                transform = Matrix.CreateScale(effSize)
                    * Matrix.CreateRotationZ(MathHelper.ToRadians(particle.rotation))
                    * Matrix.CreateRotationY(MathHelper.ToRadians(180f))
                    * Matrix.CreateWorld(particle.position, camFwd, Vector3.Up);
                break;

            case ParticleRenderType.FaceVelocity:
                // Fall back to face-camera when the particle is nearly stationary.
                if (particle.velocity.LengthSquared() < 1e-6f)
                    goto default;

                Vector3 axis = Vector3.Normalize(particle.velocity);
                transform = Matrix.CreateScale(effSize)
                    * Matrix.CreateConstrainedBillboard(
                        particle.position, camPos, axis, camFwd, axis);
                break;
            case ParticleRenderType.FaceX:
                transform = Matrix.CreateTranslation(Vector3.UnitZ*0.5f)
                    * Matrix.CreateRotationZ(MathHelper.ToRadians(particle.rotation))
                    * Matrix.CreateScale(effSize)
                    * Matrix.CreateLookAt(Vector3.Zero,Vector3.UnitX,Vector3.UnitY)
                    * Matrix.CreateTranslation(particle.position);
                break;
            case ParticleRenderType.FaceY:
                transform = Matrix.CreateTranslation(Vector3.UnitZ * 0.5f)
                    * Matrix.CreateRotationZ(MathHelper.ToRadians(particle.rotation))
                    * Matrix.CreateScale(effSize)
                    * Matrix.CreateLookAt(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ)
                    * Matrix.CreateTranslation(particle.position);
                break;
            case ParticleRenderType.FaceZ:
                transform = Matrix.CreateTranslation(Vector3.UnitZ * 0.5f)
                    * Matrix.CreateRotationZ(MathHelper.ToRadians(particle.rotation))
                    * Matrix.CreateScale(effSize)
                    * Matrix.CreateLookAt(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY)
                    * Matrix.CreateTranslation(particle.position);
                break;
        }

        effect.World = transform * world;

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            EditorHost.Instance.GraphicsDevice
                .DrawUserPrimitives(PrimitiveType.TriangleList, billboard, 0, 2);
        }
    }
}