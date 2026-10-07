using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapCompiler.Compilation;
public static class Vector3Extensions
{
    public static float GetElement(this Vector3 v, int axis)
    {
        if (axis == 0) return v.X;
        if (axis == 1) return v.Y;
        return v.Z;
    }
}