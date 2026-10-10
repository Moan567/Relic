using Relic.Utils;
using Microsoft.Xna.Framework;
using Rockwall;
using Rockwall2.Editor.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Mapper.Utils;
public class CSGUtils
{
    public readonly struct SplitResult
    {
        public readonly int sourceBrushIndex;
        public readonly Brush original;
        public readonly Brush? front;
        public readonly Brush? back;
        public readonly EntityReference owner;

        public SplitResult(int sourceBrushIndex, Brush original, Brush? front, Brush? back, EntityReference owner)
        {
            this.sourceBrushIndex = sourceBrushIndex;
            this.original = original;
            this.front = front;
            this.back = back;
            this.owner = owner;
        }
    }

    public static List<SplitResult> ComputeSplits(IEnumerable<int> brushIndices, Plane clipPlane, int sourceFaceHint = 0)
    {
        var results = new List<SplitResult>();
        foreach (var idx in brushIndices)
        {
            var brush = MapTools.Brushes[idx];
            if (!BrushOperations.SplitBrush(brush, clipPlane, out var front, out var back, sourceFaceHint))
                continue;

            results.Add(new SplitResult(idx, brush, front, back, MapTools.GetOwningEntity(idx)));
        }
        return results;
    }

    public static List<int> CommitSplits(List<SplitResult> splits, bool includeBack = true)
    {
        var addedIndices = new List<int>();
        if (splits.Count == 0) return addedIndices;

        foreach (var s in splits)
            MapTools.RemoveBrush(s.original);
        MapTools.FinalizeDeletedObjects();

        foreach (var s in splits)
        {
            void AddPiece(Brush b)
            {
                MapTools.AddBrush(b);
                int idx = MapTools.Brushes.Length - 1;
                MapTools.RecomputeBrushBounds(idx);
                addedIndices.Add(idx);

                if (s.owner != null)
                {
                    MapTools.AddBrushToEntity(s.owner, idx);
                }
            }

            if (s.front.HasValue) AddPiece(s.front.Value);
            if (s.back.HasValue && includeBack) AddPiece(s.back.Value);
        }

        return addedIndices;
    }

    public static int? AddExtrudedBrush(Brush extrudedDuplicate, int faceIndex, Plane originalPlane)
    {
        if (!BrushOperations.SplitBrush(extrudedDuplicate, originalPlane, out var front, out _, faceIndex))
            return null;
        if (!front.HasValue) return null;

        MapTools.AddBrush(front.Value);
        int idx = MapTools.Brushes.Length - 1;
        MapTools.RecomputeBrushBounds(idx);
        return idx;
    }
    public static bool SubtractWithBrush(int sourceBrushID)
    {
        List<int> affectedBrushes = new List<int>();

        for (int i = 0; i < MapTools.Brushes.Length; i++)
        {
            if (i == sourceBrushID) continue;
            if (!MapTools.BrushBounds[sourceBrushID].Intersects(MapTools.BrushBounds[i])) continue;

            var source = MapTools.Brushes[sourceBrushID];
            var target = MapTools.Brushes[i];

            if (BrushContainsBrush(source, target)) { MapTools.RemoveBrush(target); continue; }
            if (!BrushesIntersect(source, target)) continue;

            affectedBrushes.Add(i);
        }

        bool anySuccess = false;
        List<Brush> toAdd = new List<Brush>();

        var sourceFaces = MapTools.Brushes[sourceBrushID].Faces;
        var sourcePos = MapTools.Brushes[sourceBrushID].Position;

        // Cutter planes computed once, in world space.
        var cutterPlanes = new List<Plane>();
        foreach (var face in sourceFaces)
        {
            if (!face.Plane.HasValue) continue;
            cutterPlanes.Add(new Plane(face.Normal, face.Plane.Value.D - Vector3.Dot(face.Normal, sourcePos)));
        }

        foreach (var brushID in affectedBrushes)
        {
            var workingBrush = MapTools.Brushes[brushID];
            var clippedAwayPiece = workingBrush;

            // Greedy prepass: each iteration, pick whichever candidate plane
            // excludes the most of the CURRENT remainder's vertices
            while (true)
            {
                if (BrushContainsBrushInclusive(MapTools.Brushes[sourceBrushID], clippedAwayPiece))
                    break;

                Plane? bestPlane = null;
                int bestOutsideCount = 0;

                foreach (var plane in cutterPlanes)
                {
                    if (ClassifyBrush(clippedAwayPiece, plane) != PlaneIntersectionType.Intersecting)
                        continue;

                    int outsideCount = 0;
                    foreach (var v in WorldVertices(clippedAwayPiece))
                        if (plane.DotCoordinate(v) > EPSILON)
                            outsideCount++;

                    if (outsideCount > bestOutsideCount)
                    {
                        bestOutsideCount = outsideCount;
                        bestPlane = plane;
                    }
                }

                if (!bestPlane.HasValue) break; // nothing left intersects

                if (!BrushOperations.SplitBrush(clippedAwayPiece, bestPlane.Value, out var front, out var back))
                    break; // shouldn't happen given the Intersecting check, but guard anyway

                if (front.HasValue) toAdd.Add(front.Value);
                if (back.HasValue) clippedAwayPiece = back.Value;
                else break;
            }

            if (BrushContainsBrushInclusive(MapTools.Brushes[sourceBrushID], clippedAwayPiece))
            {
                MapTools.RemoveBrush(workingBrush);
                anySuccess = true;
            }
            else
            {
                toAdd.Clear();
            }
        }

        MapTools.FinalizeDeletedObjects();
        foreach (var b in toAdd) MapTools.AddBrush(b);
        MapTools.RecomputeAllBrushBounds();

        return anySuccess;
    }
    public static bool IntersectWithBrush(int sourceBrushID)
    {
        List<int> affectedBrushes = new List<int>();

        for (int i = 0; i < MapTools.Brushes.Length; i++)
        {
            if (i == sourceBrushID) continue;

            if (!MapTools.BrushBounds[sourceBrushID].Intersects(MapTools.BrushBounds[i]))
                continue;

            var source = MapTools.Brushes[sourceBrushID];
            var target = MapTools.Brushes[i];

            if (BrushContainsBrush(source, target))
            {
                MapTools.RemoveBrush(target);
                continue;
            }

            if (!BrushesIntersect(source, target))
                continue;

            affectedBrushes.Add(i);
        }

        bool anySuccess = false;

        foreach (var brushID in affectedBrushes)
        {
            var workingBrush = MapTools.Brushes[brushID];
            var testBrushBounds = MapTools.BrushBounds[brushID];

            List<Plane> splits = new List<Plane>();
            for (int f = 0; f < MapTools.Brushes[sourceBrushID].Faces.Length; f++)
            {
                var face = MapTools.Brushes[sourceBrushID].Faces[f];

                if (!face.Plane.HasValue) continue;
                var testPlane = new Plane(face.Normal, face.Plane.Value.D - Vector3.Dot(face.Normal, MapTools.Brushes[sourceBrushID].Position));

                if (ClassifyBrush(workingBrush, testPlane) == PlaneIntersectionType.Intersecting)
                {
                    splits.Add(testPlane);
                }
            }

            splits = splits.OrderBy(p => float.Abs(p.Normal.X))
                            .ThenBy(p => float.Abs(p.Normal.Y))
                            .ThenBy(p => float.Abs(p.Normal.Z)).ToList();

            var clippedAwayPiece = workingBrush;

            for (int i = 0; i < splits.Count; i++)
            {
                var split = splits[i];

                if (!BrushOperations.SplitBrush(clippedAwayPiece, split, out var front, out var back)) continue;

                if (back.HasValue) clippedAwayPiece = back.Value;
            }

            if (BrushContainsBrushInclusive(MapTools.Brushes[sourceBrushID], clippedAwayPiece))
            {
                MapTools.RemoveBrush(workingBrush);
                MapTools.RemoveBrush(MapTools.Brushes[sourceBrushID]);
                MapTools.AddBrush(clippedAwayPiece);
                anySuccess = true;
            }
        }

        MapTools.FinalizeDeletedObjects();

        MapTools.RecomputeAllBrushBounds();
        return anySuccess;
    }

    public static bool MergeBrushes(List<int> selectedBrushes)
    {
        // Have to have at least 2
        if (selectedBrushes.Count < 2) return false;

        var sourceBrush = MapTools.Brushes[selectedBrushes[0]];

        var newBrushPos = Vector3.Zero;
        foreach (var idx in selectedBrushes)
        {
            newBrushPos += WorldVertices(MapTools.Brushes[idx]).Aggregate((a,b)=>a+b) / MapTools.Brushes[idx].Vertices.Length;
        }
        newBrushPos /= selectedBrushes.Count;

        var allPoints = new List<Vector3>();
        foreach (var idx in selectedBrushes)
        {
            allPoints.AddRange(WorldVertices(MapTools.Brushes[idx]).Select(v=>v - newBrushPos));

            MapTools.RemoveBrush(MapTools.Brushes[idx]);
        }

        var poly = ConvexHull3D.BuildPolygonalHull(ConvexHull3D.Compute(allPoints));
        var brush = BrushOperations.CreateBrushFromPlanes(poly.Select(p => p.plane).ToList(), sourceBrush.Faces[0].MaterialName, sourceBrush.Faces[0].Surface);

        MapTools.FinalizeDeletedObjects();

        if (brush.HasValue)
        {
            var b = brush.Value;
            b.Position = newBrushPos;
            MapTools.AddBrush(b);
        }
        else return false;

        return true;
    }
    public static bool AddHull(List<Vector3> worldPosVerts)
    {
        // Have to have at least 3
        if (worldPosVerts.Count < 3) return false;

        var newBrushPos = Vector3.Zero;
        foreach (var vert in worldPosVerts)
        {
            newBrushPos += vert;
        }
        newBrushPos /= worldPosVerts.Count;

        var poly = ConvexHull3D.BuildPolygonalHull(ConvexHull3D.Compute(worldPosVerts.Select(v=>v-newBrushPos).ToList()));
        var brush = BrushOperations.CreateBrushFromPlanes(poly.Select(p => p.plane).ToList(), Toolbelt.ActiveTexture, GlobalMapData.MaterialNameToIndex[Toolbelt.ActiveTexture]);

        MapTools.FinalizeDeletedObjects();

        if (brush.HasValue)
        {
            var b = brush.Value;
            b.Position = newBrushPos;
            MapTools.AddBrush(b);
        }
        else return false;

        return true;
    }

    const float EPSILON = 0.001f;

    static IEnumerable<Vector3> WorldVertices(Brush brush)
    {
        foreach (var v in brush.Vertices)
            yield return v + brush.Position;
    }
    static Plane GetWorldPlane(Brush brush, Face face)
    {
        var plane = face.Plane!.Value;

        return new Plane(
            face.Normal,
            plane.D - Vector3.Dot(face.Normal, brush.Position));
    }
    static bool AllVerticesOutsidePlane(Brush brush, Plane plane)
    {
        foreach (var v in WorldVertices(brush))
        {
            if (plane.DotCoordinate(v) <= EPSILON)
                return false;
        }

        return true;
    }
    static bool BrushContainsBrush(Brush outer, Brush inner)
    {
        foreach (var v in WorldVertices(inner))
        {
            if (!PointInsideBrush(v, outer))
                return false;
        }

        return true;
    }
    static bool BrushContainsBrushInclusive(Brush outer, Brush inner)
    {
        foreach (var v in WorldVertices(inner))
        {
            foreach (var face in outer.Faces)
            {
                if (!face.Plane.HasValue)
                    continue;

                var plane = GetWorldPlane(outer, face);

                if (plane.DotCoordinate(v) > EPSILON)
                    return false;
            }
        }

        return true;
    }
    static bool PointInsideBrush(Vector3 worldPoint, Brush brush)
    {
        foreach (var face in brush.Faces)
        {
            if (!face.Plane.HasValue)
                continue;

            var plane = GetWorldPlane(brush, face);

            if (plane.DotCoordinate(worldPoint) > EPSILON)
                return false;
        }

        return true;
    }
    static bool BrushesIntersect(Brush a, Brush b)
    {
        foreach (var face in a.Faces)
        {
            if (!face.Plane.HasValue)
                continue;

            if (AllVerticesOutsidePlane(b, GetWorldPlane(a, face)))
                return false;
        }

        foreach (var face in b.Faces)
        {
            if (!face.Plane.HasValue)
                continue;

            if (AllVerticesOutsidePlane(a, GetWorldPlane(b, face)))
                return false;
        }

        return true;
    }
    static PlaneIntersectionType ClassifyBrush(Brush brush, Plane worldPlane)
    {
        int front = 0;
        int back = 0;

        foreach (var v in WorldVertices(brush))
        {
            float d = worldPlane.DotCoordinate(v);

            if (d > EPSILON)
                front++;
            else if (d < -EPSILON)
                back++;
        }

        if (back == 0)
            return PlaneIntersectionType.Front;

        if (front == 0)
            return PlaneIntersectionType.Back;

        return PlaneIntersectionType.Intersecting;
    }
    static bool AllOutside(Plane plane, Span<Vector3> vertices)
    {
        foreach (var v in vertices)
        {
            if (plane.DotCoordinate(v) <= 0)
                return false;
        }

        return true;
    }
}
