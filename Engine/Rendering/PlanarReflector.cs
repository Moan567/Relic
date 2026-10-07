using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Rendering;
public struct PlanarReflectorFace
{
    public int BrushIndex;
    public int FaceIndex;
}
public class PlanarReflectorGroup
{
    public Plane ReflectionPlane;
    public BoundingBox Bounds;
    public List<PlanarReflectorFace> Faces = new();
    public RenderTarget2D ReflectionTarget;
    public bool SkyboxWasVisible;
}