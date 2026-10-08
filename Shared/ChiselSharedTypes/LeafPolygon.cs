using Chisel.Utils;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

public class LeafPolygon
{
    public int VertexStart, VertexCount;
    public int MaterialID;
    public string MaterialName;
    public int RuntimeCubemapID = -1;
    public Vector3 Normal, Tangent, Binormal, B1, B2, B3;
}
public class WorkingLeafPoly
{
    public List<Vector3> Vertices = new();
    public List<Vector2> UVs = new();
    public List<Vector2> LightmapUVs = new();
    public List<Vector3> VertexNormals = new();
    public List<Vector3> VertexTangents = new();
    public List<Vector3> VertexBinormals = new();
    public List<int> Indices = new();

    public int BrushIndex;
    public int PendingFaceIndex;

    public int LeafIndex;
    public int MaterialID;
    public string MaterialName;
    public Vector3 Normal, Tangent, Binormal, B1, B2, B3;
}