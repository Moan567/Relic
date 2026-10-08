using Chisel.Models.Data;
using Chisel.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Chisel.Models.Morph;

public class CMorphKeyframe
{
    public float Time { get; set; }
    public float Weight { get; set; }
}
public class CMorphTrack
{
    public string Name { get; set; }
    public List<CMorphKeyframe> Keyframes { get; set; } = new();

    public float Evaluate(float time)
    {
        if (Keyframes.Count == 0) return 0f;
        if (time <= Keyframes[0].Time) return Keyframes[0].Weight;
        if (time >= Keyframes[^1].Time) return Keyframes[^1].Weight;

        for (int i = 0; i < Keyframes.Count - 1; i++)
        {
            if (time < Keyframes[i + 1].Time)
            {
                float t = (time - Keyframes[i].Time)
                        / (Keyframes[i + 1].Time - Keyframes[i].Time);
                return MathHelper.SmoothStep(Keyframes[i].Weight, Keyframes[i + 1].Weight, t);
            }
        }
        return 0f;
    }
}
public class CMorphAnimData
{
    public int StartFrame { get; set; } = 0;
    public int EndFrame { get; set; } = 300;
    public string AudioPath { get; set; }
    public List<CMorphTrack> Tracks { get; set; } = new();

    public static void WriteToFile(CMorphAnimData data, string filePath)
    {
        using var fstream = File.OpenWrite(filePath);
        using var writer = new BinaryWriter(fstream);

        writer.Write(data.StartFrame);
        writer.Write(data.EndFrame);
        writer.Write(data.AudioPath);

        writer.Write(data.Tracks.Count);
        foreach(var track in data.Tracks)
        {
            writer.Write(track.Name);
            writer.Write(track.Keyframes.Count);
        
            foreach(var key in track.Keyframes)
            {
                writer.Write(key.Time);
                writer.Write(key.Weight);
            }
        }
    }
    public static string ResolveAudioPath(string morphFile, string audioPath)
    {
        audioPath = audioPath.Replace('\\', '/');
        if (Path.IsPathFullyQualified(audioPath) && File.Exists(audioPath)) return audioPath;

        var dir = Path.GetDirectoryName(morphFile);
        var segments = audioPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        int ups = 0;
        while (ups < segments.Length && segments[ups] == "..") ups++;

        // Somehow the editor can read the file itself as a directory, which means
        // it can somehow save files with one too many "../"
        for (int drop = 0; drop <= ups; drop++)
        {
            var candidate = Path.GetFullPath(Path.Combine(dir, string.Join('/', segments.Skip(drop))));
            if (File.Exists(candidate)) return candidate;
        }

        return Path.GetFullPath(Path.Combine(dir, audioPath));
    }
    public static CMorphAnimData LoadFromFile(string filePath)
    {
        using var fstream = File.OpenRead(filePath);
        using var reader = new BinaryReader(fstream);

        CMorphAnimData data = new CMorphAnimData();

        data.StartFrame = reader.ReadInt32();
        data.EndFrame = reader.ReadInt32();
        data.AudioPath = reader.ReadString();

        int trackCount = reader.ReadInt32();
        for(int i = 0; i < trackCount; i++)
        {
            var track = new CMorphTrack();

            track.Name = reader.ReadString();
            var keyframeCount = reader.ReadInt32();

            for(int j = 0; j < keyframeCount; j++)
            {
                track.Keyframes.Add(new CMorphKeyframe
                {
                    Time = reader.ReadSingle(),
                    Weight = reader.ReadSingle()
                });
            }

            data.Tracks.Add(track);
        }

        return data;
    }
}
public class CMorphAnimator(CMorphState state)
{
    private CMorphState state = state;

    private record ActiveAnim(CMorphAnimData Data, float Start, float End, float StartTime);
    private readonly List<ActiveAnim> active = new();

    private readonly HashSet<string> driven = new();
    private readonly Dictionary<string, float> workbuf = new();

    public void PlayAnimation(CMorphAnimData data)
    {
        float start = data.StartFrame / 30f;
        float end = data.EndFrame / 30f;
        active.Add(new ActiveAnim(data, start, end, start));
    }

    public void Update(float dt)
    {
        if (active.Count == 0 && driven.Count == 0) return;

        workbuf.Clear();

        for (int i = active.Count - 1; i >= 0; i--)
        {
            var anim = active[i];
            float t = anim.Start + dt;

            foreach (var track in anim.Data.Tracks)
            {
                workbuf.TryGetValue(track.Name, out float sum);
                workbuf[track.Name] = sum + track.Evaluate(t);
            }

            if (t >= anim.End) active.RemoveAt(i);
            else active[i] = anim with { Start = t };
        }

        foreach (var name in driven)
            if (!workbuf.ContainsKey(name)) state.SetWeight(name, 0f);

        driven.Clear();
        foreach (var (name, w) in workbuf)
        {
            state.SetWeight(name, w);
            driven.Add(name);
        }
    }

    public void StopAll() => active.Clear();
    public bool IsPlaying => active.Count > 0;
}
public class CMorphState
{
    private string[] names = Array.Empty<string>();
    private float[] weights = Array.Empty<float>();
    private Dictionary<string, int> lookup = new();
    private bool dirty = false;
    public bool IsDirty => dirty;
    public void ClearDirty() => dirty = false;
    public void Initialize(CModel model)
        => Initialize(model.Bodygroups
            .Where(bg => bg.MorphTargets?.Count > 0)
            .SelectMany(bg => bg.MorphTargets.Select(m => m.Name))
            .Distinct());

    public void Initialize(CModel.CBodyGroup bodygroup)
        => Initialize(bodygroup.MorphTargets?.Select(m => m.Name)
            ?? Enumerable.Empty<string>());
    public void Initialize(IEnumerable<string> morphNames)
    {
        names = morphNames.ToArray();
        weights = new float[names.Length];
        lookup = new Dictionary<string, int>(names.Length);
        for (int i = 0; i < names.Length; i++)
            lookup[names[i]] = i;
        dirty = true;
    }

    public void SetWeight(string morphName, float weight)
    {
        if (!lookup.TryGetValue(morphName, out int i)) return;
        float clamped = Math.Clamp(weight, 0f, 1f);
        weights[i] = clamped;
        dirty = true;
    }

    public float GetWeight(string morphName)
        => lookup.TryGetValue(morphName, out int i) ? weights[i] : 0f;

    // Expose the raw array so CMorphApplicator can iterate without any lookups
    public ReadOnlySpan<float> Weights => weights;
    public ReadOnlySpan<string> Names => names;

    public bool HasAnyActive => weights.Any(w => w > 0f);
}
public class CMorphApplicator : IDisposable
{
    private CSkinnedVertex[] workBuffer;
    private DynamicVertexBuffer dynamicBuffer;
    private readonly GraphicsDevice graphicsDevice;
    private int capacity;
    public CMorphApplicator(GraphicsDevice gd, CSkinnedVertex[] baseVertices)
    {
        graphicsDevice = gd;
        Apply(baseVertices, new List<CMorphTarget>(), new CMorphState());
    }
    private void EnsureCapacity(int vertexCount)
    {
        if (dynamicBuffer != null && capacity == vertexCount) return;

        dynamicBuffer?.Dispose();
        workBuffer = new CSkinnedVertex[vertexCount];
        dynamicBuffer = new DynamicVertexBuffer(
            graphicsDevice, CSkinnedVertex.VertexDeclaration,
            vertexCount, BufferUsage.WriteOnly);
        capacity = vertexCount;
    }
    public void Apply(CSkinnedVertex[] baseVertices, List<CMorphTarget> targets, CMorphState state)
    {
        EnsureCapacity(baseVertices.Length);

        Array.Copy(baseVertices, workBuffer, baseVertices.Length);

        foreach (var target in targets)
        {
            float weight = state.GetWeight(target.Name);
            if (weight == 0f) continue;

            for (int i = 0; i < target.Indices.Length; i++)
            {
                int vi = target.Indices[i];
                workBuffer[vi].Position += target.DeltaPositions[i] * weight;
                workBuffer[vi].Normal += target.DeltaNormals[i] * weight;
            }
        }

        for (int i = 0; i < workBuffer.Length; i++)
        {
            float len = workBuffer[i].Normal.LengthSquared();
            if (len > 0.0001f) workBuffer[i].Normal /= MathF.Sqrt(len);
        }

        dynamicBuffer.SetData(workBuffer, 0, workBuffer.Length, SetDataOptions.Discard);
    }

    public void Draw(GraphicsDevice gd, IndexBuffer indexBuffer)
    {
        gd.SetVertexBuffer(dynamicBuffer);
        gd.Indices = indexBuffer;
        gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, indexBuffer.IndexCount / 3);
    }
    public void Dispose() => dynamicBuffer?.Dispose();
}
public static class CMorphApplicatorFactory
{
    /// <summary>
    /// Ensures every bodygroup with morph target data anywhere has exactly one CMorphApplicator.
    /// </summary>
    public static void EnsureMorphApplicators(CModel model, GraphicsDevice gd)
    {
        foreach (var bg in model.Bodygroups)
        {
            bool hasAnyMorphData = bg.MorphTargets?.Count > 0
                || bg.LODMeshes.Any(e => e?.MorphTargets?.Count > 0);

            if (!hasAnyMorphData) continue;

            bg.MorphApplicator ??= new CMorphApplicator(gd, bg.MeshData.Vertices);
        }
    }
}