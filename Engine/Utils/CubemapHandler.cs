using Engine.Entities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using System.Collections.Generic;
using System.Linq;

namespace Engine.Utils
{
    public static class CubemapHandler
    {
        public static Dictionary<int, EnvCubemap> brushCubemaps = new Dictionary<int, EnvCubemap>();
        public static Dictionary<(int b, int f), EnvCubemap> faceCubemaps = new Dictionary<(int b, int f), EnvCubemap>();
        public static int GetNearestCubemapIndex(Vector3 pos)
        {
            if (EnvCubemap.Cubemaps == null || EnvCubemap.Cubemaps.Count == 0 || EnvCubemap.cubeRendering) return -1;

            float mindist = float.MaxValue;
            int? cubemap = null;
            for (int i = 0; i < EnvCubemap.Cubemaps.Count; i++)
            {
                float dist = Vector3.DistanceSquared(EnvCubemap.Cubemaps[i].Position, pos);
                if (dist < mindist)
                {
                    mindist = dist;
                    cubemap = i;
                }
            }

            return cubemap ?? -1;
        }
        public static EnvCubemap GetNearestCubemap(Vector3 pos)
        {
            if (EnvCubemap.Cubemaps == null || EnvCubemap.Cubemaps.Count == 0 || EnvCubemap.cubeRendering) return null;

            float mindist = float.MaxValue;
            EnvCubemap cubemap = null;
            for(int i = 0; i < EnvCubemap.Cubemaps.Count; i++)
            {
                float dist = Vector3.DistanceSquared(EnvCubemap.Cubemaps[i].Position,pos);
                if(dist < mindist)
                {
                    mindist = dist;
                    cubemap = EnvCubemap.Cubemaps[i];
                }
            }

            return cubemap;
        }
        public static EnvCubemap GetBrushCubemap(Vector3 pos, int brush = -1)
        {
            if (EnvCubemap.Cubemaps == null || EnvCubemap.Cubemaps.Count == 0 || EnvCubemap.cubeRendering) return null;
            if (brush != -1 && brushCubemaps.TryGetValue(brush, out var cubemap)) return cubemap;

            float mindist = float.MaxValue;
            cubemap = null;
            for (int i = 0; i < EnvCubemap.Cubemaps.Count; i++)
            {
                float dist = Vector3.DistanceSquared(EnvCubemap.Cubemaps[i].Position, pos);
                if (dist < mindist)
                {
                    mindist = dist;
                    cubemap = EnvCubemap.Cubemaps[i];
                }
            }
            if (brush != -1) brushCubemaps.Add(brush, cubemap);

            return cubemap;
        }
        public static EnvCubemap GetFaceCubemap(int brush = -1, int face = -1)
        {
            if (EnvCubemap.Cubemaps == null || EnvCubemap.Cubemaps.Count == 0 || EnvCubemap.cubeRendering) return null;
            if (brush == -1 || face == -1) return null;
            if (faceCubemaps.TryGetValue((brush,face), out var cubemap)) return cubemap;

            var brushObj = GlobalMapData.ActiveMap.Brushes[brush];
            var verts = brushObj.Faces[face].Indices.Select(i => brushObj.Vertices[i]);
            Vector3 pos = Vector3.Zero;
            foreach (var vert in verts) pos += vert;
            pos /= verts.Count();
            pos += brushObj.Position;

            float mindist = float.MaxValue;
            cubemap = null;
            for (int i = 0; i < EnvCubemap.Cubemaps.Count; i++)
            {
                float dist = Vector3.DistanceSquared(EnvCubemap.Cubemaps[i].Position, pos);
                if (dist < mindist)
                {
                    mindist = dist;
                    cubemap = EnvCubemap.Cubemaps[i];
                }
            }
            if (brush != -1) faceCubemaps.Add((brush, face), cubemap);

            return cubemap;
        }
    }
}
