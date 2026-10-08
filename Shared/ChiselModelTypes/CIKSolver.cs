using Chisel.Models;
using Chisel.Utils.Animation;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Text;
using static Chisel.Models.CModel;

namespace Chisel;

public static class CIKSolver
{
    public static void SolveThreeJoint(
        Matrix[] modelTransforms,
        CIKChain chain,
        Matrix effectorTargetWorld,
        float weight)
    {
        if (weight <= 0f || chain.TopBoneIndex < 0)
        {
            return;
        }

        Matrix targetWorld = GetBoneTargetFromEffectorTarget(chain, effectorTargetWorld);

        int topIndex = chain.TopBoneIndex;
        int midIndex = chain.MidBoneIndex;
        int endIndex = chain.EndBoneIndex;

        Matrix topBefore = modelTransforms[topIndex];
        Matrix midBefore = modelTransforms[midIndex];
        Matrix endBefore = modelTransforms[endIndex];

        if (!IsUsableMatrix(topBefore) || !IsUsableMatrix(midBefore) || !IsUsableMatrix(endBefore) || !IsUsableMatrix(targetWorld))
        {
            return;
        }

        Vector3 topPos = topBefore.Translation;
        Vector3 midPos = midBefore.Translation;
        Vector3 endPos = endBefore.Translation;
        Vector3 targetPos = targetWorld.Translation;

        Vector3 pole = -Vector3.Cross(midPos - topPos, endPos - midPos);
        if (pole.LengthSquared() < 0.0001f)
        {
            pole = Vector3.Cross(midPos - topPos, Vector3.Up);
        }
        if (pole.LengthSquared() < 0.0001f)
        {
            pole = Vector3.Cross(midPos - topPos, Vector3.Right);
        }
        pole = Vector3.Normalize(pole);

        float upperLength = chain.UpperLength;
        float lowerLength = chain.LowerLength;

        Vector3 toTarget = targetPos - topPos;
        float rawDist = toTarget.Length();
        float maxReach = upperLength + lowerLength;
        float minReach = MathF.Abs(upperLength - lowerLength) + 0.0001f;
        float dist = Math.Clamp(rawDist, minReach, maxReach);

        Vector3 targetDir = rawDist > 0.0001f ? toTarget / rawDist : Vector3.Forward;
        targetPos = topPos + targetDir * dist;

        float cosTop = (upperLength * upperLength + dist * dist - lowerLength * lowerLength) / (2f * upperLength * dist);
        float topAngle = MathF.Acos(Math.Clamp(cosTop, -1f, 1f));

        Vector3 bendDir = Vector3.Normalize(Vector3.Cross(pole, targetDir));
        Vector3 solvedMidPos = topPos + targetDir * (upperLength * MathF.Cos(topAngle)) + bendDir * (upperLength * MathF.Sin(topAngle));
        Vector3 solvedEndPos = targetPos;

        Quaternion topDelta = ShortestRotation(midPos - topPos, solvedMidPos - topPos);
        Quaternion midDelta = ShortestRotation(endPos - midPos, solvedEndPos - solvedMidPos);

        Matrix solvedTop = RotateInPlace(topBefore, topDelta);
        solvedTop.Translation = topPos;

        Matrix solvedMid = RotateInPlace(midBefore, midDelta);
        solvedMid.Translation = solvedMidPos;

        targetWorld.Decompose(out Vector3 targetScale, out Quaternion targetRot, out _);
        Matrix solvedEnd = Matrix.CreateScale(targetScale) * Matrix.CreateFromQuaternion(targetRot);
        solvedEnd.Translation = solvedEndPos;

        modelTransforms[topIndex] = BlendMatrix(topBefore, solvedTop, weight);
        modelTransforms[midIndex] = BlendMatrix(midBefore, solvedMid, weight);
        modelTransforms[endIndex] = BlendMatrix(endBefore, solvedEnd, weight);
    }

    private static bool IsUsableMatrix(Matrix m)
    {
        return m.Right.LengthSquared() > 1e-8f
            && m.Up.LengthSquared() > 1e-8f
            && m.Forward.LengthSquared() > 1e-8f;
    }

    public static void ApplyChains(CModel model, CModelAnimator animator, IReadOnlyDictionary<string, (Matrix Target, float Weight)> overrides = null)
    {
        if (animator == null || model.IKChains == null)
        {
            return;
        }

        animator.BeginIKResultsFrame();

        foreach (var chain in model.IKChains)
        {
            if (chain.TopBoneIndex < 0)
            {
                continue;
            }

            Matrix target;
            float weight;

            if (overrides != null && overrides.TryGetValue(chain.ChainName, out var overrideValue))
            {
                target = overrideValue.Target;
                weight = overrideValue.Weight;
            }
            else
            {
                target = animator.ResolveIKTarget(chain.ChainName, out weight);
            }

            if (weight <= 0f)
            {
                continue;
            }

            SolveThreeJoint(model.ModelTransforms, chain, target, weight);

            animator.SyncIKCorrectedBone(chain.TopBoneIndex, model.ModelTransforms[chain.TopBoneIndex]);
            animator.SyncIKCorrectedBone(chain.MidBoneIndex, model.ModelTransforms[chain.MidBoneIndex]);
            animator.SyncIKCorrectedBone(chain.EndBoneIndex, model.ModelTransforms[chain.EndBoneIndex]);
            animator.RepropagateFromChain(chain.TopBoneIndex, chain.MidBoneIndex, chain.EndBoneIndex);

            animator.RecordIKResult(chain.ChainName, target, weight);
        }

        animator.EndIKResolutionFrame();
    }

    public static Matrix GetEffectorTransform(CIKChain chain, Matrix boneModelSpace)
    {
        if (chain.EndEffectorOffset == Vector3.Zero)
        {
            return boneModelSpace;
        }

        Vector3 worldOffset = Vector3.TransformNormal(chain.EndEffectorOffset, boneModelSpace);
        Matrix result = boneModelSpace;
        result.Translation += worldOffset;
        return result;
    }

    private static Matrix GetBoneTargetFromEffectorTarget(CIKChain chain, Matrix effectorTarget)
    {
        if (chain.EndEffectorOffset == Vector3.Zero)
        {
            return effectorTarget;
        }

        Vector3 worldOffset = Vector3.TransformNormal(chain.EndEffectorOffset, effectorTarget);
        Matrix result = effectorTarget;
        result.Translation -= worldOffset;
        return result;
    }

    private static Matrix RotateInPlace(Matrix m, Quaternion delta)
    {
        m.Decompose(out Vector3 scale, out Quaternion rotation, out Vector3 translation);
        Quaternion newRotation = Quaternion.Normalize(delta * rotation);
        Matrix result = Matrix.CreateScale(scale) * Matrix.CreateFromQuaternion(newRotation);
        result.Translation = translation;
        return result;
    }

    internal static Quaternion ShortestRotation(Vector3 from, Vector3 to)
    {
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);

        float dot = Math.Clamp(Vector3.Dot(from, to), -1f, 1f);
        if (dot > 0.9999f)
        {
            return Quaternion.Identity;
        }

        if (dot < -0.9999f)
        {
            Vector3 axis = Vector3.Cross(from, Vector3.Right);
            if (axis.LengthSquared() < 0.0001f)
            {
                axis = Vector3.Cross(from, Vector3.Up);
            }
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }

        Vector3 axisN = Vector3.Normalize(Vector3.Cross(from, to));
        float angle = MathF.Acos(dot);
        return Quaternion.CreateFromAxisAngle(axisN, angle);
    }

    private static Matrix BlendMatrix(Matrix a, Matrix b, float t)
    {
        a.Decompose(out Vector3 sa, out Quaternion ra, out Vector3 pa);
        b.Decompose(out Vector3 sb, out Quaternion rb, out Vector3 pb);

        Vector3 s = Vector3.Lerp(sa, sb, t);
        Quaternion r = Quaternion.Slerp(ra, rb, t);
        Vector3 p = Vector3.Lerp(pa, pb, t);

        return Matrix.CreateScale(s) * Matrix.CreateFromQuaternion(r) * Matrix.CreateTranslation(p);
    }
}