using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Engine.Physics;
using static Engine.MainEngine;
using Rockwall;
using Engine.Utils;
using Engine.Entities;

namespace Engine.Rendering;
internal class PortalCulling
{
    public static List<uint> MainVisibleLeaves { get; } = new List<uint>(256);
    public static List<uint> SkyVisibleLeaves { get; } = new List<uint>(256);
    public static OctBounds SkyBounds => skyboxEntryBounds ?? default;
    public struct FrustumPlanes
    {
        public Vector4 Left, Right, Bottom, Top;
    }

    private static FrustumPlanes cachedFrustum;
    private static Matrix worldToView;
    public static FrustumPlanes CurrentFrustum => cachedFrustum;

    private const float InvSqrt2 = 0.70710678f;
    private const float Sqrt2 = 1.41421356f;
    public struct OctBounds
    {
        public System.Numerics.Vector4 mins; // X, Y, D1, D2
        public System.Numerics.Vector4 maxs;

        public float minX { get => mins.X; set => mins.X = value; }
        public float maxX { get => maxs.X; set => maxs.X = value; }
        public float minY { get => mins.Y; set => mins.Y = value; }
        public float maxY { get => maxs.Y; set => maxs.Y = value; }
        public float minD1 { get => mins.Z; set => mins.Z = value; }
        public float maxD1 { get => maxs.Z; set => maxs.Z = value; }
        public float minD2 { get => mins.W; set => mins.W = value; }
        public float maxD2 { get => maxs.W; set => maxs.W = value; }
    }

    private static OctBounds[] leafBounds = Array.Empty<OctBounds>();
    private static int[] leafGeneration = Array.Empty<int>();
    private static int currentGeneration = 0;

    private static int[] queueLeaf = new int[64];
    private static OctBounds[] queueBounds = new OctBounds[64];
    private static ushort[] queueDepth = new ushort[64];

    private static int queueHead, queueTail, queueCount;

    private static OctBounds? skyboxEntryBounds;

    private const int PolyScratchCapacity = 16;
    private static List<Vector3> nearClipScratch = new(PolyScratchCapacity);
    private static List<Vector3> frustumScratchA = new(PolyScratchCapacity);
    private static List<Vector3> frustumScratchB = new(PolyScratchCapacity);
    private static List<Vector2> debugClipScratchA = new(PolyScratchCapacity);
    private static List<Vector2> debugClipScratchB = new(PolyScratchCapacity);
    private static List<(Vector2 a, Vector2 b, Color color, float thickness)> debugScreenLines = new(256);

    private static Vector2 viewportMin, viewportMax;
    private static float screenScaleX, screenScaleY, screenOffsetX, screenOffsetY;

    #region projection / clipping

    private static void CacheFrustumPlanes()
    {
        worldToView = RenderEngine.WorldMatrix * RenderEngine.ViewMatrix;
        ExtractSidePlanes(RenderEngine.ProjectionMatrix, out cachedFrustum.Left, out cachedFrustum.Right, out cachedFrustum.Bottom, out cachedFrustum.Top);

        var viewport = Instance.GraphicsDevice.Viewport;
        viewportMin = new Vector2(viewport.X, viewport.Y);
        viewportMax = viewportMin + new Vector2(viewport.Width, viewport.Height);

        screenScaleX = viewport.Width * 0.5f;
        screenScaleY = viewport.Height * 0.5f;
        screenOffsetX = viewport.X + screenScaleX;
        screenOffsetY = viewport.Y + screenScaleY;
    }
    private static Vector2 ProjectToScreen(Vector3 viewPoint)
    {
        var m = RenderEngine.ProjectionMatrix;

        float clipX = viewPoint.X * m.M11 + viewPoint.Y * m.M21 + viewPoint.Z * m.M31 + m.M41;
        float clipY = viewPoint.X * m.M12 + viewPoint.Y * m.M22 + viewPoint.Z * m.M32 + m.M42;
        float clipW = viewPoint.X * m.M14 + viewPoint.Y * m.M24 + viewPoint.Z * m.M34 + m.M44;

        float invW = MathF.Abs(clipW) > 1e-8f ? 1f / clipW : 1f;
        float ndcX = clipX * invW;
        float ndcY = clipY * invW;

        return new Vector2(screenOffsetX + ndcX * screenScaleX, screenOffsetY - ndcY * screenScaleY);
    }

    private static Vector3 IntersectNearPlane(Vector3 a, Vector3 b, float nearZ)
    {
        float t = (nearZ - a.Z) / (b.Z - a.Z);
        return a + (b - a) * t;
    }

    private static List<Vector3> ClipPolygonNearPlane(ReadOnlySpan<Vector3> viewSpacePoly, float nearZ)
    {
        nearClipScratch.Clear();
        int n = viewSpacePoly.Length;

        for (int i = 0; i < n; i++)
        {
            Vector3 cur = viewSpacePoly[i];
            Vector3 prev = viewSpacePoly[(i - 1 + n) % n];

            bool curIn = cur.Z <= nearZ;
            bool prevIn = prev.Z <= nearZ;

            if (curIn != prevIn)
                nearClipScratch.Add(IntersectNearPlane(prev, cur, nearZ));
            if (curIn)
                nearClipScratch.Add(cur);
        }
        return nearClipScratch;
    }

    public static bool TryGetPortalScreenBounds(Vector3[] worldPoints, out Vector2 min, out Vector2 max)
    {
        min = Vector2.Zero;
        max = Vector2.Zero;

        Span<Vector3> viewPoints = stackalloc Vector3[worldPoints.Length];
        for (int i = 0; i < worldPoints.Length; i++)
            viewPoints[i] = Vector3.Transform(worldPoints[i], worldToView);

        float nearZ = -0.01f;
        var clipped = ClipPolygonNearPlane(viewPoints, nearZ);
        if (clipped.Count == 0) return false;

        var viewport = Instance.GraphicsDevice.Viewport;
        min = new Vector2(float.MaxValue);
        max = new Vector2(float.MinValue);

        foreach (var vp in clipped)
        {
            var screen = viewport.Project(vp, RenderEngine.ProjectionMatrix, Matrix.Identity, Matrix.Identity);
            min.X = MathF.Min(min.X, screen.X);
            min.Y = MathF.Min(min.Y, screen.Y);
            max.X = MathF.Max(max.X, screen.X);
            max.Y = MathF.Max(max.Y, screen.Y);
        }

        var vpMin = new Vector2(viewport.X, viewport.Y);
        var vpMax = vpMin + new Vector2(viewport.Width, viewport.Height);
        min = Vector2.Max(min, vpMin);
        max = Vector2.Min(max, vpMax);

        return true;
    }

    private static void ClipNearPlaneInto(List<Vector3> input, float nearZ, List<Vector3> output)
    {
        output.Clear();
        int n = input.Count;
        if (n == 0) return;

        for (int i = 0; i < n; i++)
        {
            Vector3 cur = input[i];
            Vector3 prev = input[(i - 1 + n) % n];

            bool curIn = cur.Z <= nearZ;
            bool prevIn = prev.Z <= nearZ;

            if (curIn != prevIn)
                output.Add(IntersectNearPlane(prev, cur, nearZ));
            if (curIn)
                output.Add(cur);
        }
    }

    private static float PlaneDistance(Vector4 plane, Vector3 p)
        => plane.X * p.X + plane.Y * p.Y + plane.Z * p.Z + plane.W;

    private static void ClipPlaneInto(List<Vector3> input, Vector4 plane, List<Vector3> output)
    {
        output.Clear();
        int n = input.Count;
        if (n == 0) return;
        if (AllOutside(plane, input)) return;

        Vector3 prev = input[n - 1];
        float prevDist = PlaneDistance(plane, prev);

        for (int i = 0; i < n; i++)
        {
            Vector3 cur = input[i];
            float curDist = PlaneDistance(plane, cur);

            if ((curDist >= 0f) != (prevDist >= 0f))
            {
                float t = prevDist / (prevDist - curDist);
                output.Add(prev + (cur - prev) * t);
            }
            if (curDist >= 0f) output.Add(cur);

            prev = cur;
            prevDist = curDist;
        }
    }

    private static void ExtractSidePlanes(Matrix proj, out Vector4 left, out Vector4 right, out Vector4 bottom, out Vector4 top)
    {
        left = new Vector4(proj.M11 + proj.M14, proj.M21 + proj.M24, proj.M31 + proj.M34, proj.M41 + proj.M44);
        right = new Vector4(proj.M14 - proj.M11, proj.M24 - proj.M21, proj.M34 - proj.M31, proj.M44 - proj.M41);
        bottom = new Vector4(proj.M12 + proj.M14, proj.M22 + proj.M24, proj.M32 + proj.M34, proj.M42 + proj.M44);
        top = new Vector4(proj.M14 - proj.M12, proj.M24 - proj.M22, proj.M34 - proj.M32, proj.M44 - proj.M42);
    }
    static bool AllOutside(Vector4 plane, List<Vector3> pts)
    {
        foreach (var p in pts) if (PlaneDistance(plane, p) >= 0) return false;
        return true;
    }
    private static bool AabbOutsidePlane(Vector4 plane, Vector3 min, Vector3 max)
    {
        Vector3 positive = new(
            plane.X >= 0 ? max.X : min.X,
            plane.Y >= 0 ? max.Y : min.Y,
            plane.Z >= 0 ? max.Z : min.Z);
        return plane.X * positive.X + plane.Y * positive.Y + plane.Z * positive.Z + plane.W < 0;
    }
    private static List<Vector3> ClipPolygonToFrustum(ReadOnlySpan<Vector3> viewSpacePoly, float nearZ)
    {
        List<Vector3> current = frustumScratchA;
        List<Vector3> next = frustumScratchB;

        current.Clear();
        current.AddRange(viewSpacePoly);

        ClipNearPlaneInto(current, nearZ, next);
        (current, next) = (next, current);
        if (current.Count == 0) return current;

        ClipPlaneInto(current, cachedFrustum.Left, next);
        (current, next) = (next, current);
        if (current.Count == 0) return current;

        ClipPlaneInto(current, cachedFrustum.Right, next);
        (current, next) = (next, current);
        if (current.Count == 0) return current;

        ClipPlaneInto(current, cachedFrustum.Bottom, next);
        (current, next) = (next, current);
        if (current.Count == 0) return current;

        ClipPlaneInto(current, cachedFrustum.Top, next);
        (current, next) = (next, current);

        return current;
    }

    private static bool TryBuildPortalWindow(Vector3[] worldPoints, out OctBounds bounds)
    {
        bounds = default;

        Span<Vector3> viewPoints = stackalloc Vector3[worldPoints.Length];
        Vector3 aabbMin = new(float.MaxValue);
        Vector3 aabbMax = new(float.MinValue);

        for (int i = 0; i < worldPoints.Length; i++)
        {
            Vector3 vp = Vector3.Transform(worldPoints[i], worldToView);
            viewPoints[i] = vp;
            aabbMin = Vector3.Min(aabbMin, vp);
            aabbMax = Vector3.Max(aabbMax, vp);
        }

        float nearZ = -0.01f;
        if (aabbMin.Z > nearZ) return false;

        if (AabbOutsidePlane(cachedFrustum.Left, aabbMin, aabbMax)) return false;
        if (AabbOutsidePlane(cachedFrustum.Right, aabbMin, aabbMax)) return false;
        if (AabbOutsidePlane(cachedFrustum.Bottom, aabbMin, aabbMax)) return false;
        if (AabbOutsidePlane(cachedFrustum.Top, aabbMin, aabbMax)) return false;

        var clipped = ClipPolygonToFrustum(viewPoints, nearZ);
        if (clipped.Count == 0) return false;

        bounds.minX = bounds.minY = bounds.minD1 = bounds.minD2 = float.MaxValue;
        bounds.maxX = bounds.maxY = bounds.maxD1 = bounds.maxD2 = float.MinValue;

        foreach (var vp in clipped)
        {
            Vector2 screen = ProjectToScreen(vp);
            float x = screen.X;
            float y = screen.Y;
            float d1 = x + y;
            float d2 = x - y;

            if (x < bounds.minX) bounds.minX = x;
            if (x > bounds.maxX) bounds.maxX = x;
            if (y < bounds.minY) bounds.minY = y;
            if (y > bounds.maxY) bounds.maxY = y;
            if (d1 < bounds.minD1) bounds.minD1 = d1;
            if (d1 > bounds.maxD1) bounds.maxD1 = d1;
            if (d2 < bounds.minD2) bounds.minD2 = d2;
            if (d2 > bounds.maxD2) bounds.maxD2 = d2;
        }

        bounds.minX = MathF.Max(bounds.minX, viewportMin.X);
        bounds.maxX = MathF.Min(bounds.maxX, viewportMax.X);
        bounds.minY = MathF.Max(bounds.minY, viewportMin.Y);
        bounds.maxY = MathF.Min(bounds.maxY, viewportMax.Y);

        float vpD1Min = viewportMin.X + viewportMin.Y;
        float vpD1Max = viewportMax.X + viewportMax.Y;
        float vpD2Min = viewportMin.X - viewportMax.Y;
        float vpD2Max = viewportMax.X - viewportMin.Y;

        bounds.minD1 = MathF.Max(bounds.minD1, vpD1Min);
        bounds.maxD1 = MathF.Min(bounds.maxD1, vpD1Max);
        bounds.minD2 = MathF.Max(bounds.minD2, vpD2Min);
        bounds.maxD2 = MathF.Min(bounds.maxD2, vpD2Max);

        return true;
    }
    #endregion

    #region window ops

    public static bool IsValid(in OctBounds b)
    {
        return b.minX < b.maxX && b.minY < b.maxY && b.minD1 < b.maxD1 && b.minD2 < b.maxD2;
    }

    private static OctBounds Intersect(in OctBounds a, in OctBounds b) => new OctBounds
    {
        mins = System.Numerics.Vector4.Max(a.mins, b.mins),
        maxs = System.Numerics.Vector4.Min(a.maxs, b.maxs),
    };
    private static OctBounds Union(in OctBounds a, in OctBounds b) => new OctBounds
    {
        mins = System.Numerics.Vector4.Min(a.mins, b.mins),
        maxs = System.Numerics.Vector4.Max(a.maxs, b.maxs),
    };
    private static bool Contains(in OctBounds outer, in OctBounds inner)
    {
        return inner.minX >= outer.minX - ContainmentEpsilon && inner.maxX <= outer.maxX + ContainmentEpsilon
            && inner.minY >= outer.minY - ContainmentEpsilon && inner.maxY <= outer.maxY + ContainmentEpsilon
            && inner.minD1 >= outer.minD1 - ContainmentEpsilon * Sqrt2 && inner.maxD1 <= outer.maxD1 + ContainmentEpsilon * Sqrt2
            && inner.minD2 >= outer.minD2 - ContainmentEpsilon * Sqrt2 && inner.maxD2 <= outer.maxD2 + ContainmentEpsilon * Sqrt2;
    }
    #endregion

    #region flood fill

    private const float ContainmentEpsilon = 0.5f; // pixels of slack
    private const float CameraPlaneEpsilon = 0.25f;

    private static bool CameraStraddlesPortal(float dot)
    {
        return MathF.Abs(dot) < CameraPlaneEpsilon;
    }

    private static void EnsureLeafCapacity(int leafCount)
    {
        if (leafGeneration.Length < leafCount)
        {
            leafBounds = new OctBounds[leafCount];
            leafGeneration = new int[leafCount];
        }

        if (queueLeaf.Length < leafCount)
        {
            queueLeaf = new int[leafCount];
            queueBounds = new OctBounds[leafCount];
            queueDepth = new ushort[leafCount];
            queueHead = queueTail = queueCount = 0;
        }
    }

    private static void GrowQueue()
    {
        int oldCap = queueLeaf.Length;
        int newCap = oldCap * 2;
        var newLeaf = new int[newCap];
        var newBounds = new OctBounds[newCap];
        var newDepth = new ushort[newCap];

        for (int i = 0; i < queueCount; i++)
        {
            int idx = (queueHead + i) % oldCap;
            newLeaf[i] = queueLeaf[idx];
            newBounds[i] = queueBounds[idx];
            newDepth[i] = queueDepth[idx];
        }

        queueLeaf = newLeaf;
        queueBounds = newBounds;
        queueDepth = newDepth;
        queueHead = 0;
        queueTail = queueCount;
    }

    private static void EnqueueLeaf(int leaf, in OctBounds bounds, ushort curDepth)
    {
        if (queueCount == queueLeaf.Length) GrowQueue();

        queueLeaf[queueTail] = leaf;
        queueBounds[queueTail] = bounds;
        queueDepth[queueTail] = curDepth;
        queueTail = (queueTail + 1) % queueLeaf.Length;
        queueCount++;
    }

    private static (int leaf, OctBounds bounds, ushort depth) DequeueLeaf()
    {
        int leaf = queueLeaf[queueHead];
        OctBounds bounds = queueBounds[queueHead];
        ushort depth = queueDepth[queueHead];
        queueHead = (queueHead + 1) % queueLeaf.Length;
        queueCount--;
        return (leaf, bounds, depth);
    }

    public static void FloodFillFrustumLeaves(int startLeaf, bool isSkybox)
    {
        EnsureLeafCapacity(VisRoot.VisLeaves.Length);
        currentGeneration++;

        var visibleLeaves = isSkybox ? SkyVisibleLeaves : MainVisibleLeaves;
        visibleLeaves.Clear();
        queueHead = queueTail = queueCount = 0;

        var viewport = Instance.GraphicsDevice.Viewport;
        var fullMin = new Vector2(viewport.X, viewport.Y);
        var fullMax = fullMin + new Vector2(viewport.Width, viewport.Height);

        OctBounds fullBounds;

        if (!isSkybox)
        {
            fullBounds = new OctBounds
            {
                minX = fullMin.X,
                maxX = fullMax.X,
                minY = fullMin.Y,
                maxY = fullMax.Y,
                minD1 = (fullMin.X + fullMin.Y),
                maxD1 = (fullMax.X + fullMax.Y),
                minD2 = (fullMin.X - fullMax.Y),
                maxD2 = (fullMax.X - fullMin.Y),
            };
            skyboxEntryBounds = null;
        }
        else
        {
            fullBounds = skyboxEntryBounds ?? default;
        }

        leafGeneration[startLeaf] = currentGeneration;
        leafBounds[startLeaf] = fullBounds;
        visibleLeaves.Add((uint)startLeaf);
        EnqueueLeaf(startLeaf, fullBounds, 0);

        CacheFrustumPlanes();

        while (queueCount > 0)
        {
            var (leaf, parentBounds, depth) = DequeueLeaf();

            if (!RenderEngine.IsLeafInMainPVS((uint)leaf)) continue;

            var portals = VisRoot.VisLeaves[leaf].Portals;
            for (int pi = 0; pi < portals.Length; pi++)
            {
                int pID = portals[pi];
                if (pID == -1) continue;
                var portal = VisRoot.VisPortals[pID];
                int other = portal.LeafFront == leaf ? portal.LeafBack : portal.LeafFront;

                int otherBspNode = VisRoot.VisLeaves[other].BspLeafID;

                if (BSPRoot.Nodes[otherBspNode].split) continue;

                if (BSPRoot.Nodes[otherBspNode].solid)
                {
                    // If this leaf has a skybox, we need to possibly use the "other" side
                    if (VisRoot.VisLeaves[leaf].HasSkybox && BSPRoot.Nodes[otherBspNode].nodeFlag == BSPNode.SkyboxNode)
                    {
                        if (!skyboxEntryBounds.HasValue || !Contains(skyboxEntryBounds.Value, parentBounds))
                        {
                            if (!TryBuildPortalWindow(portal.Vertices, out var skyboxBounds)) continue;
                            skyboxBounds = Intersect(skyboxBounds, parentBounds);
                            skyboxEntryBounds = skyboxEntryBounds.HasValue ? Union(skyboxEntryBounds.Value, skyboxBounds) : skyboxBounds;
                        }
                    }
                    else continue;
                }

                // if we've already been here, and this portal wont make things any smaller,
                // skip entirely.
                if (leafGeneration[other] == currentGeneration && Contains(leafBounds[other], parentBounds))
                    continue;

                float camDot = portal.Plane.DotCoordinate(RenderEngine.CameraPosition);

                OctBounds bounds;

                if (CameraStraddlesPortal(camDot))
                {
                    bounds = parentBounds;
                }
                else
                {
                    if (!TryBuildPortalWindow(portal.Vertices, out var portalBounds)) continue;
                    bounds = Intersect(portalBounds, parentBounds);
                }

                if (!IsValid(bounds)) continue;
                //if (RenderEngine.DrawOctBounds) DebugDrawOctBounds(bounds, Color.Yellow);

                if (leafGeneration[other] == currentGeneration)
                {
                    var prevBounds = leafBounds[other];

                    // Already fully covered by what we've explored
                    if (Contains(prevBounds, bounds)) continue;

                    bounds = Union(bounds, prevBounds);
                }
                else
                {
                    leafGeneration[other] = currentGeneration;
                    visibleLeaves.Add((uint)other);
                }

                leafBounds[other] = bounds;
                EnqueueLeaf(other, bounds, (ushort)(depth + 1));
            }
        }
    }

    #endregion

    #region debug draw

    public static void DebugDrawScreenLine(Vector2 a, Vector2 b, Color color, float thickness = 1f)
        => debugScreenLines.Add((a, b, color, thickness));

    public static void DebugDrawScreenBox(Vector2 min, Vector2 max, Color color, float thickness = 1f)
    {
        DebugDrawScreenLine(new Vector2(min.X, min.Y), new Vector2(max.X, min.Y), color, thickness);
        DebugDrawScreenLine(new Vector2(max.X, min.Y), new Vector2(max.X, max.Y), color, thickness);
        DebugDrawScreenLine(new Vector2(max.X, max.Y), new Vector2(min.X, max.Y), color, thickness);
        DebugDrawScreenLine(new Vector2(min.X, max.Y), new Vector2(min.X, min.Y), color, thickness);
    }

    private static void ClipHalfPlane2D(List<Vector2> input, Vector2 normal, float offset, List<Vector2> output)
    {
        output.Clear();
        int n = input.Count;
        if (n == 0) return;

        for (int i = 0; i < n; i++)
        {
            Vector2 cur = input[i];
            Vector2 prev = input[(i - 1 + n) % n];

            float curDist = offset - Vector2.Dot(cur, normal);
            float prevDist = offset - Vector2.Dot(prev, normal);

            bool curIn = curDist >= 0f;
            bool prevIn = prevDist >= 0f;

            if (curIn != prevIn)
            {
                float t = prevDist / (prevDist - curDist);
                output.Add(prev + (cur - prev) * t);
            }
            if (curIn)
                output.Add(cur);
        }
    }

    private static readonly Vector2[] OctPlaneNormals =
    {
        new Vector2(1, 0),
        new Vector2(1, 1),
        new Vector2(0, 1),
        new Vector2(-1, 1),
        new Vector2(-1, 0),
        new Vector2(-1, -1),
        new Vector2(0, -1),
        new Vector2(1, -1),
    };

    public static void DebugDrawOctBounds(in OctBounds b, Color color, float thickness = 1f)
    {
        Span<float> offsets = stackalloc float[8]
        {
            b.maxX, b.maxD1, b.maxY, -b.minD2, -b.minX, -b.minD1, -b.minY, b.maxD2
        };

        List<Vector2> current = debugClipScratchA;
        List<Vector2> next = debugClipScratchB;

        current.Clear();
        current.Add(new Vector2(b.minX, b.minY));
        current.Add(new Vector2(b.maxX, b.minY));
        current.Add(new Vector2(b.maxX, b.maxY));
        current.Add(new Vector2(b.minX, b.maxY));

        for (int i = 0; i < OctPlaneNormals.Length; i++)
        {
            ClipHalfPlane2D(current, OctPlaneNormals[i], offsets[i], next);
            (current, next) = (next, current);
            if (current.Count == 0) return;
        }

        for (int i = 0; i < current.Count; i++)
            DebugDrawScreenLine(current[i], current[(i + 1) % current.Count], color, thickness);
    }

    private static void DrawScreenLine(Vector2 a, Vector2 b, Color color, float thickness)
    {
        Vector2 delta = b - a;
        float length = delta.Length();
        if (length < 0.0001f) return;

        float angle = MathF.Atan2(delta.Y, delta.X);
        Instance.SpriteBatch.Draw(RenderEngine.WhiteTexture, a, null, color, angle, Vector2.Zero, new Vector2(length, thickness), SpriteEffects.None, 0f);
    }

    public static void ClearDebugScreenLines() => debugScreenLines.Clear();

    public static void FlushDebugScreenLines()
    {
        if (debugScreenLines.Count == 0) return;

        Instance.SpriteBatch.Begin(blendState: BlendState.NonPremultiplied);
        foreach (var (a, b, color, thickness) in debugScreenLines)
            DrawScreenLine(a, b, color, thickness);
        Instance.SpriteBatch.End();

        debugScreenLines.Clear();
    }

    #endregion
}