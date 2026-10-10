using DefaultUnDo;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using Rockwall2.Editor.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Mapper.Utils;
internal class ArchBuilder : BrushBuilder
{
    [BuilderParam("Block Count", 1, 128, 1)]
    public int BlockCount = 8;

    [BuilderParam("Block Depth", 0.02f, 2, 0.02f)]
    public float BlockDepth = 0.15f;

    [BuilderParam("Block Thickness", 0.02f, 2, 0.02f)]
    public float BlockThickness = 0.1f;

    [BuilderParam("Block Width", 0.02f, 8, 0.02f)]
    public float BlockWidth;

    [BuilderParam("Curve Angle", -360, 360, 5)]
    public float CurveAngleDegrees = 180;

    Vector3 startPoint, travelDir;
    bool isBridgeMode;
    float bridgeHorizontalSpan, bridgeVerticalDelta; // only used when isBridgeMode
    List<Brush> previewBrushes = new();

    static readonly Color BoxBorder = new Color(255, 160, 60, 220);
    public ArchBuilder(FaceMoveable face)
    {
        var (sp, dir, faceWidth, _, _) = AnalyzeFace(face.brush, face.face);
        startPoint = sp;
        travelDir = dir;
        BlockWidth = MathF.Max(faceWidth, 0.01f);
    }

    public ArchBuilder(FaceMoveable faceA, FaceMoveable faceB)
    {
        var (spA, _, widthA, _, topA) = AnalyzeFace(faceA.brush, faceA.face);
        var (spB, _, widthB, _, topB) = AnalyzeFace(faceB.brush, faceB.face);

        Vector3 pointA = new Vector3(spA.X, topA, spA.Z);
        Vector3 pointB = new Vector3(spB.X, topB, spB.Z);

        Vector3 horizDelta = new Vector3(pointB.X - pointA.X, 0, pointB.Z - pointA.Z);
        travelDir = horizDelta.LengthSquared() > 0.0001f ? Vector3.Normalize(horizDelta) : Vector3.Forward;

        startPoint = pointA;
        isBridgeMode = true;
        bridgeHorizontalSpan = horizDelta.Length();
        bridgeVerticalDelta = pointB.Y - pointA.Y;

        BlockWidth = MathF.Max((widthA + widthB) * 0.5f, 0.01f);
    }

    static (Vector3 startPoint, Vector3 travelDir, float width, float bottomY, float topY) AnalyzeFace(int brushIdx, int faceIdx)
    {
        var brush = MapTools.Brushes[brushIdx];
        var face = brush.Faces[faceIdx];
        var uniqueVerts = face.Indices.Distinct().ToArray();

        Vector3 sum = Vector3.Zero;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var vi in uniqueVerts)
        {
            Vector3 wp = brush.Vertices[vi] + brush.Position;
            sum += wp;
            minY = MathF.Min(minY, wp.Y);
            maxY = MathF.Max(maxY, wp.Y);
        }
        Vector3 center = sum / uniqueVerts.Length;

        Vector3 normalHoriz = new Vector3(face.Normal.X, 0, face.Normal.Z);
        Vector3 travelDir;
        if (normalHoriz.LengthSquared() < 0.0001f)
        {
            travelDir = Vector3.Forward;
        }
        else
        {
            travelDir = Vector3.Normalize(normalHoriz);
        }

        Vector3 widthAxis = Vector3.Cross(Vector3.Up, travelDir);
        float minW = float.MaxValue, maxW = float.MinValue;
        foreach (var vi in uniqueVerts)
        {
            Vector3 wp = brush.Vertices[vi] + brush.Position;
            float proj = Vector3.Dot(wp, widthAxis);
            minW = MathF.Min(minW, proj);
            maxW = MathF.Max(maxW, proj);
        }
        float width = maxW - minW;

        Vector3 startPoint = new Vector3(center.X, minY, center.Z);

        return (startPoint, travelDir, width, minY, maxY);
    }
    public override void RegenerateGeometry()
    {
        previewBrushes.Clear();

        float totalAngleRad = MathHelper.ToRadians(CurveAngleDegrees);
        float totalLength, verticalRamp;

        if (isBridgeMode)
        {
            const float angleEps = 0.0001f;
            if (MathF.Abs(totalAngleRad) < angleEps)
            {
                totalLength = bridgeHorizontalSpan;
                verticalRamp = bridgeVerticalDelta;
            }
            else
            {
                float denom = 1f - MathF.Cos(totalAngleRad);
                if (MathF.Abs(denom) < angleEps)
                {
                    totalLength = bridgeHorizontalSpan;
                    verticalRamp = bridgeVerticalDelta;
                    totalAngleRad = 0f;
                }
                else
                {
                    float radius = bridgeHorizontalSpan / denom;
                    totalLength = radius * totalAngleRad;
                    verticalRamp = bridgeVerticalDelta - radius * MathF.Sin(totalAngleRad);
                }
            }
        }
        else
        {
            totalLength = BlockDepth * BlockCount;
            verticalRamp = 0f;
        }

        Vector3 widthAxis = Vector3.Cross(Vector3.Up, travelDir);

        const float baseSnapTolerance = 1f / 32f;
        const int maxSnapHalvings = 9;
        const float minSnapStep = 1f / 512f;

        float wedgeDepth = totalLength / BlockCount;
        float tolerance = PrimitiveGenerator.ScaleToleranceForSides(
            PrimitiveGenerator.ScaleToleranceForSize(baseSnapTolerance, MathF.Abs(wedgeDepth)),
            Math.Max(BlockCount, 1));

        float snapStep = PrimitiveGenerator.PickGridStep(MathF.Abs(wedgeDepth), tolerance, maxSnapHalvings);
        snapStep = MathF.Max(MathF.Min(snapStep, BlockThickness * 0.5f), minSnapStep);

        var boundaryPoints = new Vector3[BlockCount + 1];
        var boundaryOuter = new Vector3[BlockCount + 1];
        var boundaryInner = new Vector3[BlockCount + 1];
        var boundaryTangent = new Vector3[BlockCount + 1];

        for (int i = 0; i <= BlockCount; i++)
        {
            float t = (float)i / BlockCount;
            Vector3 point = ArchPoint(t, totalLength, totalAngleRad, verticalRamp);
            Vector3 tangent = ArchTangent(t, totalLength, totalAngleRad, verticalRamp);
            Vector3 radial = ArchRadialFromTangent(tangent);

            boundaryPoints[i] = SnapWithStep(point, snapStep);
            boundaryTangent[i] = tangent;
            boundaryOuter[i] = SnapWithStep(point + radial * (BlockThickness * 0.5f), snapStep);
            boundaryInner[i] = SnapWithStep(point - radial * (BlockThickness * 0.5f), snapStep);
        }

        for (int i = 0; i < BlockCount; i++)
        {
            var brush = BuildArchBlock(
                boundaryPoints[i], boundaryPoints[i + 1],
                boundaryOuter[i], boundaryOuter[i + 1],
                boundaryInner[i], boundaryInner[i + 1],
                boundaryTangent[i], boundaryTangent[i + 1],
                widthAxis, BlockWidth);
            if (brush.HasValue) previewBrushes.Add(brush.Value);
        }
    }
    static Vector3 SnapWithStep(Vector3 v, float step)
    {
        return new Vector3(
            PrimitiveGenerator.RoundToStep(v.X, step),
            PrimitiveGenerator.RoundToStep(v.Y, step),
            PrimitiveGenerator.RoundToStep(v.Z, step));
    }
    Vector3 ArchPoint(float t, float totalLength, float totalAngleRad, float verticalRamp)
    {
        if (MathF.Abs(totalAngleRad) < 0.0001f)
            return startPoint + travelDir * (totalLength * t) + Vector3.Up * (verticalRamp * t);

        float radius = totalLength / totalAngleRad;
        float angle = totalAngleRad * t;
        return startPoint
            + travelDir * (radius * (1f - MathF.Cos(angle)))
            + Vector3.Up * (radius * MathF.Sin(angle) + verticalRamp * t);
    }

    Vector3 ArchTangent(float t, float totalLength, float totalAngleRad, float verticalRamp)
    {
        if (MathF.Abs(totalAngleRad) < 0.0001f)
        {
            Vector3 flat = travelDir * totalLength + Vector3.Up * verticalRamp;
            return flat.LengthSquared() > 0.0001f ? Vector3.Normalize(flat) : travelDir;
        }

        float angle = totalAngleRad * t;
        Vector3 raw = travelDir * (totalLength * MathF.Sin(angle)) + Vector3.Up * (totalLength * MathF.Cos(angle) + verticalRamp);
        return raw.LengthSquared() > 0.0001f ? Vector3.Normalize(raw) : travelDir;
    }

    Vector3 ArchRadialFromTangent(Vector3 tangent)
    {
        float alongTravel = Vector3.Dot(tangent, travelDir);
        float alongUp = Vector3.Dot(tangent, Vector3.Up);
        return Vector3.Normalize(-travelDir * alongUp + Vector3.Up * alongTravel);
    }

    static Brush? BuildArchBlock(
        Vector3 p0, Vector3 p1,
        Vector3 outer0, Vector3 outer1,
        Vector3 inner0, Vector3 inner1,
        Vector3 tangent0, Vector3 tangent1,
        Vector3 widthAxis, float width)
    {
        if (Vector3.DistanceSquared(p0, p1) < 0.0001f) return null;

        Vector3 outerNormal = OrientOutward(Vector3.Cross(outer1 - outer0, widthAxis), outer0, p0);
        Vector3 innerNormal = OrientOutward(Vector3.Cross(inner1 - inner0, widthAxis), inner0, p0);

        var planes = new List<Plane>
        {
            new Plane(outerNormal, -Vector3.Dot(outerNormal, outer0)),
            new Plane(innerNormal, -Vector3.Dot(innerNormal, inner0)),
            new Plane(tangent1, -Vector3.Dot(tangent1, p1)),
            new Plane(-tangent0, Vector3.Dot(tangent0, p0)),
            new Plane(widthAxis, -Vector3.Dot(widthAxis, p0 + widthAxis * (width * 0.5f))),
            new Plane(-widthAxis, -Vector3.Dot(-widthAxis, p0 - widthAxis * (width * 0.5f))),
        };

        return BrushOperations.CreateBrushFromPlanes(
            planes, Toolbelt.ActiveTexture, GlobalMapData.MaterialNameToIndex[Toolbelt.ActiveTexture]);
    }
    static Vector3 OrientOutward(Vector3 normal, Vector3 pointOnPlane, Vector3 insidePoint)
    {
        normal = Vector3.Normalize(normal);
        if (Vector3.Dot(normal, insidePoint - pointOnPlane) > 0)
            normal = -normal;
        return normal;
    }
    public override void Commit()
    {
        Toolbelt.UndoManager.DoOnUndo(() => {
            foreach (var b in previewBrushes)
            {
                MapTools.RemoveBrush(b);
            }
            MapTools.FinalizeDeletedObjects();
            MapTools.RecomputeAllBrushBounds();
        });
        List<int> ids = new List<int>();
        foreach (var b in previewBrushes)
        {
            ids.Add(MapTools.Brushes.Length);
            MapTools.AddBrush(b);
        }
        MapTools.RecomputeAllBrushBounds();

        BoundingBox? unionBounds = null;

        foreach (var bi in ids)
        {
            if (bi < 0 || bi >= MapTools.BrushBounds.Length) continue;
            unionBounds = unionBounds.HasValue
                ? BoundingBox.CreateMerged(unionBounds.Value, MapTools.BrushBounds[bi])
                : MapTools.BrushBounds[bi];
        }

        var entity = new EntityReference
        {
            EntityName = "FuncDetail",
            Properties = Array.Empty<EntityProperty>(),
            BrushIndices = new List<int>(ids),
            brushOwnerGUIDs = MapTools.GuidsForBrushIndices(ids),
            Position = (unionBounds ?? default).Min,
        };

        var eid = Array.FindIndex(GlobalEditorData.EditorOverrides.overrides, o => o.name == "FuncDetail");
        if (eid != -1)
        {
            var src = GlobalEditorData.EditorOverrides.overrides[eid].defaultProperties;
            entity.Properties = new EntityProperty[src.Length];
            Array.Copy(src, entity.Properties, src.Length);
        }

        MapTools.AddEntity(entity);
        MapTools.SyncBrushOwnership();

        Close();
    }

    public override void OnRender(float delta, GraphicsDevice graphicsDevice, BasicEffect basicEffect)
    {
        if (previewBrushes.Count == 0) return;

        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        foreach (var b in previewBrushes)
        {
            foreach (var v in b.Vertices)
            {
                Vector3 wp = v + b.Position;
                lo = Vector3.Min(lo, wp);
                hi = Vector3.Max(hi, wp);
            }
        }

        Vector3 c000 = new(lo.X, lo.Y, lo.Z), c100 = new(hi.X, lo.Y, lo.Z);
        Vector3 c010 = new(lo.X, hi.Y, lo.Z), c110 = new(hi.X, hi.Y, lo.Z);
        Vector3 c001 = new(lo.X, lo.Y, hi.Z), c101 = new(hi.X, lo.Y, hi.Z);
        Vector3 c011 = new(lo.X, hi.Y, hi.Z), c111 = new(hi.X, hi.Y, hi.Z);

        Span<VertexPosition> boxVerts =
        [
            // Bottom
            new(c000), new(c100), new(c100), new(c110), new(c110), new(c010), new(c010), new(c000),
            // Top
            new(c001), new(c101), new(c101), new(c111), new(c111), new(c011), new(c011), new(c001),
            // Verticals
            new(c000), new(c001), new(c100), new(c101), new(c110), new(c111), new(c010), new(c011),
        ];

        basicEffect.DiffuseColor = BoxBorder.ToVector3();
        basicEffect.Alpha = 1f;
        basicEffect.World = Matrix.Identity;
        foreach (var pass in basicEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            graphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, boxVerts.ToArray(), 0, boxVerts.Length / 2);
        }

        var wireVerts = new List<VertexPositionColor>();
        foreach (var b in previewBrushes)
            wireVerts.AddRange(BuildBrushWireframe(b, new Color(255, 255, 255, 140)));

        if (wireVerts.Count > 1)
        {
            basicEffect.VertexColorEnabled = true;
            basicEffect.World = Matrix.Identity;
            basicEffect.Alpha = 1f;
            var arr = wireVerts.ToArray();
            foreach (var pass in basicEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, arr, 0, arr.Length / 2);
            }
            basicEffect.VertexColorEnabled = false;
        }
    }

    List<VertexPositionColor> BuildBrushWireframe(Brush brush, Color color)
    {
        var verts = new List<VertexPositionColor>();

        foreach (var face in brush.Faces)
        {
            if (face.Indices == null || face.Indices.Length < 3) continue;

            var ring = face.Indices.Distinct().ToList();
            if (ring.Count < 3) continue;

            Vector3 center = Vector3.Zero;
            foreach (var idx in ring) center += brush.Vertices[idx];
            center /= ring.Count;

            Vector3 normal = face.Normal;
            Vector3 refAxis = Vector3.Cross(normal, Vector3.UnitZ);
            if (refAxis.LengthSquared() < 1e-6f)
                refAxis = Vector3.Cross(normal, Vector3.UnitX);
            refAxis = Vector3.Normalize(refAxis);
            Vector3 perpAxis = Vector3.Normalize(Vector3.Cross(normal, refAxis));

            ring.Sort((a, b) =>
            {
                Vector3 da = brush.Vertices[a] - center;
                Vector3 db = brush.Vertices[b] - center;
                float angA = MathF.Atan2(Vector3.Dot(da, perpAxis), Vector3.Dot(da, refAxis));
                float angB = MathF.Atan2(Vector3.Dot(db, perpAxis), Vector3.Dot(db, refAxis));
                return angA.CompareTo(angB);
            });

            for (int i = 0; i < ring.Count; i++)
            {
                Vector3 p0 = brush.Vertices[ring[i]] + brush.Position;
                Vector3 p1 = brush.Vertices[ring[(i + 1) % ring.Count]] + brush.Position;
                verts.Add(new VertexPositionColor(p0, color));
                verts.Add(new VertexPositionColor(p1, color));
            }
        }

        return verts;
    }
}
