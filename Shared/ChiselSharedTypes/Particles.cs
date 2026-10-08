using Chisel.Models;
using Chisel.Utils;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;

namespace Chisel.Particles;

public enum ParticleRenderType
{
    FaceCamera,
    FaceVelocity,
    FaceX,
    FaceY,
    FaceZ
}

public enum ParticleBlendMode
{
    Alpha,
    Additive,
}

// Three-keyframe curve evaluated over a normalised [0..1] lifetime.
// start = value at birth, mid = value at 50% life, end = value at death.
public struct Curve3
{
    public float start, mid, end;

    public float Evaluate(float t)
    {
        if (t <= 0.5f)
            return MathHelper.Lerp(start, mid, t * 2f);
        return MathHelper.Lerp(mid, end, (t - 0.5f) * 2f);
    }
}

public struct Particle
{
    public Vector3 position, velocity;
    public float size, life, rotation, angularVelocity;
    public Color color;      // resolved from startColor range at spawn
    public float frameTime;  // running clock for sprite-sheet frame selection
    public ParticleRenderType renderType;
    public bool alive;
}

[System.Serializable]
public class ParticleSubsystemBehavior
{
    public enum EmissionType
    {
        Impulse,
        Constant
    }

    [EditorField("Emission type", Group = "Emission", Order = 0)]
    public EmissionType emissionType;
    [EditorField("Render type", Group = "Emission", Order = 1)]
    public ParticleRenderType renderType;
    [EditorField("Blend mode", Group = "Emission", Order = 2)]
    public ParticleBlendMode blendMode;

    // Per-particle lifetime (seconds a single particle lives).
    [EditorField("Particle lifetime", Group = "Emission", Order = 3)]
    public float lifetime;

    // Seconds into the parent system's lifetime before this subsystem
    // begins emitting. Allows sequenced, staggered effects.
    [EditorField("Start delay", Group = "Emission", Order = 4)]
    public float startDelay;

    [EditorField("Particle count", Group = "Emission", Order = 5)]
    public ushort particles;
    public string material;
    public string model;

    [EditorField("Spawn rate", Group = "Emission", Order = 6)]
    public float particleSpawnRate;

    [EditorField("Keep alive on reset", Group = "Emission", Order = 7)]
    public bool dontKillOnReset;

    [EditorField("Velocity", Group = "Motion", Order = 1)]
    public ValueRange<Vector3> startVelocity;
    [EditorField("Spawn position", Group = "Motion", Order = 2)]
    public ValueRange<Vector3> startPosition;
    [EditorField("Size", Group = "Size & Rotation", Order = 0)]
    public ValueRange<float> startSize;
    [EditorField("Angular velocity", Group = "Size & Rotation", Order = 1)]
    public ValueRange<float> angularVelocity;
    [EditorField("Start frame", Group = "Sprite Sheet", Order = 4)]
    public ValueRange<float> startFrame;

    // Each particle picks a random color inside [startColor.min, startColor.max]
    // at spawn and then lerps toward endColor over its lifetime.
    [EditorField("Start color", Group = "Color", Order = 0)]
    public ValueRange<Color> startColor;
    [EditorField("End color", Group = "Color", Order = 1)]
    public Color endColor;

    // Multiplier curves evaluated each frame via perc = life / lifetime.
    [EditorField("Size multiplier", Group = "Over Lifetime", Order = 0, Max = 4f)]
    public Curve3 sizeOverLifetime;
    [EditorField("Alpha", Group = "Over Lifetime", Order = 1, Max = 1f)]
    public Curve3 alphaOverLifetime;
    [EditorField("Full-bright", Group = "Color", Order = 2)]
    public bool ignoreLighting;

    [EditorField("Gravity", Group = "Motion", Order = 0)]
    public float gravity;

    // Sprite-sheet animation.
    // Set spriteSheetColumns > 0 to activate; leave at 0 for a static quad.
    [EditorField("Columns", Group = "Sprite Sheet", Order = 0)]
    public int spriteSheetColumns;
    [EditorField("Rows", Group = "Sprite Sheet", Order = 1)]
    public int spriteSheetRows;
    [EditorField("Total frames", Group = "Sprite Sheet", Order = 2)]
    public int spriteSheetTotalFrames;
    [EditorField("FPS", Group = "Sprite Sheet", Order = 3)]
    public float spriteFramesPerSecond;
    [EditorField("Loop", Group = "Sprite Sheet", Order = 5)]
    public bool spriteLoopFrames;

    public float spriteMaxTime { get; private set; }

    public ParticleSubsystemBehavior()
    {
        startColor = new ValueRange<Color> { min = Color.White, max = Color.White };
        endColor = Color.White;
        sizeOverLifetime = new Curve3 { start = 1f, mid = 1f, end = 1f };
        alphaOverLifetime = new Curve3 { start = 1f, mid = 1f, end = 0f };
        particles = 10;
        lifetime = 1f;
    }
    public void Init()
    {
        spriteMaxTime = (spriteSheetTotalFrames - 1) / spriteFramesPerSecond;
    }
}

[System.Serializable]
public class ParticleSystemBehavior
{
    public ParticleSubsystemBehavior[] subsystemBehaviors;
    public float lifetime;
    public bool collidesWithWorld;
    public float bounce, velocityDampening;
}

public class ParticleSubsystem
{
    public ParticleSubsystemBehavior behavior;
    public ParticleSystem parent;

    private float particleTime;
    private float totalTime;
    public Particle[] particles;

    private int currentParticleIndex;
    private int targetMaxParticles;

    public ParticleSubsystem(ParticleSubsystemBehavior behavior)
    {
        this.behavior = behavior;
        targetMaxParticles = behavior.particles * (behavior.dontKillOnReset ? 2 : 1);
        particles = new Particle[targetMaxParticles]; // leave enough room for past particles, if set
        behavior.Init();
    }

    public void Reset()
    {
        if(!behavior.dontKillOnReset)
        {
            particles = new Particle[targetMaxParticles];

            currentParticleIndex = 0;
        }
        else 
        {
            if (targetMaxParticles != particles.Length)
                Array.Resize(ref particles, targetMaxParticles);

            currentParticleIndex += behavior.particles;
            currentParticleIndex %= targetMaxParticles;
        }

        totalTime = 0f;
        particleTime = 0f;
        behavior.Init();
    }

    private static Vector3 GetFromRange(ValueRange<Vector3> r)
    {
        return new Vector3(
            MathHelper.Lerp(r.min.X, r.max.X, (float)Random.Shared.NextDouble()),
            MathHelper.Lerp(r.min.Y, r.max.Y, (float)Random.Shared.NextDouble()),
            MathHelper.Lerp(r.min.Z, r.max.Z, (float)Random.Shared.NextDouble())
        );
    }

    private static float GetFromRange(ValueRange<float> r)
    {
        return MathHelper.Lerp(r.min, r.max, (float)Random.Shared.NextDouble());
    }

    private static Color GetFromRange(ValueRange<Color> r)
    {
        float t = (float)Random.Shared.NextDouble();
        return new Color(
            (byte)MathHelper.Lerp(r.min.R, r.max.R, t),
            (byte)MathHelper.Lerp(r.min.G, r.max.G, t),
            (byte)MathHelper.Lerp(r.min.B, r.max.B, t),
            (byte)MathHelper.Lerp(r.min.A, r.max.A, t)
        );
    }

    private void SpawnParticle(ref Particle p)
    {
        p.position = Vector3.Transform(GetFromRange(behavior.startPosition), parent.particleSpawnTransformation) + parent.position;
        p.velocity = Vector3.Transform(GetFromRange(behavior.startVelocity), parent.particleSpawnTransformation);
        p.size = GetFromRange(behavior.startSize);
        p.angularVelocity = GetFromRange(behavior.angularVelocity);
        p.color = GetFromRange(behavior.startColor);
        p.life = 0f;
        p.rotation = 0f;
        p.renderType = behavior.renderType;
        p.alive = true;

        // Optionally scatter the starting frame so a burst doesn't look
        // like every particle is in sync.
        p.frameTime = behavior.spriteFramesPerSecond > 0f ? GetFromRange(behavior.startFrame) / behavior.spriteFramesPerSecond : 0f;
    }

    public void Update(float deltaTime)
    {
        totalTime += deltaTime;

        // Gate emission behind startDelay.
        if (totalTime >= behavior.startDelay)
        {
            switch (behavior.emissionType)
            {
                case ParticleSubsystemBehavior.EmissionType.Impulse:
                    if (particleTime >= 0f)
                    {
                        for (int i = 0; i < behavior.particles; i++)
                            SpawnParticle(ref particles[i + currentParticleIndex]);
                    }
                    break;

                case ParticleSubsystemBehavior.EmissionType.Constant:
                    if (particleTime <= 0f)
                    {
                        particleTime = behavior.particleSpawnRate;
                        SpawnParticle(ref particles[currentParticleIndex]);
                        currentParticleIndex = (currentParticleIndex + 1) % targetMaxParticles;
                    }
                    break;
            }

            // Decrement shared timer. For Impulse this drives particleTime
            // negative after the first frame and keeps it there.
            particleTime -= deltaTime;
        }

        // Simulate all live particles.
        for (int i = 0; i < particles.Length; i++)
        {
            ref Particle p = ref particles[i];
            if (!p.alive) continue;

            p.position += p.velocity * deltaTime;
            p.velocity.Y += behavior.gravity * deltaTime;
            p.life += deltaTime;
            p.frameTime += deltaTime;

            if (!behavior.spriteLoopFrames) p.frameTime = float.Min(p.frameTime, behavior.spriteMaxTime);

            p.rotation += (p.velocity * new Vector3(1f, 0f, 1f)).Length()
                            * p.angularVelocity * deltaTime * 10f;

            if (p.life >= behavior.lifetime)
                p.alive = false;
        }
    }
}

public class ParticleSystem
{
    public ParticleSubsystem[] particleSubsystems;

    public ParticleSystemBehavior behavior;

    public float life { get; private set; } = 0f;

    public bool isFinished => life > behavior.lifetime && behavior.lifetime > 0f;
    public bool isLooping;

    public Vector3 position;
    public Matrix particleSpawnTransformation = Matrix.Identity;

    public ParticleSystem(ParticleSystemBehavior behaviour)
    {
        this.behavior = behaviour;
        particleSubsystems = new ParticleSubsystem[behaviour.subsystemBehaviors.Length];

        for (int i = 0; i < particleSubsystems.Length; i++)
        {
            particleSubsystems[i] = new ParticleSubsystem(behaviour.subsystemBehaviors[i]);
            particleSubsystems[i].parent = this;
        }
    }

    public void Update(float deltaTime)
    {
        life += deltaTime;

        for (int i = 0; i < particleSubsystems.Length; i++)
            particleSubsystems[i].Update(deltaTime);

        if (isFinished && isLooping)
        {
            life = 0f;
            for (int i = 0; i < particleSubsystems.Length; i++)
                particleSubsystems[i].Reset();
        }
    }

    public void Reset()
    {
        for (int i = 0; i < particleSubsystems.Length; i++)
            particleSubsystems[i].Reset();

        life = 0;
    }
}

public static class ParticleJson
{
    public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
    {
        Converters = { new ColorJsonConverter() }
    };
}

public class ColorJsonConverter : JsonConverter<Color>
{
    public override void WriteJson(JsonWriter writer, Color value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("R");
        writer.WriteValue(value.R);
        writer.WritePropertyName("G");
        writer.WriteValue(value.G);
        writer.WritePropertyName("B");
        writer.WriteValue(value.B);
        writer.WritePropertyName("A");
        writer.WriteValue(value.A);
        writer.WriteEndObject();
    }

    public override Color ReadJson(JsonReader reader, Type objectType, Color existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.StartObject)
        {
            JObject obj = JObject.Load(reader);
            byte r = obj["R"]?.Value<byte>() ?? (byte)255;
            byte g = obj["G"]?.Value<byte>() ?? (byte)255;
            byte b = obj["B"]?.Value<byte>() ?? (byte)255;
            byte a = obj["A"]?.Value<byte>() ?? (byte)255;
            return new Color(r, g, b, a);
        }

        if (reader.TokenType == JsonToken.String)
        {
            string hex = (string)reader.Value;
            return ColorFromHexOrName(hex);
        }

        return Color.White;
    }

    private static Color ColorFromHexOrName(string value)
    {
        if (value.StartsWith("#"))
        {
            value = value.Substring(1);
            byte r = Convert.ToByte(value.Substring(0, 2), 16);
            byte g = Convert.ToByte(value.Substring(2, 2), 16);
            byte b = Convert.ToByte(value.Substring(4, 2), 16);
            byte a = value.Length >= 8 ? Convert.ToByte(value.Substring(6, 2), 16) : (byte)255;
            return new Color(r, g, b, a);
        }

        return Color.White;
    }
}