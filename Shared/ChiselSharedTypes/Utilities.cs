using Chisel.Collision;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Chisel.Utils
{
    public static class Bitset
    {
        public static ulong[] Create(int bitCount) => new ulong[(bitCount + 63) >> 6];
        public static void Set(ulong[] b, int i) => b[i >> 6] |= 1UL << (i & 63);
        public static bool Get(ulong[] b, int i) => (b[i >> 6] & (1UL << (i & 63))) != 0;
        public static void And(ulong[] dst, ulong[] src) { for (int i = 0; i < dst.Length; i++) dst[i] &= src[i]; }
        public static void Or(ulong[] dst, ulong[] src) { for (int i = 0; i < dst.Length; i++) dst[i] |= src[i]; }
        public static bool AnyOutside(ulong[] test, ulong[] exclude)
        {
            for (int i = 0; i < test.Length; i++) if ((test[i] & ~exclude[i]) != 0) return true;
            return false;
        }
        public static bool Intersects(ulong[] a, ulong[] b)
        {
            int len = Math.Min(a.Length, b.Length);
            for (int i = 0; i < len; i++)
            {
                if ((a[i] & b[i]) != 0) return true;
            }
            return false;
        }
        public static List<uint> ToList(ulong[] b)
        {
            var r = new List<uint>();
            for (int w = 0; w < b.Length; w++)
            {
                ulong word = b[w];
                while (word != 0)
                {
                    int bit = System.Numerics.BitOperations.TrailingZeroCount(word);
                    r.Add((uint)(w * 64 + bit));
                    word &= word - 1;
                }
            }
            return r;
        }
    }

    [System.Serializable]
    public struct ValueRange<T>
    {
        public T min, max;
    }
    public class Box<T> { public T Value; }

    public class FixedList<T>
    {
        readonly T[] items;
        readonly T[] valueCache;
        readonly ulong[] occupied;
        readonly Stack<int> freeSlots;
        int count;
        int cachedCount;
        bool dirty = true;
        bool writeover = false;

        public int Count => count;
        public int Capacity => items.Length;

        public FixedList(int size, bool writeover = false)
        {
            items = new T[size];
            valueCache = new T[size];
            occupied = new ulong[(size + 63) / 64];
            freeSlots = new Stack<int>(size);
            for (int i = 0; i < size; i++)
            {
                freeSlots.Push(i);
            }
            this.writeover = writeover;
        }

        public int Add(T item)
        {
            if (freeSlots.Count == 0)
            {
                if (!writeover)
                {
                    return items.Length - 1;
                }
                for (int i = 0; i < items.Length; i++)
                {
                    freeSlots.Push(i);
                }
            }
            int slot = freeSlots.Pop();
            items[slot] = item;
            SetOccupied(slot, true);
            dirty = true;
            count++;
            return slot;
        }

        public void Remove(int index)
        {
            if ((uint)index >= (uint)items.Length) return;
            if (!IsOccupied(index)) return;

            items[index] = default;
            SetOccupied(index, false);
            freeSlots.Push(index);
            dirty = true;
            count--;
        }

        public void Remove(T item)
        {
            int index = FindOccupiedIndex(item);
            if (index >= 0)
            {
                Remove(index);
            }
        }

        public Span<T> GetValues()
        {
            if (dirty)
            {
                RebuildCache();
                dirty = false;
            }
            return new Span<T>(valueCache, 0, cachedCount);
        }

        public unsafe int FindIndex(Predicate<T> match)
        {
            fixed (ulong* bits = occupied)
            {
                for (int word = 0; word < occupied.Length; word++)
                {
                    ulong bitset = bits[word];
                    while (bitset != 0)
                    {
                        int bit = System.Numerics.BitOperations.TrailingZeroCount(bitset);
                        int index = word * 64 + bit;
                        bitset &= bitset - 1;
                        if (index < items.Length && match(items[index]))
                        {
                            return index;
                        }
                    }
                }
            }

            return -1;
        }

        public T this[int id]
        {
            get { return items[id]; }
            set
            {
                items[id] = value;
                dirty = true;
                if (value == null) Remove(id);
            }
        }

        bool IsOccupied(int index)
        {
            return (occupied[index >> 6] & (1UL << (index & 63))) != 0;
        }

        void SetOccupied(int index, bool value)
        {
            ulong bit = 1UL << (index & 63);
            if (value)
            {
                occupied[index >> 6] |= bit;
            }
            else
            {
                occupied[index >> 6] &= ~bit;
            }
        }

        unsafe int FindOccupiedIndex(T item)
        {
            var comparer = EqualityComparer<T>.Default;

            fixed (ulong* bits = occupied)
            {
                for (int word = 0; word < occupied.Length; word++)
                {
                    ulong bitset = bits[word];
                    while (bitset != 0)
                    {
                        int bit = System.Numerics.BitOperations.TrailingZeroCount(bitset);
                        int index = word * 64 + bit;
                        bitset &= bitset - 1;
                        if (index < items.Length && comparer.Equals(items[index], item))
                        {
                            return index;
                        }
                    }
                }
            }

            return -1;
        }

        unsafe void RebuildCache()
        {
            int writeIndex = 0;

            fixed (ulong* bits = occupied)
            {
                for (int word = 0; word < occupied.Length; word++)
                {
                    ulong bitset = bits[word];
                    while (bitset != 0)
                    {
                        int bit = System.Numerics.BitOperations.TrailingZeroCount(bitset);
                        int index = word * 64 + bit;
                        bitset &= bitset - 1;
                        if (index < items.Length)
                        {
                            valueCache[writeIndex] = items[index];
                            writeIndex++;
                        }
                    }
                }
            }

            cachedCount = writeIndex;
        }
    }
    public static class CMath
    {
        public const float Deg2Rad = MathF.PI / 180f;
        public const float kEpsilon = 0.000001F;

        public static Matrix ObliqueClipProjection(Matrix projection, Matrix view, Plane worldClipPlane)
        {
            Plane clipPlane = Plane.Transform(worldClipPlane, view);

            Vector4 c = new Vector4(clipPlane.Normal, clipPlane.D);
            if (Vector4.Dot(c, new Vector4(0, 0, 1, 1)) < 0)
                c = -c;

            Matrix result = projection;

            Vector4 q;
            q.X = Math.Sign(c.X) / result.M11;
            q.Y = Math.Sign(c.Y) / result.M22;
            q.Z = 1.0f;
            q.W = (1.0f - result.M33) / result.M43;

            Vector4 scaledClip = c * (1.0f / Vector4.Dot(c, q));

            result.M13 = scaledClip.X;
            result.M23 = scaledClip.Y;
            result.M33 = scaledClip.Z;
            result.M43 = scaledClip.W;

            return result;
        }

        public static float Smoothstep(float t)
        {
            return t * t * (3f - 2f * t);
        }

        public static Vector3 CatmullRomCentripetal(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            const float alpha = 0.5f;

            float t0 = 0f;
            float t1 = t0 + MathF.Pow(Vector3.Distance(p0, p1), alpha);
            float t2 = t1 + MathF.Pow(Vector3.Distance(p1, p2), alpha);
            float t3 = t2 + MathF.Pow(Vector3.Distance(p2, p3), alpha);

            float u = t1 + t * (t2 - t1);

            Vector3 a1 = Remap(p0, p1, t0, t1, u);
            Vector3 a2 = Remap(p1, p2, t1, t2, u);
            Vector3 a3 = Remap(p2, p3, t2, t3, u);

            Vector3 b1 = Remap(a1, a2, t0, t2, u);
            Vector3 b2 = Remap(a2, a3, t1, t3, u);

            return Remap(b1, b2, t1, t2, u);
        }

        public static Vector3 Remap(Vector3 from, Vector3 to, float fromT, float toT, float t)
        {
            if (MathF.Abs(toT - fromT) < 0.00001f)
            {
                return from;
            }

            return Vector3.Lerp(from, to, (t - fromT) / (toT - fromT));
        }
        public static Quaternion SquadRotation(Quaternion q0, Quaternion q1, Quaternion q2, Quaternion q3, float t)
        {
            Quaternion s1 = IntermediateControlPoint(q0, q1, q2);
            Quaternion s2 = IntermediateControlPoint(q1, q2, q3);

            Quaternion a = Quaternion.Slerp(q1, q2, t);
            Quaternion b = Quaternion.Slerp(s1, s2, t);

            return Quaternion.Slerp(a, b, 2f * t * (1f - t));
        }

        public static Quaternion IntermediateControlPoint(Quaternion prev, Quaternion cur, Quaternion next)
        {
            Quaternion invCur = Quaternion.Inverse(cur);

            Quaternion a = invCur * prev;
            Quaternion b = invCur * next;

            Vector3 logSum = QuatLog(a) + QuatLog(b);

            return cur * QuatExp(logSum * -0.25f);
        }
        public static Vector3 QuatLog(Quaternion q)
        {
            float angle = MathF.Acos(MathHelper.Clamp(q.W, -1f, 1f));

            if (angle < 0.0001f)
            {
                return Vector3.Zero;
            }

            float sinAngle = MathF.Sin(angle);

            return new Vector3(q.X, q.Y, q.Z) * (angle / sinAngle);
        }
        public static Quaternion QuatExp(Vector3 v)
        {
            float angle = v.Length();

            if (angle < 0.0001f)
            {
                return Quaternion.Identity;
            }

            Vector3 axis = v / angle;
            float sinAngle = MathF.Sin(angle);

            return new Quaternion(axis * sinAngle, MathF.Cos(angle));
        }
        public static Vector3 ReflectPoint(Vector3 point, Plane plane)
        {
            float distance = plane.DotCoordinate(point);
            return point - 2f * distance * plane.Normal;
        }
        public static Vector3 ReflectVector(Vector3 vector, Vector3 normal)
        {
            return Vector3.Reflect(vector,normal);
        }
        private static bool IsEqualUsingDot(float dot)
        {
            return dot > 1.0f - kEpsilon;
        }
        public static float Angle(Quaternion a, Quaternion b)
        {
            float dot = MathF.Min(MathF.Abs(Quaternion.Dot(a, b)), 1.0F);
            return MathHelper.ToDegrees(IsEqualUsingDot(dot) ? 0.0f : MathF.Acos(dot) * 2.0F);
        }
        public static Quaternion RotateTowards(Quaternion from, Quaternion to, float maxDegreesDelta)
        {
            float angle = Angle(from, to);
            if (angle == 0.0f) return to;
            return Quaternion.Lerp(from, to, float.Min(1.0f, maxDegreesDelta / angle));
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
        public static Vector2 MoveTowards(Vector2 current, Vector2 target, float maxDistanceDelta)
        {
            Vector2 a = target - current;
            float magnitude = a.Length();
            if (magnitude <= maxDistanceDelta || magnitude == 0f)
            {
                return target;
            }
            return current + a / magnitude * maxDistanceDelta;
        }
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (float.IsNaN(current)) return !float.IsNaN(target) ? target : 0;
            if (float.IsNaN(target)) return current;
            if (float.IsNaN(maxDelta)) return current;

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
            float delta = Repeat((target - current), 360.0F);
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
                W = (cr * cp * cy + sr * sp * sy),
                X = (sr * cp * cy - cr * sp * sy),
                Y = (cr * sp * cy + sr * cp * sy),
                Z = (cr * cp * sy - sr * sp * cy)
            };
        }

        public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
        {
            return a + (b - a) * t;
        }

        // Source - https://stackoverflow.com/a/67920029
        public static Vector3 Slerp(Vector3 start, Vector3 end, float percent)
        {
            // Dot product - the cosine of the angle between 2 vectors.
            float dot = Vector3.Dot(start, end);

            // Clamp it to be in the range of Acos()
            // This may be unnecessary, but floating point
            // precision can be a fickle mistress.
            //Mathf.Clamp(dot, -1.0f, 1.0f);
            // annotation derHugo: like it stands this is indeed completely unnecessary. 
            // If something it should be
            dot = float.Clamp(dot, -1.0f, 1.0f);

            // Acos(dot) returns the angle between start and end,
            // And multiplying that by percent returns the angle between
            // start and the final result.
            float theta = float.Acos(dot) * percent;
            Vector3 RelativeVec = end - start * dot;
            RelativeVec.Normalize();

            // Orthonormal basis
            // The final result.
            return ((start * float.Cos(theta)) + (RelativeVec * float.Sin(theta)));
        }
        public static Vector3 CatmullRom(
            Vector3 p0,
            Vector3 p1,
            Vector3 p2,
            Vector3 p3,
            float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return 0.5f * (
                2.0f * p1 +
                (-p0 + p2) * t +
                (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2 +
                (-p0 + 3.0f * p1 - 3.0f * p2 + p3) * t3
            );
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
        public static Vector3 ProjectPointOnPlane(Vector3 point, Plane plane)
        {
            float distance = plane.DotCoordinate(point);
            return point - plane.Normal * distance;
        }

        public static float InflatedPlaneDistance(Vector3 center, Matrix orientation, Vector3 halfExtents, Vector3 planePoint, Vector3 planeNormal)
        {
            float dist = Vector3.Dot(center - planePoint, planeNormal);

            Vector3 ax = orientation.Right;
            Vector3 ay = orientation.Up;
            Vector3 az = orientation.Forward;

            float r = halfExtents.X * MathF.Abs(Vector3.Dot(ax, planeNormal))
                    + halfExtents.Y * MathF.Abs(Vector3.Dot(ay, planeNormal))
                    + halfExtents.Z * MathF.Abs(Vector3.Dot(az, planeNormal));

            return dist - r; // <= 0 means touching or overlapping the half-space
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
        /// Checks if a and b are almost equals, taking into account the magnitude of floating point numbers (unlike <see cref="WithinEpsilon"/> method). See Remarks.
        /// See remarks.
        /// </summary>
        /// <param name="a">The left value to compare.</param>
        /// <param name="b">The right value to compare.</param>
        /// <returns><c>true</c> if a almost equal to b, <c>false</c> otherwise</returns>
        public static bool NearEqual(float a, float b)
        {
            if (IsZero(a - b))
                return true;

            return false;
        }

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
            return ((-epsilon <= num) && (num <= epsilon));
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
            return (byte)Lerp((float)from, (float)to, amount);
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
            return (amount <= 0) ? 0
                : (amount >= 1) ? 1
                : amount * amount * (3 - (2 * amount));
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
            return (amount <= 0) ? 0
                : (amount >= 1) ? 1
                : amount * amount * amount * (amount * ((amount * 6) - 15) + 10);
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
        /// Wraps the specified value into a range [min, max[
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="min">The min.</param>
        /// <param name="max">The max.</param>
        /// <returns>Result of the wrapping.</returns>
        /// <exception cref="ArgumentException">Is thrown when <paramref name="min"/> is greater than <paramref name="max"/>.</exception>
        public static float Wrap(float value, float min, float max)
        {
            if (NearEqual(min, max)) return min;

            double mind = min;
            double maxd = max;
            double valued = value;

            if (mind > maxd)
                throw new ArgumentException(string.Format("min {0} should be less than or equal to max {1}", min, max), "min");

            var range_size = maxd - mind;
            return (float)(mind + (valued - mind) - (range_size * Math.Floor((valued - mind) / range_size)));
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

            var componentX = (cx * cx) / (2 * sigmaX * sigmaX);
            var componentY = (cy * cy) / (2 * sigmaY * sigmaY);

            return amplitude * Math.Exp(-(componentX + componentY));
        }

        public static VertexPosition[] GetDebugEdges(BoundingBox box)
        {
            VertexPosition[] corners = new VertexPosition[8]
            {
                new VertexPosition(new Vector3(box.Min.X,box.Min.Y,box.Min.Z)),

                new VertexPosition(new Vector3(box.Max.X,box.Min.Y,box.Min.Z)),

                new VertexPosition(new Vector3(box.Min.X,box.Max.Y,box.Min.Z)),

                new VertexPosition(new Vector3(box.Min.X,box.Min.Y,box.Max.Z)),


                new VertexPosition(new Vector3(box.Max.X,box.Max.Y,box.Min.Z)),

                new VertexPosition(new Vector3(box.Max.X,box.Max.Y,box.Max.Z)),

                new VertexPosition(new Vector3(box.Min.X,box.Max.Y,box.Max.Z)),

                new VertexPosition(new Vector3(box.Max.X,box.Min.Y,box.Max.Z))
            };

            VertexPosition[] edges =
            [
                corners[0],corners[1],
                corners[0],corners[2],
                corners[1],corners[4],
                corners[2],corners[4],

                corners[0],corners[3],
                corners[3],corners[6],
                corners[6],corners[2],

                corners[7],corners[1],
                corners[7],corners[5],
                corners[5],corners[4],

                corners[3],corners[7],
                corners[5],corners[6],
            ];
            return edges;
        }
#if !rockwall
        public static VertexPosition[] GetDebugEdges(OrientedBoundingBox box)
        {
            Vector3[] _corners = box.GetCorners();
            VertexPosition[] corners = new VertexPosition[_corners.Length];

            for (int i = 0; i < _corners.Length; i++)
            {
                corners[i] = new VertexPosition(_corners[i]);
            }

            VertexPosition[] edges = new VertexPosition[]
            {
                corners[0],corners[1],
                corners[1],corners[2],
                corners[2],corners[3],
                corners[3],corners[0],

                corners[4],corners[5],
                corners[5],corners[6],
                corners[6],corners[7],
                corners[7],corners[4],

                corners[0],corners[4],
                corners[1],corners[5],
                corners[2],corners[6],
                corners[3],corners[7],
            };
            return edges;
        }
#endif
    }
}