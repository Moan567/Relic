using Microsoft.Xna.Framework;
using Rockwall;
using Rockwall2.Editor.Mapper;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Rockwall2.Editor.Common;

public class Vector3Comparer : IEqualityComparer<Vector3>
{
    float _eps;
    public Vector3Comparer(float eps) { _eps = eps; }
    public bool Equals(Vector3 a, Vector3 b) => Vector3.DistanceSquared(a, b) < _eps * _eps;
    public int GetHashCode(Vector3 v) => 0; // force Equals check
}
public static class OtherMath
{
    public const float Deg2Rad = MathF.PI / 180f;
    public static Vector3 SnapVector(Vector3 v)
    {
        float gs = Transformable.GridSize;
        return new Vector3(MathF.Round(v.X / gs) * gs, MathF.Round(v.Y / gs) * gs, MathF.Round(v.Z / gs) * gs);
    }
    static void ClipVertToPlane(Vector3 a, Vector3 b, float da, float db, List<Vector3> output)
    {
        bool aFront = da > -0.01f;
        bool bFront = db > -0.01f;

        if (aFront) output.Add(a);

        if (aFront != bFront)
        {
            float t = da / (da - db);
            output.Add(a + t * (b - a));
        }
    }
    public static float SnapToGridPlane(Ray ray, float distance)
    {
        float snappedDistance = float.MaxValue;

        var x = ray.Position + ray.Direction * distance;

        var c = Vector3.Round(x / Transformable.GridSize) * Transformable.GridSize;

        for (int i = 0; i < 3; i++)
        {
            var p = new Plane(c, i switch
            {
                0 => Vector3.UnitX,
                1 => Vector3.UnitY,
                2 => Vector3.UnitZ,
                _ => Vector3.Zero,
            });
            var y = ray.Intersects(p);
            if (y.HasValue && float.Abs(y.Value - distance) < float.Abs(snappedDistance - distance))
            {
                snappedDistance = y.Value;
            }
        }

        return snappedDistance;
    }

    public static bool ClipSegmentToNearPlane(Vector3 a, Vector3 b,
                                              Matrix view, Matrix proj,
                                              out Vector3 clippedA, out Vector3 clippedB)
    {
        Vector4 va = Vector4.Transform(new Vector4(a, 1), view);
        Vector4 vb = Vector4.Transform(new Vector4(b, 1), view);

        float nearZ = 0.1f;

        if (va.Z >= -nearZ && vb.Z >= -nearZ)
        {
            clippedA = a; clippedB = b;
            return false;
        }

        if (va.Z >= -nearZ)
        {
            float t = (-nearZ - va.Z) / (vb.Z - va.Z);
            a = Vector3.Lerp(a, b, t);
        }
        else if (vb.Z >= -nearZ)
        {
            float t = (-nearZ - va.Z) / (vb.Z - va.Z);
            b = Vector3.Lerp(a, b, t);
        }

        clippedA = a; clippedB = b;
        return true;
    }
    private static readonly List<int> polygonScratch = new(64);
    public static (int, int)[] GetUniqueEdges(int brushID)
    {
        return GetUniqueEdges(MapTools.Brushes[brushID]);
    }
    public static (int, int)[] GetUniqueEdges(Brush br)
    {
        var set = new HashSet<(int, int)>();
        PopulateUniqueEdges(br, set);
        var arr = new (int, int)[set.Count];
        set.CopyTo(arr);
        return arr;
    }
    public static void PopulateUniqueEdges(Brush br, HashSet<(int, int)> result)
    {
        result.Clear();

        foreach (var face in br.Faces)
        {
            var indices = face.Indices;
            if (indices.Length < 2) continue;

            polygonScratch.Clear();
            foreach (var idx in indices)
            {
                if (!polygonScratch.Contains(idx))
                    polygonScratch.Add(idx);
            }

            int count = polygonScratch.Count;
            if (count < 2) continue;

            Vector3 center = Vector3.Zero;
            for (int i = 0; i < count; i++)
                center += br.Vertices[polygonScratch[i]];
            center /= count;

            Vector3 refAxis = Vector3.Cross(face.Normal, Vector3.UnitZ);
            if (refAxis.LengthSquared() < 1e-6f)
                refAxis = Vector3.Cross(face.Normal, Vector3.UnitX);
            refAxis = Vector3.Normalize(refAxis);
            Vector3 perpAxis = Vector3.Normalize(Vector3.Cross(face.Normal, refAxis));

            var verts = br.Vertices;
            var rA = refAxis;
            var pA = perpAxis;
            var c = center;
            polygonScratch.Sort((a, b) =>
            {
                var da = verts[a] - c;
                var db = verts[b] - c;
                float aAng = MathF.Atan2(Vector3.Dot(da, pA), Vector3.Dot(da, rA));
                float bAng = MathF.Atan2(Vector3.Dot(db, pA), Vector3.Dot(db, rA));
                return aAng.CompareTo(bAng);
            });

            for (int e = 0; e < count; e++)
            {
                int a = polygonScratch[e];
                int b = polygonScratch[(e + 1) % count];
                result.Add(a < b ? (a, b) : (b, a));
            }
        }
    }
    public static float PointToSegmentDist(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var ap = p - a;
        float t = Math.Clamp(Vector2.Dot(ap, ab) / Vector2.Dot(ab, ab), 0f, 1f);
        return (ap - ab * t).Length();
    }
    public static (Vector2 axis, float pixelsPerWorldUnit) ProjectAxisToScreen(Vector3 pos, Vector3 normal)
    {
        Vector3 ws0 = pos;
        Vector3 ws1 = pos + normal;
        Vector3 ss0 = EditorHost.Instance.GraphicsDevice.Viewport.Project(ws0, Viewport3DCamera.projectionMatrix, Viewport3DCamera.viewMatrix, Viewport3DCamera.worldMatrix);
        Vector3 ss1 = EditorHost.Instance.GraphicsDevice.Viewport.Project(ws1, Viewport3DCamera.projectionMatrix, Viewport3DCamera.viewMatrix, Viewport3DCamera.worldMatrix);

        var projectedAxis = Vector2.Normalize(new Vector2(ss1.X, ss1.Y) - new Vector2(ss0.X, ss0.Y));
        var pixelsPerWorldUnit = Vector2.Distance(new Vector2(ss1.X, ss1.Y), new Vector2(ss0.X, ss0.Y));

        //foreach (var obj in Toolbelt.SelectedObjects)
        //{
        //    obj?.PrepareMove();
        //}

        return (projectedAxis, pixelsPerWorldUnit);
    }

    public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDistanceDelta)
    {
        Vector3 a = target - current;
        float magnitude = a.Length();
        if (magnitude <= maxDistanceDelta || magnitude == 0f)
        {
            return target;
        }
        return current + a / magnitude * maxDistanceDelta;
    }
    public static float MoveTowards(float current, float target, float maxDelta)
    {
        if (MathF.Abs(target - current) <= maxDelta)
        {
            return target;
        }
        return current + MathF.Sign(target - current) * maxDelta;
    }
    public static float MoveTowardsAngle(float current, float target, float maxDelta)
    {
        float deltaAngle = DeltaAngle(current, target);
        if (-maxDelta < deltaAngle && deltaAngle < maxDelta)
            return target;
        target = current + deltaAngle;
        return MoveTowards(current, target, maxDelta);
    }
    public static float DeltaAngle(float current, float target)
    {
        float delta = Repeat(target - current, 360.0F);
        if (delta > 180.0F)
            delta -= 360.0F;
        return delta;
    }
    public static float Repeat(float t, float length)
    {
        return float.Clamp(t - float.Floor(t / length) * length, 0.0f, length);
    }
    public static Vector3 ToEulerAngles(Quaternion q)
    {
        Vector3 angles = new Vector3();

        // roll / x
        double sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
        double cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
        angles.X = (float)Math.Atan2(sinr_cosp, cosr_cosp);

        // pitch / y
        double sinp = 2 * (q.W * q.Y - q.Z * q.X);
        if (Math.Abs(sinp) >= 1)
        {
            angles.Y = (float)(Math.Abs(Math.PI / 2) * Math.Sign(sinp));
        }
        else
        {
            angles.Y = (float)Math.Asin(sinp);
        }

        // yaw / z
        double siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
        double cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
        angles.Z = (float)Math.Atan2(siny_cosp, cosy_cosp);

        return angles;
    }
    public static Quaternion ToQuaternion(Vector3 v)
    {

        float cy = (float)Math.Cos(v.Z * 0.5);
        float sy = (float)Math.Sin(v.Z * 0.5);
        float cp = (float)Math.Cos(v.Y * 0.5);
        float sp = (float)Math.Sin(v.Y * 0.5);
        float cr = (float)Math.Cos(v.X * 0.5);
        float sr = (float)Math.Sin(v.X * 0.5);

        return new Quaternion
        {
            W = cr * cp * cy + sr * sp * sy,
            X = sr * cp * cy - cr * sp * sy,
            Y = cr * sp * cy + sr * cp * sy,
            Z = cr * cp * sy - sr * sp * cy
        };

    }
    public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
    {
        return a + (b - a) * t;
    }
    public static Vector3 ClampToBoundingBox(Vector3 point, BoundingBox box)
    {
        return new Vector3(MathF.Max(MathF.Min(point.X, box.Max.X), box.Min.X),
                           MathF.Max(MathF.Min(point.Y, box.Max.Y), box.Min.Y),
                           MathF.Max(MathF.Min(point.Z, box.Max.Z), box.Min.Z));
    }
    public static Vector3 Project(Vector3 vector, Vector3 onNormal)
    {
        float sqrMag = Vector3.Dot(onNormal, onNormal);
        if (sqrMag < float.Epsilon)
            return Vector3.Zero;
        else
        {
            var dot = Vector3.Dot(vector, onNormal);
            return new Vector3(onNormal.X * dot / sqrMag,
                onNormal.Y * dot / sqrMag,
                onNormal.Z * dot / sqrMag);
        }
    }
    public static Vector3 ProjectOnPlane(Vector3 vector, Vector3 planeNormal)
    {
        float sqrMag = Vector3.Dot(planeNormal, planeNormal);
        if (sqrMag < float.Epsilon)
            return vector;
        else
        {
            var dot = Vector3.Dot(vector, planeNormal);
            return new Vector3(vector.X - planeNormal.X * dot / sqrMag,
                vector.Y - planeNormal.Y * dot / sqrMag,
                vector.Z - planeNormal.Z * dot / sqrMag);
        }
    }

    /// <summary>
    /// The value for which all absolute numbers smaller than are considered equal to zero.
    /// </summary>
    public const float ZeroTolerance = 1e-6f; // Value a 8x higher than 1.19209290E-07F

    /// <summary>
    /// A value specifying the approximation of π which is 180 degrees.
    /// </summary>
    public const float Pi = (float)Math.PI;

    /// <summary>
    /// A value specifying the approximation of 2π which is 360 degrees.
    /// </summary>
    public const float TwoPi = (float)(2 * Math.PI);

    /// <summary>
    /// A value specifying the approximation of π/2 which is 90 degrees.
    /// </summary>
    public const float PiOverTwo = (float)(Math.PI / 2);

    /// <summary>
    /// A value specifying the approximation of π/4 which is 45 degrees.
    /// </summary>
    public const float PiOverFour = (float)(Math.PI / 4);

    /// <summary>
    /// Determines whether the specified value is close to zero (0.0f).
    /// </summary>
    /// <param name="a">The floating value.</param>
    /// <returns><c>true</c> if the specified value is close to zero (0.0f); otherwise, <c>false</c>.</returns>
    public static bool IsZero(float a)
    {
        return Math.Abs(a) < ZeroTolerance;
    }

    /// <summary>
    /// Determines whether the specified value is close to one (1.0f).
    /// </summary>
    /// <param name="a">The floating value.</param>
    /// <returns><c>true</c> if the specified value is close to one (1.0f); otherwise, <c>false</c>.</returns>
    public static bool IsOne(float a)
    {
        return IsZero(a - 1.0f);
    }

    /// <summary>
    /// Checks if a - b are almost equals within a float epsilon.
    /// </summary>
    /// <param name="a">The left value to compare.</param>
    /// <param name="b">The right value to compare.</param>
    /// <param name="epsilon">Epsilon value</param>
    /// <returns><c>true</c> if a almost equal to b within a float epsilon, <c>false</c> otherwise</returns>
    public static bool WithinEpsilon(float a, float b, float epsilon)
    {
        float num = a - b;
        return -epsilon <= num && num <= epsilon;
    }

    /// <summary>
    /// Converts revolutions to degrees.
    /// </summary>
    /// <param name="revolution">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static float RevolutionsToDegrees(float revolution)
    {
        return revolution * 360.0f;
    }

    /// <summary>
    /// Converts revolutions to radians.
    /// </summary>
    /// <param name="revolution">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static float RevolutionsToRadians(float revolution)
    {
        return revolution * TwoPi;
    }

    /// <summary>
    /// Converts revolutions to gradians.
    /// </summary>
    /// <param name="revolution">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static float RevolutionsToGradians(float revolution)
    {
        return revolution * 400.0f;
    }

    /// <summary>
    /// Converts degrees to revolutions.
    /// </summary>
    /// <param name="degree">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static float DegreesToRevolutions(float degree)
    {
        return degree / 360.0f;
    }

    /// <summary>
    /// Converts degrees to radians.
    /// </summary>
    /// <param name="degree">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static float DegreesToRadians(float degree)
    {
        return degree * (Pi / 180.0f);
    }

    /// <summary>
    /// Converts radians to revolutions.
    /// </summary>
    /// <param name="radian">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static float RadiansToRevolutions(float radian)
    {
        return radian / TwoPi;
    }

    /// <summary>
    /// Converts radians to gradians.
    /// </summary>
    /// <param name="radian">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static float RadiansToGradians(float radian)
    {
        return radian * (200.0f / Pi);
    }

    /// <summary>
    /// Converts gradians to revolutions.
    /// </summary>
    /// <param name="gradian">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static float GradiansToRevolutions(float gradian)
    {
        return gradian / 400.0f;
    }

    /// <summary>
    /// Converts gradians to degrees.
    /// </summary>
    /// <param name="gradian">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static float GradiansToDegrees(float gradian)
    {
        return gradian * (9.0f / 10.0f);
    }

    /// <summary>
    /// Converts gradians to radians.
    /// </summary>
    /// <param name="gradian">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static float GradiansToRadians(float gradian)
    {
        return gradian * (Pi / 200.0f);
    }

    /// <summary>
    /// Converts radians to degrees.
    /// </summary>
    /// <param name="radian">The value to convert.</param>
    /// <returns>The converted value.</returns>
    public static float RadiansToDegrees(float radian)
    {
        return radian * (180.0f / Pi);
    }

    /// <summary>
    /// Clamps the specified value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="min">The min.</param>
    /// <param name="max">The max.</param>
    /// <returns>The result of clamping a value between min and max</returns>
    public static float Clamp(float value, float min, float max)
    {
        return value < min ? min : value > max ? max : value;
    }

    /// <summary>
    /// Clamps the specified value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="min">The min.</param>
    /// <param name="max">The max.</param>
    /// <returns>The result of clamping a value between min and max</returns>
    public static int Clamp(int value, int min, int max)
    {
        return value < min ? min : value > max ? max : value;
    }

    /// <summary>
    /// Interpolates between two values using a linear function by a given amount.
    /// </summary>
    /// <remarks>
    /// See http://www.encyclopediaofmath.org/index.php/Linear_interpolation and
    /// http://fgiesen.wordpress.com/2012/08/15/linear-interpolation-past-present-and-future/
    /// </remarks>
    /// <param name="from">Value to interpolate from.</param>
    /// <param name="to">Value to interpolate to.</param>
    /// <param name="amount">Interpolation amount.</param>
    /// <returns>The result of linear interpolation of values based on the amount.</returns>
    public static double Lerp(double from, double to, double amount)
    {
        return (1 - amount) * from + amount * to;
    }

    /// <summary>
    /// Interpolates between two values using a linear function by a given amount.
    /// </summary>
    /// <remarks>
    /// See http://www.encyclopediaofmath.org/index.php/Linear_interpolation and
    /// http://fgiesen.wordpress.com/2012/08/15/linear-interpolation-past-present-and-future/
    /// </remarks>
    /// <param name="from">Value to interpolate from.</param>
    /// <param name="to">Value to interpolate to.</param>
    /// <param name="amount">Interpolation amount.</param>
    /// <returns>The result of linear interpolation of values based on the amount.</returns>
    public static float Lerp(float from, float to, float amount)
    {
        return (1 - amount) * from + amount * to;
    }

    /// <summary>
    /// Interpolates between two values using a linear function by a given amount.
    /// </summary>
    /// <remarks>
    /// See http://www.encyclopediaofmath.org/index.php/Linear_interpolation and
    /// http://fgiesen.wordpress.com/2012/08/15/linear-interpolation-past-present-and-future/
    /// </remarks>
    /// <param name="from">Value to interpolate from.</param>
    /// <param name="to">Value to interpolate to.</param>
    /// <param name="amount">Interpolation amount.</param>
    /// <returns>The result of linear interpolation of values based on the amount.</returns>
    public static byte Lerp(byte from, byte to, float amount)
    {
        return (byte)Lerp(from, (float)to, amount);
    }

    /// <summary>
    /// Performs smooth (cubic Hermite) interpolation between 0 and 1.
    /// </summary>
    /// <remarks>
    /// See https://en.wikipedia.org/wiki/Smoothstep
    /// </remarks>
    /// <param name="amount">Value between 0 and 1 indicating interpolation amount.</param>
    public static float SmoothStep(float amount)
    {
        return amount <= 0 ? 0
            : amount >= 1 ? 1
            : amount * amount * (3 - 2 * amount);
    }

    /// <summary>
    /// Performs a smooth(er) interpolation between 0 and 1 with 1st and 2nd order derivatives of zero at endpoints.
    /// </summary>
    /// <remarks>
    /// See https://en.wikipedia.org/wiki/Smoothstep
    /// </remarks>
    /// <param name="amount">Value between 0 and 1 indicating interpolation amount.</param>
    public static float SmootherStep(float amount)
    {
        return amount <= 0 ? 0
            : amount >= 1 ? 1
            : amount * amount * amount * (amount * (amount * 6 - 15) + 10);
    }

    /// <summary>
    /// Calculates the modulo of the specified value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="modulo">The modulo.</param>
    /// <returns>The result of the modulo applied to value</returns>
    public static float Mod(float value, float modulo)
    {
        if (modulo == 0.0f)
        {
            return value;
        }

        return value % modulo;
    }

    /// <summary>
    /// Calculates the modulo 2*PI of the specified value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The result of the modulo applied to value</returns>
    public static float Mod2PI(float value)
    {
        return Mod(value, TwoPi);
    }

    /// <summary>
    /// Wraps the specified value into a range [min, max]
    /// </summary>
    /// <param name="value">The value to wrap.</param>
    /// <param name="min">The min.</param>
    /// <param name="max">The max.</param>
    /// <returns>Result of the wrapping.</returns>
    /// <exception cref="ArgumentException">Is thrown when <paramref name="min"/> is greater than <paramref name="max"/>.</exception>
    public static int Wrap(int value, int min, int max)
    {
        if (min > max)
            throw new ArgumentException(string.Format("min {0} should be less than or equal to max {1}", min, max), "min");

        // Code from http://stackoverflow.com/a/707426/1356325
        int range_size = max - min + 1;

        if (value < min)
            value += range_size * ((min - value) / range_size + 1);

        return min + (value - min) % range_size;
    }

    /// <summary>
    /// Gauss function.
    /// http://en.wikipedia.org/wiki/Gaussian_function#Two-dimensional_Gaussian_function
    /// </summary>
    /// <param name="amplitude">Curve amplitude.</param>
    /// <param name="x">Position X.</param>
    /// <param name="y">Position Y</param>
    /// <param name="centerX">Center X.</param>
    /// <param name="centerY">Center Y.</param>
    /// <param name="sigmaX">Curve sigma X.</param>
    /// <param name="sigmaY">Curve sigma Y.</param>
    /// <returns>The result of Gaussian function.</returns>
    public static float Gauss(float amplitude, float x, float y, float centerX, float centerY, float sigmaX, float sigmaY)
    {
        return (float)Gauss((double)amplitude, x, y, centerX, centerY, sigmaX, sigmaY);
    }

    /// <summary>
    /// Gauss function.
    /// http://en.wikipedia.org/wiki/Gaussian_function#Two-dimensional_Gaussian_function
    /// </summary>
    /// <param name="amplitude">Curve amplitude.</param>
    /// <param name="x">Position X.</param>
    /// <param name="y">Position Y</param>
    /// <param name="centerX">Center X.</param>
    /// <param name="centerY">Center Y.</param>
    /// <param name="sigmaX">Curve sigma X.</param>
    /// <param name="sigmaY">Curve sigma Y.</param>
    /// <returns>The result of Gaussian function.</returns>
    public static double Gauss(double amplitude, double x, double y, double centerX, double centerY, double sigmaX, double sigmaY)
    {
        var cx = x - centerX;
        var cy = y - centerY;

        var componentX = cx * cx / (2 * sigmaX * sigmaX);
        var componentY = cy * cy / (2 * sigmaY * sigmaY);

        return amplitude * Math.Exp(-(componentX + componentY));
    }
}