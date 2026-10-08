using Chisel.Collision;
using Chisel.EXScript;
using Chisel.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Newtonsoft.Json;
using Rockwall;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

#if !rockwall

namespace Chisel.Formatter
{
    // The format is a dead-simple binary container:
    //
    //   [4 bytes]  magic "CMAP"
    //   [1 byte]   version
    //   [4 bytes]  map lump offset
    //   [4 bytes]  bsp lump offset
    //   [4 bytes]  vis lump offset
    //   <map lump data>
    //   <bsp lump data>
    //   <vis lump data>

    public static class MapFormatter
    {
        const uint Magic = 0x50414D43; // "CMAP" as a little-endian uint
        
        const byte Version = 10;
        public static byte[] WriteMapData(Map map, BSPFile bspFile, VisFile visFile)
        {
            using var ms = new MemoryStream(1 << 20); // start at 1 MB, grows as needed
            using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

            w.Write(Magic);
            w.Write(Version);

            // Reserve space for the three lump offsets, we'll seek back and fill them in
            // once we know where each lump actually landed.
            long offsetTablePos = ms.Position;
            w.Write(0); // map offset placeholder
            w.Write(0); // bsp offset placeholder
            w.Write(0); // vis offset placeholder

            int mapOffset = (int)ms.Position; WriteMap(w, map);
            int bspOffset = (int)ms.Position; WriteBSP(w, bspFile);
            int visOffset = (int)ms.Position; WriteVis(w, visFile);

            ms.Seek(offsetTablePos, SeekOrigin.Begin);
            w.Write(mapOffset);
            w.Write(bspOffset);
            w.Write(visOffset);

            return ms.ToArray();
        }

        public static (Map map, BSPFile bspFile, VisFile visFile) ReadMapData(string path)
        {
            // ReadAllBytes up front so we get a single contiguous MemoryStream
            var bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            if (r.ReadUInt32() != Magic)
                throw new Exception("ReadMapData: not a CMAP file");

            byte version = r.ReadByte();
            if (version != Version)
                throw new Exception($"ReadMapData: unsupported version {version}, expected {Version}");

            int mapOffset = r.ReadInt32();
            int bspOffset = r.ReadInt32();
            int visOffset = r.ReadInt32();

            // Jump directly to each lump by its stored offset.
            ms.Seek(mapOffset, SeekOrigin.Begin); var map = ReadMap(r);
            ms.Seek(bspOffset, SeekOrigin.Begin); var bspFile = ReadBSP(r);
            ms.Seek(visOffset, SeekOrigin.Begin); var visFile = ReadVis(r);

            return (map, bspFile, visFile);
        }

        static void WriteMap(BinaryWriter w, Map map)
        {
            w.Write(map.HasVis);

            w.Write(map.BrushBounds.Length);
            foreach (var bb in map.BrushBounds) WriteBBox(w, bb);

            w.Write(map.Brushes.Length);
            foreach (var b in map.Brushes) WriteBrush(w, b);

            w.Write(map.Terrains.Length);
            foreach (var t in map.Terrains) WriteTerrain(w, t);

            w.Write(map.Entities.Length);
            foreach (var e in map.Entities) WriteEntityRef(w, e);

            // Root node first, then the flat allNodes list the octree traversal uses.
            w.Write(map.OctreeNodes.Count);
            WriteOctree(w, map.Root);
            foreach (var n in map.OctreeNodes) WriteOctree(w, n);

            w.Write(map.LightNodes.Length);
            foreach (var ln in map.LightNodes) WriteLightNodeBundle(w, ln);

            w.Write(map.LightGroupKeys.Length);
            foreach (var key in map.LightGroupKeys) WriteStr(w, key);

            w.Write(map.MapModels.Length);
            foreach (var dm in map.MapModels) WriteDetailModel(w, dm);

            w.Write(map.StaticGeomVertices.Length);
            foreach (var v in map.StaticGeomVertices) WriteVertexLM(w, v);

            w.Write(map.LeafPolygons.Length);
            foreach (var lp in map.LeafPolygons) WriteLeafPolygon(w, lp);

            w.Write(map.LeafPolyStart.Length);
            for (int i = 0; i < map.LeafPolyStart.Length; i++)
            {
                w.Write(map.LeafPolyStart[i]);
                w.Write(map.LeafPolyCount[i]);
            }

            WriteNodeGraph(w, map.Nodegraph);
        }

        static Map ReadMap(BinaryReader r)
        {
            bool hasVis = r.ReadBoolean();

            var brushBounds = new BoundingBox[r.ReadInt32()];
            for (int i = 0; i < brushBounds.Length; i++) brushBounds[i] = ReadBBox(r);

            var brushes = new Brush[r.ReadInt32()];
            for (int i = 0; i < brushes.Length; i++) brushes[i] = ReadBrush(r);

            var terrains = new Terrain[r.ReadInt32()];
            for (int i = 0; i < terrains.Length; i++) terrains[i] = ReadTerrain(r);

            var entities = new EntityReference[r.ReadInt32()];
            for (int i = 0; i < entities.Length; i++) entities[i] = ReadEntityRef(r);

            int octreeNodeCount = r.ReadInt32();
            OctreeRoot.AllNodes.Clear();
            var root = ReadOctree(r);
            OctreeRoot.AllNodes.Add(root);
            var octreeNodes = new List<Octree>(octreeNodeCount);
            for (int i = 0; i < octreeNodeCount; i++)
            {
                var n = ReadOctree(r);
                OctreeRoot.AllNodes.Add(n);
                octreeNodes.Add(n);
            }

            var lightNodes = new LightNodeBundle[r.ReadInt32()];
            for (int i = 0; i < lightNodes.Length; i++) lightNodes[i] = ReadLightNodeBundle(r);

            var lightGroupKeys = new string[r.ReadInt32()];
            for (int i = 0; i < lightGroupKeys.Length; i++) lightGroupKeys[i] = ReadStr(r);

            var models = new MapPropModel[r.ReadInt32()];
            for (int i = 0; i < models.Length; i++) models[i] = ReadDetailModel(r);

            var staticGeomVertices = new VertexLightmapped[r.ReadInt32()];
            for (int i = 0; i < staticGeomVertices.Length; i++) staticGeomVertices[i] = ReadVertexLM(r);

            var leafPolygons = new LeafPolygon[r.ReadInt32()];
            for (int i = 0; i < leafPolygons.Length; i++) leafPolygons[i] = ReadLeafPolygon(r);

            int leafCount = r.ReadInt32();
            var leafPolyStart = new int[leafCount];
            var leafPolyCount = new int[leafCount];
            for (int i = 0; i < leafCount; i++)
            {
                leafPolyStart[i] = r.ReadInt32();
                leafPolyCount[i] = r.ReadInt32();
            }

            var nodegraph = ReadNodeGraph(r);

            return new Map
            {
                HasVis = hasVis,
                BrushBounds = brushBounds,
                Brushes = brushes,
                Terrains = terrains,
                Entities = entities,
                MapModels = models,
                StaticGeomVertices = staticGeomVertices,
                LeafPolygons = leafPolygons,
                LeafPolyStart = leafPolyStart,
                LeafPolyCount = leafPolyCount,
                Root = root,
                OctreeNodes = octreeNodes,
                LightNodes = lightNodes,
                LightGroupKeys = lightGroupKeys,
                Nodegraph = nodegraph,
            };
        }

        static void WriteDetailModel(BinaryWriter w, MapPropModel mdl)
        {
            w.Write(mdl.VisLeaf);
            w.Write(mdl.Material);

            w.Write(mdl.Vertices.Length);
            foreach(var vert in mdl.Vertices)
            {
                WritePropVertex(w,vert);
            }
            w.Write(mdl.Indices.Length);
            foreach (var indice in mdl.Indices)
            {
                w.Write(indice);
            }
        }
        static void WriteLeafPolygon(BinaryWriter w, LeafPolygon lp)
        {
            w.Write(lp.VertexStart);
            w.Write(lp.VertexCount);
            WriteStr(w, lp.MaterialName);
            WriteVec3(w, lp.Normal);
            WriteVec3(w, lp.Tangent);
            WriteVec3(w, lp.Binormal);
            WriteVec3(w, lp.B1);
            WriteVec3(w, lp.B2);
            WriteVec3(w, lp.B3);
        }

        static LeafPolygon ReadLeafPolygon(BinaryReader r) => new()
        {
            VertexStart = r.ReadInt32(),
            VertexCount = r.ReadInt32(),
            MaterialName = ReadStr(r),
            Normal = ReadVec3(r),
            Tangent = ReadVec3(r),
            Binormal = ReadVec3(r),
            B1 = ReadVec3(r),
            B2 = ReadVec3(r),
            B3 = ReadVec3(r),
        };
        static MapPropModel ReadDetailModel(BinaryReader r)
        {
            MapPropModel model = default;

            model.VisLeaf = r.ReadUInt32();
            model.Material = r.ReadString();
            model.Vertices = new MapPropModelVertex[r.ReadInt32()];

            for(int i = 0; i < model.Vertices.Length; i++)
            {
                model.Vertices[i] = ReadPropVertex(r);
            }

            model.Indices = new int[r.ReadInt32()];
            for (int i = 0; i < model.Indices.Length; i++)
            {
                model.Indices[i] = r.ReadInt32();
            }

            return model;
        }

        static void WriteBSP(BinaryWriter w, BSPFile file)
        {
            w.Write(file.Nodes.Length);
            foreach (var n in file.Nodes)
            {
                w.Write(n.nodeFlag);
                w.Write(n.spx); w.Write(n.spy); w.Write(n.spz); w.Write(n.d);
                w.Write(n.front); w.Write(n.back); w.Write(n.parent); w.Write(n.id);
                w.Write(n.solid); w.Write(n.split);
                w.Write(n.brush); w.Write(n.face);
                w.Write(n.bnx); w.Write(n.bny); w.Write(n.bnz);
            }
        }

        static BSPFile ReadBSP(BinaryReader r)
        {
            var nodes = new BSPNode[r.ReadInt32()];
            for (int i = 0; i < nodes.Length; i++)
            {
                var n = new BSPNode();
                n.nodeFlag = r.ReadByte();
                n.spx = r.ReadSingle(); n.spy = r.ReadSingle(); n.spz = r.ReadSingle(); n.d = r.ReadSingle();
                n.front = r.ReadUInt32(); n.back = r.ReadUInt32(); n.parent = r.ReadUInt32(); n.id = r.ReadUInt32();
                n.solid = r.ReadBoolean(); n.split = r.ReadBoolean();
                n.brush = r.ReadUInt16(); n.face = r.ReadByte();
                n.bnx = r.ReadSingle(); n.bny = r.ReadSingle(); n.bnz = r.ReadSingle();
                nodes[i] = n;
            }
            return new BSPFile { Nodes = nodes };
        }

        static void WriteVis(BinaryWriter w, VisFile file)
        {
            w.Write(file.Leaves.Length);
            foreach (var leaf in file.Leaves)
            {
                w.Write(leaf.IsEmpty); w.Write(leaf.HasSkybox); w.Write(leaf.BspLeafID);
                w.Write(leaf.Portals.Length);
                foreach (var p in leaf.Portals) w.Write(p);
                w.Write(leaf.PVS.Length);
                foreach (var p in leaf.PVS) w.Write(p);
                w.Write(leaf.Brushes.Length);
                foreach (var b in leaf.Brushes) w.Write(b);
            }

            w.Write(file.Portals.Length);
            foreach (var portal in file.Portals)
            {
                w.Write(portal.LeafFront); w.Write(portal.LeafBack);
                WriteVec3(w, portal.Plane.Normal); w.Write(portal.Plane.D);
                w.Write(portal.Vertices.Length);
                foreach (var v in portal.Vertices) WriteVec3(w, v);
                w.Write(portal.Brushes.Length);
                foreach (var b in portal.Brushes) w.Write(b);
            }
        }

        static VisFile ReadVis(BinaryReader r)
        {
            var leaves = new VisLeaf[r.ReadInt32()];
            for (int i = 0; i < leaves.Length; i++)
            {
                var leaf = new VisLeaf();
                leaf.IsEmpty = r.ReadBoolean(); leaf.HasSkybox = r.ReadBoolean(); leaf.BspLeafID = r.ReadInt32();
                leaf.Portals = new int[r.ReadInt32()];
                for (int j = 0; j < leaf.Portals.Length; j++) leaf.Portals[j] = r.ReadInt32();
                leaf.PVS = new uint[r.ReadInt32()];
                for (int j = 0; j < leaf.PVS.Length; j++) leaf.PVS[j] = r.ReadUInt32();
                leaf.Brushes = new ushort[r.ReadInt32()];
                for (int j = 0; j < leaf.Brushes.Length; j++) leaf.Brushes[j] = r.ReadUInt16();
                leaves[i] = leaf;
            }

            var portals = new Portal[r.ReadInt32()];
            for (int i = 0; i < portals.Length; i++)
            {
                var portal = new Portal();
                portal.LeafFront = r.ReadInt32(); portal.LeafBack = r.ReadInt32();
                portal.Plane = new Plane(ReadVec3(r), r.ReadSingle());
                portal.Vertices = new Vector3[r.ReadInt32()];
                for (int j = 0; j < portal.Vertices.Length; j++) portal.Vertices[j] = ReadVec3(r);
                portal.Brushes = new ushort[r.ReadInt32()];
                for (int j = 0; j < portal.Brushes.Length; j++) portal.Brushes[j] = r.ReadUInt16();
                portals[i] = portal;
            }

            return new VisFile { Leaves = leaves, Portals = portals };
        }

        static void WriteBrush(BinaryWriter w, Brush b)
        {
            WriteVec3(w, b.Position);

            // Pack the seven bool flags into one byte rather than seven.
            byte flags = 0;
            if (b.Abnormal) flags |= 1 << 0;
            if (b.IsDetail) flags |= 1 << 1;
            if (b.IsClip) flags |= 1 << 2;
            if (b.IsTrigger) flags |= 1 << 3;
            if (b.IsSkybox) flags |= 1 << 4;
            if (b.IsLightNodeVolume) flags |= 1 << 5;
            if (b.IsEntity) flags |= 1 << 6;
            w.Write(flags);

            w.Write(b.Vertices.Length);
            foreach (var v in b.Vertices) WriteVec3(w, v);

            w.Write(b.UVs.Length);
            foreach (var uv in b.UVs) WriteVec2(w, uv);

            w.Write(b.LightmapUVs.Length);
            foreach (var uv in b.LightmapUVs) WriteVec2(w, uv);

            w.Write(b.Faces.Length);
            foreach (var f in b.Faces) WriteFace(w, f);
        }

        static Brush ReadBrush(BinaryReader r)
        {
            var b = new Brush();
            b.Position = ReadVec3(r);

            byte flags = r.ReadByte();
            b.Abnormal = (flags & (1 << 0)) != 0;
            b.IsDetail = (flags & (1 << 1)) != 0;
            b.IsClip = (flags & (1 << 2)) != 0;
            b.IsTrigger = (flags & (1 << 3)) != 0;
            b.IsSkybox = (flags & (1 << 4)) != 0;
            b.IsLightNodeVolume = (flags & (1 << 5)) != 0;
            b.IsEntity = (flags & (1 << 6)) != 0;

            b.Vertices = new Vector3[r.ReadInt32()];
            for (int i = 0; i < b.Vertices.Length; i++) b.Vertices[i] = ReadVec3(r);

            b.UVs = new Vector2[r.ReadInt32()];
            for (int i = 0; i < b.UVs.Length; i++) b.UVs[i] = ReadVec2(r);

            b.LightmapUVs = new Vector2[r.ReadInt32()];
            for (int i = 0; i < b.LightmapUVs.Length; i++) b.LightmapUVs[i] = ReadVec2(r);

            b.Faces = new Face[r.ReadInt32()];
            for (int i = 0; i < b.Faces.Length; i++) b.Faces[i] = ReadFace(r);

            return b;
        }

        static void WriteFace(BinaryWriter w, Face f)
        {
            WriteVec3(w, f.Normal); WriteVec3(w, f.Tangent); WriteVec3(w, f.Binormal);
            WriteVec3(w, f.Basis1); WriteVec3(w, f.Basis2); WriteVec3(w, f.Basis3);
            w.Write(f.Drawn); w.Write(f.Surface);
            w.Write(f.TOffX); w.Write(f.TOffY); w.Write(f.TScaleX); w.Write(f.TScaleY);
            w.Write(f.LuxelScale);
            WriteStr(w, f.MaterialName);

            w.Write(f.Indices.Length);
            foreach (var idx in f.Indices) w.Write(idx);

            int decalCount = f.Decals?.Length ?? 0;
            w.Write(decalCount);
            for (int i = 0; i < decalCount; i++)
            {
                w.Write(f.Decals[i].surface);
                w.Write(f.Decals[i].vertices.Length);
                foreach (var v in f.Decals[i].vertices) WriteVertexLM(w, v);
            }
        }

        static Face ReadFace(BinaryReader r)
        {
            var f = new Face();
            f.Normal = ReadVec3(r); f.Tangent = ReadVec3(r); f.Binormal = ReadVec3(r);
            f.Basis1 = ReadVec3(r); f.Basis2 = ReadVec3(r); f.Basis3 = ReadVec3(r);
            f.Drawn = r.ReadBoolean(); f.Surface = r.ReadInt32();
            f.TOffX = r.ReadSingle(); f.TOffY = r.ReadSingle();
            f.TScaleX = r.ReadSingle(); f.TScaleY = r.ReadSingle();
            f.LuxelScale = r.ReadSingle();
            f.MaterialName = ReadStr(r);

            f.Indices = new int[r.ReadInt32()];
            for (int i = 0; i < f.Indices.Length; i++) f.Indices[i] = r.ReadInt32();

            f.Decals = new EnvironmentalDecal[r.ReadInt32()];
            for (int i = 0; i < f.Decals.Length; i++)
            {
                f.Decals[i].surface = r.ReadInt32();
                f.Decals[i].vertices = new VertexLightmapped[r.ReadInt32()];
                for (int j = 0; j < f.Decals[i].vertices.Length; j++) f.Decals[i].vertices[j] = ReadVertexLM(r);
            }

            return f;
        }

        static void WriteTerrain(BinaryWriter w, Terrain t)
        {
            w.Write(t.Surface); w.Write(t.BlendedSurface);
            w.Write(t.SurfaceName); w.Write(t.BlendedSurfaceName);
            w.Write(t.BrushSource); w.Write(t.FaceSource);
            WriteBBox(w, t.Bounds);
            w.Write(t.Vertices.Length);
            foreach (var v in t.Vertices) WriteTerrainVert(w, v);
            w.Write(t.Triangles.Length);
            foreach (var tri in t.Triangles) w.Write(tri);
        }

        static Terrain ReadTerrain(BinaryReader r)
        {
            var t = new Terrain();
            t.Surface = r.ReadInt32(); t.BlendedSurface = r.ReadInt32();
            t.SurfaceName = r.ReadString(); t.BlendedSurfaceName = r.ReadString();
            t.BrushSource = r.ReadInt32(); t.FaceSource = r.ReadInt32();
            t.Bounds = ReadBBox(r);
            t.Vertices = new TerrainVertex[r.ReadInt32()];
            for (int i = 0; i < t.Vertices.Length; i++) t.Vertices[i] = ReadTerrainVert(r);
            t.Triangles = new short[r.ReadInt32()];
            for (int i = 0; i < t.Triangles.Length; i++) t.Triangles[i] = r.ReadInt16();
            return t;
        }

        static void WriteEntityRef(BinaryWriter w, EntityReference e)
        {
            WriteVec3(w, e.Position); WriteVec3(w, e.SpawnRotation); WriteVec3(w, e.Scale);
            WriteStr(w, e.EntityName ?? "");
            WriteStr(w, e.Name ?? "");
            WriteStr(w, e.entityMoveParentName ?? "");

            int propCount = e.Properties?.Length ?? 0;
            w.Write(propCount);
            for (int i = 0; i < propCount; i++)
            {
                WriteStr(w, e.Properties[i].Name);
                WriteStr(w, e.Properties[i].Value);
            }

            int outCount = e.EntityOutputs?.Count ?? 0;
            w.Write(outCount);
            for (int i = 0; i < outCount; i++)
            {
                var (evtName, output) = e.EntityOutputs[i];
                WriteStr(w, evtName);
                w.Write(output.Delay); w.Write(output.Refire);
                WriteStr(w, output.EntityTarget);
                WriteStr(w, output.EntityInputTarget);
                WriteStr(w, output.InputParameters);
                WriteStr(w, output.ScriptSource ?? "NULLSCRIPT");
            }

            int brushCount = e.BrushIndices?.Count ?? 0;
            w.Write(brushCount);
            for (int i = 0; i < brushCount; i++)
                w.Write(e.BrushIndices[i]);
        }

        static EntityReference ReadEntityRef(BinaryReader r)
        {
            var e = new EntityReference();
            e.Position = ReadVec3(r); e.SpawnRotation = ReadVec3(r); e.Scale = ReadVec3(r);
            e.EntityName = ReadStr(r);
            e.Name = ReadStr(r);
            e.entityMoveParentName = ReadStr(r);

            int propCount = r.ReadInt32();
            if (propCount > 0)
            {
                e.Properties = new EntityProperty[propCount];
                for (int i = 0; i < propCount; i++)
                {
                    var p = new EntityProperty();
                    p.Name = ReadStr(r); p.Value = ReadStr(r);
                    e.Properties[i] = p;
                }
            }

            int outCount = r.ReadInt32();
            if (outCount > 0)
            {
                e.EntityOutputs = new List<(string, EntityOutput)>(outCount);
                for (int i = 0; i < outCount; i++)
                {
                    string evtName = ReadStr(r);
                    var output = new EntityOutput();
                    output.Delay = r.ReadSingle(); output.Refire = r.ReadInt32();
                    output.EntityTarget = ReadStr(r);
                    output.EntityInputTarget = ReadStr(r);
                    output.InputParameters = ReadStr(r);
                    var script = ReadStr(r);
                    output.ScriptSource = script == "NULLSCRIPT" ? null : script;
                    e.EntityOutputs.Add((evtName, output));
                }
            }

            int brushCount = r.ReadInt32();
            if (brushCount > 0)
            {
                e.BrushIndices = new List<int>(brushCount);
                for (int i = 0; i < brushCount; i++)
                    e.BrushIndices.Add(r.ReadInt32());
            }

            return e;
        }

        static void WriteOctree(BinaryWriter w, Octree oct)
        {
            WriteBBox(w, oct.Box);
            w.Write(oct.IsEnd);
            // children is always exactly 8 elements so no length prefix needed
            foreach (var c in oct.Children) w.Write(c);
            w.Write(oct.Corners.Length);
            foreach (var c in oct.Corners) WriteBBox(w, c);
            w.Write(oct.Contents.Count);
            foreach (var c in oct.Contents) w.Write(c);
        }

        static Octree ReadOctree(BinaryReader r)
        {
            var oct = new Octree();
            oct.Box = ReadBBox(r);
            oct.IsEnd = r.ReadBoolean();
            oct.Children = new int[8];
            for (int i = 0; i < 8; i++) oct.Children[i] = r.ReadInt32();
            oct.Corners = new BoundingBox[r.ReadInt32()];
            for (int i = 0; i < oct.Corners.Length; i++) oct.Corners[i] = ReadBBox(r);
            oct.Contents = new List<int>();
            var contentLength = r.ReadInt32();
            for (int i = 0; i < contentLength; i++) oct.Contents.Add(r.ReadInt32());
            return oct;
        }

        static void WriteLightNodeBundle(BinaryWriter w, LightNodeBundle bundle)
        {
            WriteBBox(w, bundle.Box);
            w.Write(bundle.Children.Length);
            foreach (var child in bundle.Children)
            {
                WriteVec3(w, child.Pos);
                w.Write(child.Data.Length);
                foreach (var d in child.Data) { w.Write(d.LightBlocked); w.Write(d.LightNum); }
                w.Write(child.IndirectCoefficients.Length);
                foreach (var c in child.IndirectCoefficients) WriteVec3(w, c);

                w.Write(child.GroupIndirectCoefficients.Length);
                foreach (var groupCoeffs in child.GroupIndirectCoefficients)
                {
                    w.Write(groupCoeffs.Length);
                    foreach (var c in groupCoeffs) WriteVec3(w, c);
                }
            }
        }

        static LightNodeBundle ReadLightNodeBundle(BinaryReader r)
        {
            var bundle = new LightNodeBundle(ReadBBox(r));
            bundle.Children = new LightNodeBundle.LightNode[r.ReadInt32()];
            for (int i = 0; i < bundle.Children.Length; i++)
            {
                var child = new LightNodeBundle.LightNode();
                child.Pos = ReadVec3(r);
                child.Data = new LightNodeBundle.LightData[r.ReadInt32()];
                for (int j = 0; j < child.Data.Length; j++)
                    child.Data[j] = new LightNodeBundle.LightData { LightBlocked = r.ReadBoolean(), LightNum = r.ReadInt32() };
                child.IndirectCoefficients = new Vector3[r.ReadInt32()];
                for (int j = 0; j < child.IndirectCoefficients.Length; j++)
                    child.IndirectCoefficients[j] = ReadVec3(r);

                child.GroupIndirectCoefficients = new Vector3[r.ReadInt32()][];
                for (int g = 0; g < child.GroupIndirectCoefficients.Length; g++)
                {
                    var coeffs = new Vector3[r.ReadInt32()];
                    for (int j = 0; j < coeffs.Length; j++) coeffs[j] = ReadVec3(r);
                    child.GroupIndirectCoefficients[g] = coeffs;
                }

                bundle.Children[i] = child;
            }
            return bundle;
        }

        static void WriteNodeGraph(BinaryWriter w, NodeGraph graph)
        {
            w.Write(graph.Nodes.Length);
            foreach (var n in graph.Nodes)
            {
                WriteVec3(w, n.Position);
                int connCount = n.Connections?.Length ?? 0;
                w.Write(connCount);
                for (int i = 0; i < connCount; i++) w.Write(n.Connections[i]);
            }
        }

        static NodeGraph ReadNodeGraph(BinaryReader r)
        {
            var nodes = new AINode[r.ReadInt32()];
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i].Position = ReadVec3(r);
                nodes[i].Connections = new int[r.ReadInt32()];
                for (int j = 0; j < nodes[i].Connections.Length; j++)
                    nodes[i].Connections[j] = r.ReadInt32();
            }
            return new NodeGraph { Nodes = nodes };
        }

        static void WriteVec3(BinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
        static void WriteVec2(BinaryWriter w, Vector2 v) { w.Write(v.X); w.Write(v.Y); }
        static void WriteBBox(BinaryWriter w, BoundingBox b) { WriteVec3(w, b.Min); WriteVec3(w, b.Max); }
        static void WriteShort4(BinaryWriter w, NormalizedShort4 v) { w.Write(v.PackedValue); }

        static Vector3 ReadVec3(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        static Vector2 ReadVec2(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle());
        static BoundingBox ReadBBox(BinaryReader r) => new(ReadVec3(r), ReadVec3(r));
        static NormalizedShort4 ReadShort4(BinaryReader r) => new() { PackedValue = r.ReadUInt64()};

        static void WriteStr(BinaryWriter w, string s)
        {
            if (string.IsNullOrEmpty(s)) { w.Write(0); return; }
            var bytes = Encoding.UTF8.GetBytes(s);
            w.Write(bytes.Length);
            w.Write(bytes);
        }
        static string ReadStr(BinaryReader r)
        {
            int len = r.ReadInt32();
            return len == 0 ? string.Empty : Encoding.UTF8.GetString(r.ReadBytes(len));
        }
        static void WriteVertexLM(BinaryWriter w, VertexLightmapped v)
        {
            WriteVec3(w, v.Position); WriteVec3(w, v.Normal);
            WriteVec3(w, v.Tangent); WriteVec3(w, v.Binormal);
            WriteVec2(w, v.TextureCoordinate); WriteVec2(w, v.LightmapCoordinate);
        }
        static VertexLightmapped ReadVertexLM(BinaryReader r) => new()
        {
            Position = ReadVec3(r),
            Normal = ReadVec3(r),
            Tangent = ReadVec3(r),
            Binormal = ReadVec3(r),
            TextureCoordinate = ReadVec2(r),
            LightmapCoordinate = ReadVec2(r),
        };

        static void WriteVertexCTN(BinaryWriter w, VertexPositionColorNormalTexture v)
        {
            WriteVec3(w, v.Position); WriteVec3(w, v.Normal);
            WriteVec2(w, v.TextureCoordinate); WriteVec3(w, v.Color.ToVector3());
        }
        static VertexPositionColorNormalTexture ReadVertexCTN(BinaryReader r) => new()
        {
            Position = ReadVec3(r),
            Normal = ReadVec3(r),
            TextureCoordinate = ReadVec2(r),
            Color = new Color(ReadVec3(r)),
        };

        static void WritePropVertex(BinaryWriter w, MapPropModelVertex v)
        {
            WriteVec3(w, v.Position); WriteVec3(w, v.Normal);
            WriteVec2(w, v.TextureCoordinate); WriteVec3(w, v.Color);
        }
        static MapPropModelVertex ReadPropVertex(BinaryReader r) => new()
        {
            Position = ReadVec3(r),
            Normal = ReadVec3(r),
            TextureCoordinate = ReadVec2(r),
            Color = ReadVec3(r),
        };

        static void WriteTerrainVert(BinaryWriter w, TerrainVertex v)
        {
            WriteVec3(w, v.Position); WriteVec3(w, v.Normal); WriteVec3(w, v.TextureCoordinate); WriteVec2(w, v.LightmapCoordinate); WriteShort4(w, v.Tangent);
        }
        static TerrainVertex ReadTerrainVert(BinaryReader r) => new()
        {
            Position = ReadVec3(r),
            Normal = ReadVec3(r),
            TextureCoordinate = ReadVec3(r),
            LightmapCoordinate = ReadVec2(r),
            Tangent = ReadShort4(r)
        };
    }
}
#endif