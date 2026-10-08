using Chisel.Models;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Text;
using static Chisel.Models.CModel;

namespace Chisel;
public static class CFootPlacement
{
    public struct FootInput
    {
        public string ChainName;
        public Vector3 AnimatedAnklePos;
        public Quaternion AnimatedAnkleRot;
        public Quaternion LocalOrientationOffset;
        public float WindowWeight;

        public bool HasPredictedTarget;
        public Matrix PredictedTarget;

        public bool LockedGroundHit;
        public Vector3 LockedGroundPoint;
        public Vector3 LockedGroundNormal;
    }

    public struct FootResult
    {
        public string ChainName;
        public Matrix Target;
        public float Weight;
    }

    public static List<FootResult> SolveFeet(IReadOnlyList<FootInput> feet, float maxLegCorrection = 1f)
    {
        var results = new List<FootResult>(feet.Count);

        for (int i = 0; i < feet.Count; i++)
        {
            var foot = feet[i];

            if (!foot.HasPredictedTarget && !foot.LockedGroundHit)
            {
                continue;
            }

            Matrix continuousTarget = foot.HasPredictedTarget
                ? foot.PredictedTarget
                : IdentityAt(foot.AnimatedAnklePos, foot.AnimatedAnkleRot);

            Matrix lockedTarget = foot.LockedGroundHit
                ? BuildFootTarget(foot.AnimatedAnklePos, foot.AnimatedAnkleRot, foot.LocalOrientationOffset, foot.LockedGroundPoint, foot.LockedGroundNormal, maxLegCorrection)
                : continuousTarget;

            continuousTarget.Decompose(out _, out Quaternion contRot, out Vector3 contPos);
            lockedTarget.Decompose(out _, out Quaternion lockRot, out Vector3 lockPos);

            Vector3 finalPos = Vector3.Lerp(contPos, lockPos, foot.WindowWeight);
            Quaternion finalRot = Quaternion.Slerp(contRot, lockRot, foot.WindowWeight);

            Matrix target = Matrix.CreateFromQuaternion(finalRot);
            target.Translation = finalPos;

            results.Add(new FootResult { ChainName = foot.ChainName, Target = target, Weight = 1f });
        }

        return results;
    }

    private static Matrix BuildFootTarget(Vector3 animatedPos, Quaternion animatedRot, Quaternion localOffset, Vector3 groundPoint, Vector3 groundNormal, float maxLegCorrection)
    {
        Vector3 raw = groundPoint - animatedPos;
        float rawLength = raw.Length();
        if (rawLength > maxLegCorrection)
        {
            raw = raw / rawLength * maxLegCorrection;
        }

        Vector3 targetPos = animatedPos + raw;

        Quaternion effectiveRot = Quaternion.Normalize(animatedRot * localOffset);
        Vector3 animatedUp = Vector3.Transform(Vector3.Up, effectiveRot);
        Quaternion tilt = CIKSolver.ShortestRotation(animatedUp, groundNormal);
        Quaternion targetRot = Quaternion.Normalize(tilt * animatedRot);

        Matrix target = Matrix.CreateFromQuaternion(targetRot);
        target.Translation = targetPos;
        return target;
    }

    private static Matrix IdentityAt(Vector3 pos, Quaternion rot)
    {
        Matrix m = Matrix.CreateFromQuaternion(rot);
        m.Translation = pos;
        return m;
    }
}
public static class CPelvisPlacement
{
    public struct Sample
    {
        public bool Hit;
        public Vector3 GroundPoint;
        public Vector3 GroundNormal;
    }

    public static float ComputeShift(Vector3 referencePoint, IReadOnlyList<Sample> samples, float maxShift, float slopeShiftFactor)
    {
        float shift = 0f;
        bool any = false;

        for (int i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            if (!s.Hit) continue;

            any = true;

            float heightDelta = s.GroundPoint.Y - referencePoint.Y;
            float tiltFactor = MathF.Max(0f, 1f - Vector3.Dot(Vector3.Normalize(s.GroundNormal), Vector3.Up));

            float perPoint = Math.Clamp(heightDelta, -maxShift, maxShift) - tiltFactor * slopeShiftFactor;
            shift = Math.Min(shift, perPoint);
        }

        if (!any) return 0f;
        return Math.Clamp(shift, -maxShift, maxShift);
    }
}