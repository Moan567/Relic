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
internal class StairBuilder : BrushBuilder
{
    [BuilderParam("Step Count", 1, 128, 1)]
    public int StepCount = 8;

    [BuilderParam("Step Height", 0.05f, 4, 0.125f)]
    public float StepHeight;

    [BuilderParam("Step Width", 0.25f, 32, 0.25f)]
    public float StepWidth;

    [BuilderParam("Step Depth", 0.25f, 16, 0.25f)]
    public float StepDepth = 0.5f;

    [BuilderParam("Curve Angle", -360, 360, 5)]
    public float CurveAngleDegrees = 0f;

    Vector3 startPoint, travelDir;
    float bottomY;
    List<Brush> previewBrushes = new();

    static readonly Color BoxBorder = new Color(255, 160, 60, 220);

    public StairBuilder(FaceMoveable face)
    {
        var (sp, dir, faceWidth, bY, tY) = AnalyzeFace(face.brush, face.face);
        startPoint = sp;
        travelDir = dir;
        bottomY = bY;

        StepWidth = MathF.Max(faceWidth, 1f);
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

        float totalLength = StepDepth * StepCount;
        float totalAngleRad = MathHelper.ToRadians(CurveAngleDegrees);
        Vector3 sideAxis = Vector3.Cross(Vector3.Up, travelDir);

        float tolerance = PrimitiveGenerator.ScaleToleranceForSize(1f / 32f, MathF.Min(StepDepth, StepHeight));

        for (int i = 0; i < StepCount; i++)
        {
            float t0 = (float)i / StepCount;
            float t1 = (float)(i + 1) / StepCount;

            var (rawP0, widthDir0) = PathPointAndWidthDir(t0, totalLength, totalAngleRad, sideAxis);
            var (rawP1, widthDir1) = PathPointAndWidthDir(t1, totalLength, totalAngleRad, sideAxis);

            Vector3 p0 = PrimitiveGenerator.SnapVertex(rawP0, tolerance);
            Vector3 p1 = PrimitiveGenerator.SnapVertex(rawP1, tolerance);

            float y0 = PrimitiveGenerator.SnapToGridStep(bottomY + StepHeight * i, tolerance);
            float y1 = PrimitiveGenerator.SnapToGridStep(bottomY + StepHeight * (i + 1), tolerance);

            var brush = BuildStepBrush(p0, p1, widthDir0, widthDir1, y0, y1, StepWidth, StepWidth, tolerance);
            if (brush.HasValue) previewBrushes.Add(brush.Value);
        }
    }
    (Vector3 point, Vector3 widthDir) PathPointAndWidthDir(float t, float totalLength, float totalAngleRad, Vector3 sideAxis)
    {
        if (MathF.Abs(totalAngleRad) < 0.0001f)
            return (startPoint + travelDir * (totalLength * t), sideAxis);

        float radius = totalLength / totalAngleRad;
        Vector3 center = startPoint - sideAxis * radius;

        float theta = totalAngleRad * t;
        Vector3 toStart = startPoint - center;

        float cosT = MathF.Cos(theta), sinT = MathF.Sin(theta);
        Vector3 rotated = new Vector3(
            toStart.X * cosT - toStart.Z * sinT,
            0,
            toStart.X * sinT + toStart.Z * cosT);

        Vector3 point = center + rotated;
        Vector3 widthDir = Vector3.Normalize(point - center);
        return (point, widthDir);
    }
    static Brush? BuildStepBrush(Vector3 p0, Vector3 p1, Vector3 widthDir0, Vector3 widthDir1, float bottomY, float topY, float width0, float width1, float tolerance)
    {
        if (Vector3.DistanceSquared(p0, p1) < 0.0001f) return null;

        Vector3 leadA = PrimitiveGenerator.SnapVertex(p0 + widthDir0 * (width0 * 0.5f), tolerance);
        Vector3 leadB = PrimitiveGenerator.SnapVertex(p0 - widthDir0 * (width0 * 0.5f), tolerance);
        Vector3 trailA = PrimitiveGenerator.SnapVertex(p1 + widthDir1 * (width1 * 0.5f), tolerance);
        Vector3 trailB = PrimitiveGenerator.SnapVertex(p1 - widthDir1 * (width1 * 0.5f), tolerance);

        Vector3 tangent0 = Vector3.Normalize(Vector3.Cross(widthDir0, Vector3.Up));
        Vector3 tangent1 = Vector3.Normalize(Vector3.Cross(widthDir1, Vector3.Up));

        Vector3 sideRightNormal = OrientOutward(Vector3.Cross(trailA - leadA, Vector3.Up), leadA, p0);
        Vector3 sideLeftNormal = OrientOutward(Vector3.Cross(trailB - leadB, Vector3.Up), leadB, p0);

        var planes = new List<Plane>
        {
            new Plane(Vector3.Down, bottomY),
            new Plane(Vector3.Up, -topY),
            new Plane(tangent1, -Vector3.Dot(tangent1, p1)),
            new Plane(-tangent0, Vector3.Dot(tangent0, p0)),
            new Plane(sideRightNormal, -Vector3.Dot(sideRightNormal, leadA)),
            new Plane(sideLeftNormal, -Vector3.Dot(sideLeftNormal, leadB)),
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
