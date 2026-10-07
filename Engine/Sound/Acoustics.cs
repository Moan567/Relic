using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Sound;
public struct AcousticPreset
{
    public string Name;
    public float ReferenceSize;
    public float DecayTime;
    public float Diffusion;
    public float GainHF;
    public float WetSend;

    public static AcousticPreset Lerp(AcousticPreset a, AcousticPreset b, float t)
    {
        return new AcousticPreset
        {
            ReferenceSize = MathHelper.Lerp(a.ReferenceSize, b.ReferenceSize, t),
            DecayTime = MathHelper.Lerp(a.DecayTime, b.DecayTime, t),
            Diffusion = MathHelper.Lerp(a.Diffusion, b.Diffusion, t),
            GainHF = MathHelper.Lerp(a.GainHF, b.GainHF, t),
            WetSend = MathHelper.Lerp(a.WetSend, b.WetSend, t)
        };
    }
}

public static class AcousticPresets
{
    public static readonly AcousticPreset[] Sizes = new AcousticPreset[]
    {
        new AcousticPreset { Name = "Closet",     ReferenceSize = 1f, DecayTime = 0.3f, Diffusion = 0.65f, GainHF = 0.95f, WetSend = 0.08f },
        new AcousticPreset { Name = "SmallRoom",  ReferenceSize = 8f,   DecayTime = 0.7f, Diffusion = 0.75f, GainHF = 0.90f, WetSend = 0.12f },
        new AcousticPreset { Name = "Hallway",    ReferenceSize = 12f,  DecayTime = 1.1f, Diffusion = 0.5f,  GainHF = 0.92f, WetSend = 0.18f },
        new AcousticPreset { Name = "MediumRoom", ReferenceSize = 30f,  DecayTime = 1.5f, Diffusion = 0.85f, GainHF = 0.85f, WetSend = 0.18f },
        new AcousticPreset { Name = "LargeHall",  ReferenceSize = 80f,  DecayTime = 2.4f, Diffusion = 1.0f,  GainHF = 0.75f, WetSend = 0.12f },
        new AcousticPreset { Name = "Cavernous",  ReferenceSize = 140f,  DecayTime = 4.0f, Diffusion = 1.0f,  GainHF = 0.65f, WetSend = 0.08f },
    };

    public static readonly AcousticPreset Outdoor = new AcousticPreset
    {
        Name = "Outdoor",
        ReferenceSize = 1000f,
        DecayTime = 0.02f,
        Diffusion = 0.2f,
        GainHF = 0.5f,
        WetSend = 0.01f
    };
}

public static class AcousticSampler
{
    const float maxTraceDistance = 100f;
    const float outdoorRayFraction = 0.1f;

    public static AcousticPreset Sample(Vector3 position)
    {
        float totalDistance = 0f;
        int openRays = 0;

        if (BSPRoot.Nodes == null) return AcousticPresets.Outdoor;

        for (int i = 0; i < Octree.SearchDirections.Length; i++)
        {
            Ray ray = new Ray(position, Octree.SearchDirections[i]);
            BSPHit hit = BSPRoot.TraceRay(ray, maxTraceDistance);

            if (hit.Hit && BSPRoot.Nodes[hit.Node].nodeFlag != BSPNode.SkyboxNode)
            {
                totalDistance += Vector3.Distance(position, hit.Point);
            }
            else
            {
                totalDistance += maxTraceDistance;
                openRays++;
            }
        }

        float openFraction = openRays / (float)Octree.SearchDirections.Length;
        if (openFraction >= outdoorRayFraction)
        {
            return AcousticPresets.Outdoor;
        }

        float avgDistance = totalDistance / Octree.SearchDirections.Length;
        return BlendBySize(avgDistance);
    }

    static AcousticPreset BlendBySize(float size)
    {
        AcousticPreset[] presets = AcousticPresets.Sizes;

        if (size <= presets[0].ReferenceSize)
        {
            return presets[0];
        }

        if (size >= presets[presets.Length - 1].ReferenceSize)
        {
            return presets[presets.Length - 1];
        }

        for (int i = 0; i < presets.Length - 1; i++)
        {
            AcousticPreset a = presets[i];
            AcousticPreset b = presets[i + 1];

            if (size >= a.ReferenceSize && size <= b.ReferenceSize)
            {
                float t = (size - a.ReferenceSize) / (b.ReferenceSize - a.ReferenceSize);
                return AcousticPreset.Lerp(a, b, t);
            }
        }

        return presets[presets.Length - 1];
    }
}
public class AcousticZoneCache
{
    class Entry
    {
        public AcousticPreset Profile;
        public int RefCount;
        public float LastReleasedTime;
    }

    const float evictionGraceSeconds = 3f;

    Dictionary<int, Entry> zones = new Dictionary<int, Entry>();
    Dictionary<int, int> occupantZone = new Dictionary<int, int>();

    public AcousticPreset Enter(int occupantKey, Vector3 position, float now, out int zoneKey, out bool changed)
    {
        int leaf = FindAcousticLeaf(position, out Vector3 sampleOrigin, out bool hasSkybox);
        zoneKey = leaf;
        changed = false;

        if (occupantZone.TryGetValue(occupantKey, out int previousLeaf))
        {
            if (previousLeaf == leaf)
            {
                return zones[leaf].Profile;
            }

            Release(previousLeaf, now);
        }

        changed = true;

        if (!zones.TryGetValue(leaf, out Entry entry))
        {
            AcousticPreset profile = hasSkybox ? AcousticPresets.Outdoor : AcousticSampler.Sample(sampleOrigin);
            entry = new Entry { Profile = profile, RefCount = 0 };
            zones[leaf] = entry;
        }

        entry.RefCount++;
        occupantZone[occupantKey] = leaf;

        return entry.Profile;
    }

    public void Exit(int occupantKey, float now)
    {
        if (occupantZone.TryGetValue(occupantKey, out int leaf))
        {
            Release(leaf, now);
            occupantZone.Remove(occupantKey);
        }
    }

    public void SweepExpired(float now)
    {
        List<int> expired = null;

        foreach (var kv in zones)
        {
            if (kv.Value.RefCount <= 0 && now - kv.Value.LastReleasedTime > evictionGraceSeconds)
            {
                (expired ??= new List<int>()).Add(kv.Key);
            }
        }

        if (expired != null)
        {
            foreach (int leaf in expired)
            {
                zones.Remove(leaf);
            }
        }
    }

    public void Clear()
    {
        zones.Clear();
        occupantZone.Clear();
    }

    void Release(int leaf, float now)
    {
        if (zones.TryGetValue(leaf, out Entry entry))
        {
            entry.RefCount--;
            entry.LastReleasedTime = now;
        }
    }

    static Vector3 ComputeLeafCentroid(VisLeaf leaf)
    {
        if (leaf.Portals == null) return Vector3.Zero;

        Vector3 sum = Vector3.Zero;
        int count = 0;

        foreach (int portalId in leaf.Portals)
        {
            if (portalId == -1) continue;

            Portal portal = VisRoot.VisPortals[portalId];
            if (portal.Vertices == null) continue;

            for (int v = 0; v < portal.Vertices.Length; v++)
            {
                sum += portal.Vertices[v];
                count++;
            }
        }

        return count > 0 ? sum / count : Vector3.Zero;
    }

    static int FindAcousticLeaf(Vector3 position, out Vector3 sampleOrigin, out bool hasSkybox)
    {
        sampleOrigin = position;
        hasSkybox = false;

        if (BSPRoot.Nodes == null) return -1;

        uint node = BSPRoot.Traverse(position);

        if (!MainEngine.LoadedMapHasVis || VisRoot.VisLeaves == null)
        {
            return (int)node;
        }

        int local = Array.FindIndex(VisRoot.VisLeaves, l => l.BspLeafID == node);
        if (local == -1)
        {
            return (int)node;
        }

        VisLeaf leaf = VisRoot.VisLeaves[local];
        hasSkybox = leaf.HasSkybox;

        Vector3 centroid = ComputeLeafCentroid(leaf);
        if (centroid != Vector3.Zero)
        {
            sampleOrigin = centroid;
        }

        return local;
    }
}