using Chisel.Utils;
using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Collections.Generic;
using static Engine.MainEngine;

namespace Engine.Rendering;

public static class TransparentRenderQueue
{
    private struct QueuedItem
    {
        public int LeafRank;
        public float Distance;
        public Action Draw;
    }

    private static List<QueuedItem> items = new List<QueuedItem>();

    private static int[] leafRanks = new int[0];
    private static int[] leafRankVersions = new int[0];
    private static int currentVersion;

    private static void EnsureCapacity(int size)
    {
        if (leafRanks.Length < size)
        {
            int newSize = Math.Max(size, VisRoot.VisLeaves?.Length ?? size);
            Array.Resize(ref leafRanks, newSize);
            Array.Resize(ref leafRankVersions, newSize);
        }
    }

    public static void BuildLeafOrder(uint cameraLeaf)
    {
        currentVersion++;

        if (!LoadedMapHasVis) return;

        var pvs = VisRoot.VisLeaves[cameraLeaf].PVS;

        EnsureCapacity((int)cameraLeaf + 1);

        for (int i = 0; i < pvs.Length; i++)
        {
            uint leaf = pvs[i];
            EnsureCapacity((int)leaf + 1);

            leafRanks[leaf] = pvs.Length - i;
            leafRankVersions[leaf] = currentVersion;
        }

        leafRanks[cameraLeaf] = pvs.Length + 1;
        leafRankVersions[cameraLeaf] = currentVersion;
    }

    private static int GetRank(uint leafID)
    {
        if (leafID < leafRanks.Length && leafRankVersions[leafID] == currentVersion)
        {
            return leafRanks[leafID];
        }

        return 0;
    }

    public static void RegisterLeafBound(uint leafID, Vector3 approxWorldPosition, Action draw)
    {
        int rank = GetRank(leafID);
        float distance = Vector3.DistanceSquared(approxWorldPosition, RenderEngine.CameraPosition);

        items.Add(new QueuedItem { LeafRank = rank, Distance = distance, Draw = draw });
    }

    public static void RegisterFreeform(Vector3 worldPosition, Action draw)
    {
        uint leaf = (uint)BSPRoot.Traverse(worldPosition);
        int rank = GetRank(leaf);
        float distance = Vector3.DistanceSquared(worldPosition, RenderEngine.CameraPosition);

        items.Add(new QueuedItem { LeafRank = rank, Distance = distance, Draw = draw });
    }

    public static int RankFromLeafBits(ulong[] leafBits)
    {
        if (leafBits == null) return 0;

        int best = 0;

        for (int w = 0; w < leafBits.Length; w++)
        {
            ulong word = leafBits[w];

            while (word != 0)
            {
                int bitIndex = System.Numerics.BitOperations.TrailingZeroCount(word);
                uint leafID = (uint)(w * 64 + bitIndex);

                int rank = GetRank(leafID);
                if (rank > best)
                {
                    best = rank;
                }

                word &= word - 1;
            }
        }

        return best;
    }

    public static void RegisterBrushFace(bool isEntityOwned, int precomputedRank, Vector3 sortPosition, Action draw)
    {
        if (isEntityOwned)
        {
            items.Add(new QueuedItem { LeafRank = precomputedRank, Distance = Vector3.DistanceSquared(sortPosition, RenderEngine.CameraPosition), Draw = draw });
        }
        else
        {
            RegisterFreeform(sortPosition, draw);
        }
    }

    public static void RenderAll()
    {
        items.Sort((a, b) =>
        {
            int rankCompare = a.LeafRank.CompareTo(b.LeafRank);
            if (rankCompare != 0)
            {
                return rankCompare;
            }

            return b.Distance.CompareTo(a.Distance);
        });

        for (int i = 0; i < items.Count; i++)
        {
            items[i].Draw();
        }

        items.Clear();
    }
}