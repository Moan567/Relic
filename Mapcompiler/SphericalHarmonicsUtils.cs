using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapCompiler;
public static class SphericalHarmonicsUtils
{
    public static float[] EvaluateSHBasis(Vector3 dir)
    {
        float[] basis = new float[9];

        // L0
        basis[0] = 0.282095f; // Y(0,0)

        // L1
        basis[1] = 0.488603f * dir.Y;  // Y(1,-1)
        basis[2] = 0.488603f * dir.Z;  // Y(1,0)
        basis[3] = 0.488603f * dir.X;  // Y(1,1)

        // L2
        basis[4] = 1.092548f * dir.X * dir.Y;  // Y(2,-2)
        basis[5] = 1.092548f * dir.Y * dir.Z;  // Y(2,-1)
        basis[6] = 0.315392f * (3f * dir.Z * dir.Z - 1f);  // Y(2,0)
        basis[7] = 1.092548f * dir.X * dir.Z;  // Y(2,1)
        basis[8] = 0.546274f * (dir.X * dir.X - dir.Y * dir.Y);  // Y(2,2)

        return basis;
    }

    public static Vector3 UniformSampleSphere(int index, int total)
    {
        float phi = MathF.PI * (3f - MathF.Sqrt(5f)); // Golden angle
        float y = 1f - (index / (float)(total - 1)) * 2f;
        float radius = MathF.Sqrt(1f - y * y);
        float theta = phi * index;

        float x = MathF.Cos(theta) * radius;
        float z = MathF.Sin(theta) * radius;

        return new Vector3(x, y, z);
    }
}
