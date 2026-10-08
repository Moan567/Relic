using Chisel.Models;
using Microsoft.Xna.Framework;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static Chisel.Models.CModel;
using static Chisel.Utils.Animation.LayerConfig;

namespace Chisel.Utils.Animation
{
    public enum LayerBlendType
    {
        Replace,
        Additive
    }
    public struct BoneSRT
    {
        public Vector3 Scale;
        public Quaternion Rotation;
        public Vector3 Translation;

        public static readonly BoneSRT Identity = new BoneSRT { Scale = Vector3.One, Rotation = Quaternion.Identity, Translation = Vector3.Zero };

        public static BoneSRT FromMatrix(Matrix m)
        {
            m.Decompose(out Vector3 s, out Quaternion r, out Vector3 t);
            return new BoneSRT { Scale = s, Rotation = r, Translation = t };
        }

        public readonly Matrix ToMatrix()
        {
            float x = Rotation.X, y = Rotation.Y, z = Rotation.Z, w = Rotation.W;
            float x2 = x + x, y2 = y + y, z2 = z + z;
            float xx = x * x2, xy = x * y2, xz = x * z2;
            float yy = y * y2, yz = y * z2, zz = z * z2;
            float wx = w * x2, wy = w * y2, wz = w * z2;

            float sx = Scale.X, sy = Scale.Y, sz = Scale.Z;

            return new Matrix(
                sx * (1f - (yy + zz)), sx * (xy + wz), sx * (xz - wy), 0f,
                sy * (xy - wz), sy * (1f - (xx + zz)), sy * (yz + wx), 0f,
                sz * (xz + wy), sz * (yz - wx), sz * (1f - (xx + yy)), 0f,
                Translation.X, Translation.Y, Translation.Z, 1f
            );
        }

        public static BoneSRT LocalFromModelSpace(in BoneSRT child, in BoneSRT parent)
        {
            Quaternion invParentRot = Quaternion.Inverse(parent.Rotation);
            Vector3 invParentScale = new Vector3(
                parent.Scale.X == 0f ? 0f : 1f / parent.Scale.X,
                parent.Scale.Y == 0f ? 0f : 1f / parent.Scale.Y,
                parent.Scale.Z == 0f ? 0f : 1f / parent.Scale.Z);

            return new BoneSRT
            {
                Scale = child.Scale * invParentScale,
                Rotation = Quaternion.Normalize(invParentRot * child.Rotation),
                Translation = Vector3.Transform(child.Translation - parent.Translation, invParentRot) * invParentScale,
            };
        }

        public static BoneSRT ModelFromLocal(in BoneSRT local, in BoneSRT parentModelSpace)
        {
            return new BoneSRT
            {
                Scale = local.Scale * parentModelSpace.Scale,
                Rotation = Quaternion.Normalize(parentModelSpace.Rotation * local.Rotation),
                Translation = Vector3.Transform(local.Translation * parentModelSpace.Scale, parentModelSpace.Rotation)
                              + parentModelSpace.Translation,
            };
        }

        public static BoneSRT Lerp(in BoneSRT a, in BoneSRT b, float t)
        {
            return new BoneSRT
            {
                Scale = Vector3.Lerp(a.Scale, b.Scale, t),
                Rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t),
                Translation = Vector3.Lerp(a.Translation, b.Translation, t)
            };
        }

        public static BoneSRT Blend3(in BoneSRT a, in BoneSRT b, in BoneSRT c, float wa, float wb, float wc)
        {
            float sumAB = wa + wb;
            BoneSRT ab = sumAB > 0f ? Lerp(a, b, wb / sumAB) : a;
            float sumABC = sumAB + wc;
            return sumABC > 0f ? Lerp(ab, c, wc / sumABC) : ab;
        }

        public static BoneSRT BlendExtrapolated(in BoneSRT a, in BoneSRT b, float weightA, float weightB)
        {
            float sum = weightA + weightB;
            float t = sum > 0f ? weightB / sum : 0f;
            return new BoneSRT
            {
                Scale = Vector3.Lerp(a.Scale, b.Scale, t),
                Rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t),
                Translation = a.Translation * weightA + b.Translation * weightB
            };
        }
    }
    public struct ClipWeight
    {
        public ClipRuntime Clip;
        public float Weight;
    }

    public abstract class BlendNodeRuntime
    {
        public BoneSRT[] Result { get; protected set; }
        public Vector3 RootMotionDelta { get; protected set; }
        public abstract bool IsFinished { get; }

        public abstract void Update(float dt);
        public abstract void UpdateRootOnly(float dt);
        public abstract int CountClips();
        public abstract void CollectWeightedClips(ClipWeight[] buffer, ref int count, float parentWeight);
    }
    public class ClipRuntime : BlendNodeRuntime
    {
        public CAnimationPlayer Player { get; }
        public Vector3 CyclePerLoopDisplacement { get; private set; }
        public float NaturalSpeed { get; private set; }
        internal int TickBefore { get; private set; }
        public override bool IsFinished => !Player.IsPlaying;

        public int CurrentWholeTick => Player.CurrentWholeTick;

        private Vector3 lastRawRootTranslation;
        private Vector3 rootTranslationAtClipStart;
        private Vector3 heldTranslation;
        private int lastRootSampleTick;
        private bool hasLastRoot;

        public int[] MaskedBoneIndices { get; }
        private readonly BoneSRT[] bindPoseLocalSRT;

        private readonly Dictionary<string, BoneSRT> capturedIKDeltas = new();

        public ClipRuntime(CModel model, BlendSource source, bool loop = true)
        {
            Player = new CAnimationPlayer(model)
            {
                SRTOnly = true,
                AnimDef = model.FindAnimation(source.AnimationName),
                IsPlaying = true,
                IsLooping = loop
            };

            Result = new BoneSRT[model.Bones.Count];
            ComputeRootMotionStats(model);

            var mask = Player.AnimDef?.BoneMask;
            if (mask != null && mask.Count > 0)
            {
                var indices = new List<int>(mask.Count);
                foreach (var boneName in mask)
                {
                    int idx = model.Bones.FindIndex(b => b.Name == boneName);
                    if (idx >= 0) indices.Add(idx);
                }

                if (indices.Count > 0)
                {
                    MaskedBoneIndices = indices.ToArray();
                    bindPoseLocalSRT = model.GetBindPoseLocalSRTs();
                }
            }
        }

        private void ComputeRootMotionStats(CModel model)
        {
            var config = Player.AnimDef?.RootMotion;
            if (config == null || Player.AnimDef == null)
            {
                CyclePerLoopDisplacement = Vector3.Zero;
                NaturalSpeed = 0f;
                return;
            }

            float durationTicks = Player.AnimDef.Animation.DurationInTicks;
            float duration = Player.AnimDef.Animation.DurationInSeconds;

            bool wasPlaying = Player.IsPlaying;
            float savedTick = Player.CurrentTick;

            Player.CurrentTick = 0f;
            Player.ForceUpdateTransforms();
            Vector3 start = Player.LocalSpaceSRTs[0].Translation;

            Player.CurrentTick = Math.Max(durationTicks - 0.01f, 0f);
            Player.ForceUpdateTransforms();
            Vector3 end = Player.LocalSpaceSRTs[0].Translation;

            Player.CurrentTick = savedTick;
            Player.IsPlaying = wasPlaying;
            Player.ForceUpdateTransforms();

            Vector3 total = end - start;
            CyclePerLoopDisplacement = new Vector3(
                config.ExtractTranslationX ? total.X : 0f,
                config.ExtractTranslationY ? total.Y : 0f,
                config.ExtractTranslationZ ? total.Z : 0f);

            NaturalSpeed = duration > 0f ? CyclePerLoopDisplacement.Length() / duration : 0f;
        }

        public override void Update(float dt)
        {
            if (Player.AnimDef == null)
            {
                if (Result == null) Result = new BoneSRT[Player.LocalSpaceSRTs.Length];
                Array.Fill(Result, BoneSRT.Identity);
                RootMotionDelta = Vector3.Zero;
                return;
            }

            TickBefore = Player.CurrentWholeTick;
            int sampleTick = TickBefore;

            Player.Update(dt);

            Array.Copy(Player.LocalSpaceSRTs, Result, Result.Length);

            if (MaskedBoneIndices != null)
            {
                for (int i = 0; i < MaskedBoneIndices.Length; i++)
                {
                    Result[MaskedBoneIndices[i]] = bindPoseLocalSRT[MaskedBoneIndices[i]];
                }
            }

            RootMotionConfig config = Player.AnimDef?.RootMotion;

            if (config == null)
            {
                RootMotionDelta = Vector3.Zero;
                hasLastRoot = false;
                return;
            }

            Vector3 rawTranslation = Player.LocalSpaceSRTs[0].Translation;

            if (!hasLastRoot)
            {
                // First sample of this clip instance
                rootTranslationAtClipStart = rawTranslation;
                heldTranslation = rawTranslation;
                lastRawRootTranslation = rawTranslation;
                lastRootSampleTick = sampleTick;
                hasLastRoot = true;
                RootMotionDelta = Vector3.Zero;
                return;
            }

            // Compares this frame's sample-tick against last frame's sample-tick
            bool wrapped = sampleTick < lastRootSampleTick;

            Vector3 raw = rawTranslation - lastRawRootTranslation;
            if (wrapped)
            {
                raw += lastRawRootTranslation - rootTranslationAtClipStart;
            }

            Vector3 delta = Vector3.Zero;
            BoneSRT root = Result[0];

            if (config.ExtractTranslationX) { delta.X = raw.X; root.Translation.X = heldTranslation.X; }
            if (config.ExtractTranslationY) { delta.Y = raw.Y; root.Translation.Y = heldTranslation.Y; }
            if (config.ExtractTranslationZ) { delta.Z = raw.Z; root.Translation.Z = heldTranslation.Z; }

            RootMotionDelta = delta;
            Result[0] = root;

            lastRawRootTranslation = rawTranslation;
            lastRootSampleTick = sampleTick;
        }
        public override void UpdateRootOnly(float dt)
        {
            if (Player.AnimDef == null) return;
            if (!(Player.AnimDef.RootMotion?.ExtractTranslationX ?? false) &&
                !(Player.AnimDef.RootMotion?.ExtractTranslationY ?? false) &&
                !(Player.AnimDef.RootMotion?.ExtractTranslationZ ?? false)) return;

            int sampleTick = Player.CurrentWholeTick;
            Player.UpdateRootBoneOnly(dt);

            RootMotionConfig config = Player.AnimDef.RootMotion;
            if (config == null)
            {
                hasLastRoot = false;
                return;
            }

            Vector3 rawTranslation = Player.LocalSpaceSRTs[0].Translation;

            if (!hasLastRoot)
            {
                rootTranslationAtClipStart = rawTranslation;
                heldTranslation = rawTranslation;
                lastRawRootTranslation = rawTranslation;
                lastRootSampleTick = sampleTick;
                hasLastRoot = true;
                return;
            }

            lastRawRootTranslation = rawTranslation;
            lastRootSampleTick = sampleTick;
        }

        public override int CountClips() => 1;

        public override void CollectWeightedClips(ClipWeight[] buffer, ref int count, float parentWeight)
        {
            buffer[count] = new ClipWeight { Clip = this, Weight = parentWeight };
            count++;
        }

        public bool TryGetActiveIKEvent(string chainName, out CIKAnimEvent activeEvent, out float weight)
        {
            activeEvent = null;
            weight = 0f;

            var events = Player.AnimDef?.IKEvents;
            if (events == null)
            {
                return false;
            }

            int tick = Player.CurrentWholeTick;

            foreach (var ev in events)
            {
                if (ev.ChainName != chainName)
                {
                    continue;
                }

                int freeTick = ev.FreeFrame < 0 ? int.MaxValue : ev.FreeFrame;
                if (tick < ev.LockFrame || tick > freeTick)
                {
                    continue;
                }

                float w = 1f;
                if (ev.FadeInFrames > 0 && tick < ev.LockFrame + ev.FadeInFrames)
                {
                    w = (tick - ev.LockFrame) / (float)ev.FadeInFrames;
                }
                if (ev.FreeFrame >= 0 && ev.FadeOutFrames > 0 && tick > ev.FreeFrame - ev.FadeOutFrames)
                {
                    w = Math.Min(w, (ev.FreeFrame - tick) / (float)ev.FadeOutFrames);
                }

                activeEvent = ev;
                weight = Math.Clamp(w, 0f, 1f);
                return true;
            }

            capturedIKDeltas.Remove(chainName);
            return false;
        }

        public BoneSRT GetOrCaptureIKDelta(CModel model, CIKChain chain, CIKAnimEvent ikEvent, Func<int, Matrix> finalModelSpace)
        {
            if (capturedIKDeltas.TryGetValue(chain.ChainName, out var cached))
            {
                return cached;
            }

            Matrix endWorld;
            Matrix refWorld;

            if (ikEvent.UseSource)
            {
                endWorld = CIKSolver.GetEffectorTransform(chain, SourceModelSpace(model, chain.EndBoneIndex));
                refWorld = ResolveReference(model, ikEvent.TargetReference, (i)=>SourceModelSpace(model,i));
            }
            else
            {
                endWorld = CIKSolver.GetEffectorTransform(chain, finalModelSpace(chain.EndBoneIndex));
                refWorld = ResolveReference(model, ikEvent.TargetReference, finalModelSpace);
            }

            BoneSRT delta = BoneSRT.LocalFromModelSpace(BoneSRT.FromMatrix(endWorld), BoneSRT.FromMatrix(refWorld));
            capturedIKDeltas[chain.ChainName] = delta;
            return delta;
        }

        private Matrix SourceModelSpace(CModel model, int boneIndex)
        {
            Matrix result = Matrix.Identity;
            int current = boneIndex;
            var stack = new List<int>();

            while (current >= 0)
            {
                stack.Add(current);
                current = model.Bones[current].HasParent ? model.Bones[current].Parent.Index : -1;
            }

            for (int i = stack.Count - 1; i >= 0; i--)
            {
                result = Player.LocalSpaceSRTs[stack[i]].ToMatrix() * result;
            }

            return result;
        }

        private static Matrix ResolveReference(CModel model, string reference, Func<int, Matrix> lookup)
        {
            var attachment = model.FindAttachment(reference);
            if (attachment != null)
            {
                return attachment.Offset * lookup(attachment.BoneID);
            }

            int boneIndex = model.Bones.FindIndex(b => b.Name == reference);
            return boneIndex >= 0 ? lookup(boneIndex) : Matrix.Identity;
        }
    }
    public class Blend1DRuntime : BlendNodeRuntime
    {
        private readonly string paramName;
        private readonly float[] values;
        private readonly BlendNodeRuntime[] nodes;
        private readonly SequenceRuntime owner;
        private BoneSRT[] workbuf;

        private int lastLow, lastHigh;
        private float lastT;

        public Blend1DRuntime(CModel model, BlendSource source, SequenceRuntime owner, bool loop = true)
        {
            this.owner = owner;
            paramName = source.ParamName;

            var sorted = source.Entries1D.OrderBy(e => e.Value).ToArray();
            values = new float[sorted.Length];
            nodes = new BlendNodeRuntime[sorted.Length];

            for (int i = 0; i < sorted.Length; i++)
            {
                values[i] = sorted[i].Value;
                nodes[i] = SequenceRuntime.BuildRuntime(model, sorted[i].Source, owner, loop);
            }

            workbuf = new BoneSRT[model.Bones.Count];
        }

        public override bool IsFinished => nodes.All(n => n.IsFinished);

        public override unsafe void Update(float dt)
        {
            if (nodes.Length == 0)
            {
                if (Result == null) Result = new BoneSRT[workbuf.Length];
                Array.Fill(Result, BoneSRT.Identity);
                RootMotionDelta = Vector3.Zero;
                return;
            }

            SolveBracket(owner.GetParam(paramName), out int low, out int high, out float t);
            lastLow = low; lastHigh = high; lastT = t;

            for (int i = 0; i < nodes.Length; i++)
            {
                if (i == low || i == high) nodes[i].Update(dt);
                else nodes[i].UpdateRootOnly(dt);
            }

            BoneSRT[] a = nodes[low].Result;
            BoneSRT[] b = nodes[high].Result;
            int boneCount = workbuf.Length;

            fixed (BoneSRT* pA = a)
            fixed (BoneSRT* pB = b)
            fixed (BoneSRT* pOut = workbuf)
            {
                for (int i = 0; i < boneCount; i++)
                {
                    pOut[i].Scale = Vector3.Lerp(pA[i].Scale, pB[i].Scale, t);
                    pOut[i].Rotation = Quaternion.Slerp(pA[i].Rotation, pB[i].Rotation, t);
                    pOut[i].Translation = Vector3.Lerp(pA[i].Translation, pB[i].Translation, t);
                }
            }

            Result = workbuf;
            RootMotionDelta = Vector3.Lerp(nodes[low].RootMotionDelta, nodes[high].RootMotionDelta, t);
        }
        public override void UpdateRootOnly(float dt)
        {
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i].UpdateRootOnly(dt);
            }
        }
        private void SolveBracket(float param, out int low, out int high, out float t)
        {
            if (values.Length == 1 || param <= values[0])
            {
                low = high = 0;
                t = 0f;
                return;
            }

            if (param >= values[values.Length - 1])
            {
                low = high = values.Length - 1;
                t = 0f;
                return;
            }

            for (int i = 0; i < values.Length - 1; i++)
            {
                if (param >= values[i] && param <= values[i + 1])
                {
                    low = i;
                    high = i + 1;
                    float span = values[high] - values[low];
                    t = span <= 0f ? 0f : (param - values[low]) / span;
                    return;
                }
            }

            low = high = 0;
            t = 0f;
        }
        public override int CountClips()
        {
            int total = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                total += nodes[i].CountClips();
            }
            return total;
        }

        public override void CollectWeightedClips(ClipWeight[] buffer, ref int count, float parentWeight)
        {
            if (nodes.Length == 0) return;

            nodes[lastLow].CollectWeightedClips(buffer, ref count, parentWeight * (1f - lastT));
            if (lastHigh != lastLow)
            {
                nodes[lastHigh].CollectWeightedClips(buffer, ref count, parentWeight * lastT);
            }
        }
    }
    public class Blend2DRuntime : BlendNodeRuntime
    {
        private readonly string paramNameX, paramNameY;
        private readonly Vector2[] points;
        private readonly BlendNodeRuntime[] nodes;
        private readonly int[] triangleIndices;
        private BoneSRT[] workbuf;

        private int lastA, lastB, lastC;
        private float lastWeightA, lastWeightB, lastWeightC;

        public Blend2DRuntime(CModel model, BlendSource source, SequenceRuntime owner, bool loop = true)
        {
            paramNameX = source.ParamNameX;
            paramNameY = source.ParamNameY;
            this.owner = owner;

            points = new Vector2[source.Entries2D.Count];
            nodes = new BlendNodeRuntime[source.Entries2D.Count];

            for (int i = 0; i < source.Entries2D.Count; i++)
            {
                var entry = source.Entries2D[i];
                points[i] = new Vector2(entry.X, entry.Y);
                nodes[i] = SequenceRuntime.BuildRuntime(model, entry.Source, owner, loop);
            }

            if (points.Length >= 3)
            {
                var tris = DelaunayTriangulator.Triangulate(points);

                triangleIndices = new int[tris.Count * 3];
                for (int i = 0; i < tris.Count; i++)
                {
                    triangleIndices[i * 3] = tris[i].a;
                    triangleIndices[i * 3 + 1] = tris[i].b;
                    triangleIndices[i * 3 + 2] = tris[i].c;
                }
            }
            else
            {
                triangleIndices = Array.Empty<int>();
            }

            workbuf = new BoneSRT[model.Bones.Count];
        }

        public override bool IsFinished => nodes.All(n => n.IsFinished);

        private readonly SequenceRuntime owner;

        public override unsafe void Update(float dt)
        {
            if (nodes.Length == 0)
            {
                if (Result == null) Result = new BoneSRT[workbuf.Length];
                Array.Fill(Result, BoneSRT.Identity);
                RootMotionDelta = Vector3.Zero;
                return;
            }

            Vector2 param = new Vector2(owner.GetParam(paramNameX), owner.GetParam(paramNameY));
            SolveTriangle(param, out int a, out int b, out int c, out float wa, out float wb, out float wc);
            lastA = a; lastB = b; lastC = c;
            lastWeightA = wa; lastWeightB = wb; lastWeightC = wc;

            for (int i = 0; i < nodes.Length; i++)
            {
                if (i == a || i == b || i == c) nodes[i].Update(dt);
                else nodes[i].UpdateRootOnly(dt);
            }

            BoneSRT[] ra = nodes[a].Result;
            BoneSRT[] rb = nodes[b].Result;
            BoneSRT[] rc = nodes[c].Result;
            int boneCount = workbuf.Length;

            fixed (BoneSRT* pA = ra)
            fixed (BoneSRT* pB = rb)
            fixed (BoneSRT* pC = rc)
            fixed (BoneSRT* pOut = workbuf)
            {
                for (int i = 0; i < boneCount; i++)
                {
                    pOut[i] = BoneSRT.Blend3(pA[i], pB[i], pC[i], wa, wb, wc);
                }
            }

            Result = workbuf;
            RootMotionDelta = nodes[a].RootMotionDelta * wa + nodes[b].RootMotionDelta * wb + nodes[c].RootMotionDelta * wc;
        }
        public override void UpdateRootOnly(float dt)
        {
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i].UpdateRootOnly(dt);
            }
        }
        private void SolveTriangle(Vector2 p, out int a, out int b, out int c, out float wa, out float wb, out float wc)
        {
            if (points.Length == 1)
            {
                a = b = c = 0;
                wa = 1f; wb = 0f; wc = 0f;
                return;
            }

            if (points.Length == 2)
            {
                a = 0; b = 1; c = 1;
                Vector2 dir = points[1] - points[0];
                float len2 = dir.LengthSquared();
                float t = len2 <= 0f ? 0f : MathHelper.Clamp(Vector2.Dot(p - points[0], dir) / len2, 0f, 1f);
                wa = 1f - t; wb = t; wc = 0f;
                return;
            }

            for (int i = 0; i < triangleIndices.Length; i += 3)
            {
                int ia = triangleIndices[i], ib = triangleIndices[i + 1], ic = triangleIndices[i + 2];

                if (TryBarycentric(p, points[ia], points[ib], points[ic], out float u, out float v, out float w))
                {
                    a = ia; b = ib; c = ic;
                    wa = u; wb = v; wc = w;
                    return;
                }
            }

            int nearest = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < points.Length; i++)
            {
                float dist = Vector2.DistanceSquared(p, points[i]);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    nearest = i;
                }
            }

            a = b = c = nearest;
            wa = 1f; wb = 0f; wc = 0f;
        }

        private static bool TryBarycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float u, out float v, out float w)
        {
            Vector2 v0 = b - a, v1 = c - a, v2 = p - a;
            float den = v0.X * v1.Y - v1.X * v0.Y;

            if (Math.Abs(den) < 1e-6f)
            {
                u = v = w = 0f;
                return false;
            }

            v = (v2.X * v1.Y - v1.X * v2.Y) / den;
            w = (v0.X * v2.Y - v2.X * v0.Y) / den;
            u = 1f - v - w;

            return u >= -1e-4f && v >= -1e-4f && w >= -1e-4f;
        }
        public override int CountClips()
        {
            int total = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                total += nodes[i].CountClips();
            }
            return total;
        }
        public override void CollectWeightedClips(ClipWeight[] buffer, ref int count, float parentWeight)
        {
            if (nodes.Length == 0) return;

            nodes[lastA].CollectWeightedClips(buffer, ref count, parentWeight * lastWeightA);
            if (lastB != lastA) nodes[lastB].CollectWeightedClips(buffer, ref count, parentWeight * lastWeightB);
            if (lastC != lastA && lastC != lastB) nodes[lastC].CollectWeightedClips(buffer, ref count, parentWeight * lastWeightC);
        }
    }

    public class SequenceRuntime
    {
        public CSequence Sequence { get; }
        internal readonly BlendNodeRuntime root;
        private readonly Dictionary<string, float> parameters = [];
        private readonly ClipWeight[] clipWeightBuffer;

        public float PlaybackSpeedMultiplier { get; set; } = 1f;

        public bool IsFinished => root.IsFinished;

        public SequenceRuntime(CModel model, CSequence sequence, bool loop = true)
        {
            Sequence = sequence;
            root = BuildRuntime(model, sequence.Root, this, loop);
            clipWeightBuffer = new ClipWeight[root.CountClips()];
        }

        public void SetParam(string name, float value) => parameters[name] = value;
        public float GetParam(string name) => parameters.TryGetValue(name, out var val) ? val : 0f;

        public void Update(float dt) => root.Update(dt * PlaybackSpeedMultiplier);

        public BoneSRT[] GetBlendedLocalSRT() => root.Result;
        public Vector3 RootMotionDelta => root.RootMotionDelta;

        public void CollectMaskedBones(bool[] maskBuffer)
        {
            int count = 0;
            root.CollectWeightedClips(clipWeightBuffer, ref count, 1f);

            for (int i = 0; i < count; i++)
            {
                if (clipWeightBuffer[i].Weight <= 0f) continue;

                var indices = clipWeightBuffer[i].Clip.MaskedBoneIndices;
                if (indices == null) continue;

                for (int j = 0; j < indices.Length; j++)
                {
                    maskBuffer[indices[j]] = true;
                }
            }
        }

        public ClipRuntime GetDominantClip()
        {
            int count = 0;
            root.CollectWeightedClips(clipWeightBuffer, ref count, 1f);

            ClipRuntime best = null;
            float bestWeight = -1f;

            for (int i = 0; i < count; i++)
            {
                if (clipWeightBuffer[i].Weight > bestWeight)
                {
                    bestWeight = clipWeightBuffer[i].Weight;
                    best = clipWeightBuffer[i].Clip;
                }
            }

            return best;
        }

        internal static BlendNodeRuntime BuildRuntime(CModel model, BlendSource source, SequenceRuntime owner, bool loop = true)
        {
            switch (source.Type)
            {
                case BlendSourceType.Blend1D: return new Blend1DRuntime(model, source, owner, loop);
                case BlendSourceType.Blend2D: return new Blend2DRuntime(model, source, owner, loop);
                default: return new ClipRuntime(model, source, loop);
            }
        }

        public float GetAggregateNaturalSpeed()
        {
            int count = 0;
            root.CollectWeightedClips(clipWeightBuffer, ref count, 1f);

            float total = 0f;
            for (int i = 0; i < count; i++)
            {
                total += clipWeightBuffer[i].Clip.NaturalSpeed * clipWeightBuffer[i].Weight;
            }
            return total;
        }
    }
    public struct IKResult
    {
        public Matrix Target;
        public float Weight;
    }
    public class LayerConfig
    {
        public class IKContribution
        {
            public ClipRuntime Clip;
            public CIKAnimEvent Event;
            public float Weight;
        }

        private readonly CModel model;
        private readonly bool[] ignoredBoneFlags;
        private readonly BoneSRT[] fadebuf;

        private readonly Dictionary<CSequence, SequenceRuntime> runtimeCache = new();
        private readonly Queue<CSequence> cacheEvictionOrder = new();
        private const int MaxCachedRuntimes = 4;

        private readonly Dictionary<string, List<IKContribution>> ikContributions = new();

        public IReadOnlyDictionary<string, List<IKContribution>> IKContributions => ikContributions;

        private ClipWeight[] ikClipWeightBuffer;

        public float Influence { get; set; } = 1f;
        public float PlaybackSpeedMultiplier
        {
            get => current?.PlaybackSpeedMultiplier ?? 1f;
            set { if (current != null) current.PlaybackSpeedMultiplier = value; }
        }
        public LayerBlendType BlendType { get; set; }
        public bool IsFinished => current?.IsFinished ?? true;

        private SequenceRuntime current, previous;
        private float fadeDuration, fadeElapsed;

        internal BoneSRT[] Result { get; private set; }
        private readonly bool[] maskedBoneFlags;

        public int Order { get; set; }
        internal int CreationSequence { get; set; }

        public LayerConfig(CModel model, LayerBlendType blendType)
        {
            this.model = model;
            this.BlendType = blendType;
            ignoredBoneFlags = new bool[model.Bones.Count];
            maskedBoneFlags = new bool[model.Bones.Count];
            fadebuf = new BoneSRT[model.Bones.Count];
            ikClipWeightBuffer = new ClipWeight[64];
        }

        public void IgnoreBones(params string[] boneNames)
        {
            foreach(var name in boneNames)
            {
                int id = model.Bones.FindIndex(b => b.Name == name);

                if(id >= 0)
                {
                    ignoredBoneFlags[id] = true;
                }
            }
        }

        private SequenceRuntime AcquireRuntime(CSequence sequence, bool loop)
        {
            if (runtimeCache.Remove(sequence, out var cached))
            {
                return cached;
            }
            return new SequenceRuntime(model, sequence, loop);
        }

        private void ReleaseToCache(SequenceRuntime runtime)
        {
            if (runtime == null) return;

            runtimeCache[runtime.Sequence] = runtime;
            cacheEvictionOrder.Enqueue(runtime.Sequence);

            while (cacheEvictionOrder.Count > MaxCachedRuntimes)
            {
                var oldestKey = cacheEvictionOrder.Dequeue();
                if (!cacheEvictionOrder.Contains(oldestKey))
                {
                    runtimeCache.Remove(oldestKey);
                }
            }
        }

        public void Play(CSequence sequence, float fadeTime = 0f, bool loop = true)
        {
            if (current != null && current.Sequence == sequence) return;

            if (fadeTime > 0f && current != null)
            {
                if (previous != null) ReleaseToCache(previous);
                previous = current;
            }
            else
            {
                if (current != null) ReleaseToCache(current);
                previous = null;
            }

            fadeDuration = fadeTime;
            fadeElapsed = 0f;
            current = AcquireRuntime(sequence, loop);
        }

        public void ForceReplay(CSequence sequence, bool loop = true)
        {
            if (previous != null) ReleaseToCache(previous);
            if (current != null) ReleaseToCache(current);
            previous = null;

            current = new SequenceRuntime(model, sequence, loop);
        }

        public void SetParam(string name, float value) => current?.SetParam(name, value);
        public Vector3 RootMotionDelta => current?.RootMotionDelta ?? Vector3.Zero;

        internal void Update(float dt)
        {
            current?.Update(dt);
            previous?.Update(dt);

            Array.Clear(maskedBoneFlags, 0, maskedBoneFlags.Length);
            current?.CollectMaskedBones(maskedBoneFlags);
            previous?.CollectMaskedBones(maskedBoneFlags);

            foreach (var list in ikContributions.Values)
            {
                list.Clear();
            }

            CollectIK(current);
            CollectIK(previous);

            void CollectIK(SequenceRuntime seq)
            {
                if (seq == null)
                {
                    return;
                }

                int count = 0;
                seq.root.CollectWeightedClips(ikClipWeightBuffer, ref count, 1f);

                for (int i = 0; i < count; i++)
                {
                    var clip = ikClipWeightBuffer[i].Clip;
                    float clipWeight = ikClipWeightBuffer[i].Weight;

                    foreach (var chain in model.IKChains)
                    {
                        if (!clip.TryGetActiveIKEvent(chain.ChainName, out var ev, out float localWeight))
                        {
                            continue;
                        }

                        if (!ikContributions.TryGetValue(chain.ChainName, out var list))
                        {
                            ikContributions[chain.ChainName] = list = new List<IKContribution>();
                        }

                        list.Add(new IKContribution { Clip = clip, Event = ev, Weight = clipWeight * localWeight });
                    }
                }
            }

            if (current == null)
            {
                Result = null;
                return;
            }

            if (previous == null)
            {
                Result = current.GetBlendedLocalSRT();
                return;
            }

            fadeElapsed += dt;
            float t = fadeDuration <= 0f ? 1f : MathHelper.Clamp(fadeElapsed / fadeDuration, 0f, 1f);

            BoneSRT[] from = previous.GetBlendedLocalSRT();
            BoneSRT[] to = current.GetBlendedLocalSRT();

            for (int b = 0; b < fadebuf.Length; b++)
            {
                fadebuf[b] = BoneSRT.Lerp(from[b], to[b], t);
            }

            Result = fadebuf;

            if (t >= 1f)
            {
                previous = null;
            }
        }

        // We probably dont HAVE to use unsafe for this one specifically, but I want
        // the animations to contribute next to nothing to the GC.
        internal unsafe void Blend(BoneSRT[] finalSRT, BoneSRT[] basePoseLocalSRT)
        {
            if (Result == null) return;

            int boneCount = finalSRT.Length;
            float influence = Influence;

            fixed (BoneSRT* pFinal = finalSRT)
            fixed (BoneSRT* pResult = Result)
            fixed (BoneSRT* pBase = basePoseLocalSRT)
            fixed (bool* pIgnored = ignoredBoneFlags)
            fixed (bool* pMasked = maskedBoneFlags)
            {
                if (BlendType == LayerBlendType.Replace)
                {
                    if (influence >= 1f)
                    {
                        for (int b = 0; b < boneCount; b++)
                        {
                            if (pIgnored[b] || pMasked[b]) continue;
                            pFinal[b] = pResult[b];
                        }
                    }
                    else
                    {
                        for (int b = 0; b < boneCount; b++)
                        {
                            if (pIgnored[b] || pMasked[b]) continue;

                            ref BoneSRT dst = ref pFinal[b];
                            ref BoneSRT src = ref pResult[b];

                            dst.Scale = Vector3.Lerp(dst.Scale, src.Scale, influence);
                            dst.Rotation = Quaternion.Slerp(dst.Rotation, src.Rotation, influence);
                            dst.Translation = Vector3.Lerp(dst.Translation, src.Translation, influence);
                        }
                    }
                }
                else
                {
                    for (int b = 0; b < boneCount; b++)
                    {
                        if (pIgnored[b]) continue;

                        ref BoneSRT baseSRT = ref pBase[b];
                        ref BoneSRT layerLocal = ref pResult[b];
                        ref BoneSRT cur = ref pFinal[b];

                        Vector3 deltaScale = layerLocal.Scale - baseSRT.Scale;
                        Quaternion deltaRotation = Quaternion.Normalize(layerLocal.Rotation * Quaternion.Inverse(baseSRT.Rotation));
                        Vector3 deltaTranslation = layerLocal.Translation - baseSRT.Translation;

                        cur.Scale = cur.Scale * (Vector3.One + deltaScale * influence);
                        cur.Rotation = Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, deltaRotation, influence) * cur.Rotation);
                        cur.Translation = cur.Translation + deltaTranslation * influence;
                    }
                }
            }
        }

        internal void FireEvents(Action<CAnimEvent> callback)
        {
            if (current == null) return;

            ClipRuntime dominant = current.GetDominantClip();
            if (dominant?.Player.AnimDef?.Events == null) return;

            int before = dominant.TickBefore;
            int after = dominant.CurrentWholeTick;

            foreach (var ev in dominant.Player.AnimDef.Events)
            {
                bool crossed = after >= before
                    ? ev.Frame > before && ev.Frame <= after
                    : ev.Frame > before || ev.Frame <= after;

                if (crossed)
                {
                    callback(ev);
                }
            }
        }
        public float GetAggregateNaturalSpeed() => current?.GetAggregateNaturalSpeed() ?? 0f;
        public bool AffectsBone(int boneIndex) => !ignoredBoneFlags[boneIndex] && !maskedBoneFlags[boneIndex];
    }

    public class CModelAnimator
    {
        private readonly CModel model;
        private readonly Dictionary<string, LayerConfig> layers = new();

        private readonly BoneSRT[] finalSRT;
        private readonly BoneSRT[] basePoseLocalSRT;
        private readonly Matrix[] basePose;
        private readonly BoneSRT[] modelSpaceSRT;
        private readonly Matrix[] modelSpace;
        private readonly Matrix[] finalTransformsOutput;
        private readonly Matrix[] preIKModelSpace;

        private readonly bool[] repropagateFlags;

        private readonly Dictionary<string, IKResult> lastIKResults = new();
        public IReadOnlyDictionary<string, IKResult> LastIKResults => lastIKResults;

        private readonly Dictionary<string, (Matrix Target, float Weight)> proceduralIKTargets = new();
        private readonly HashSet<string> proceduralIKTargetsSetThisTick = new();

        private readonly int[] parentIndex;

        public Action<CAnimEvent> OnAnimEvent;
        public bool Paused { get; set; }
        public IReadOnlyDictionary<string, LayerConfig> Layers => layers;

        private LayerConfig[] orderedLayersCache;
        private bool orderDirty = true;
        private int nextLayerOrder = 0;
        private int nextCreationSequence = 0;

        private float pendingPelvisShiftY;

        public CModelAnimator(CModel model)
        {
            this.model = model;

            int boneCount = model.Bones.Count;
            finalSRT = new BoneSRT[boneCount];
            modelSpaceSRT = new BoneSRT[boneCount];
            modelSpace = new Matrix[boneCount];
            finalTransformsOutput = new Matrix[boneCount];
            model.ModelTransforms = new Matrix[boneCount];
            preIKModelSpace = new Matrix[boneCount];

            repropagateFlags = new bool[boneCount];

            basePoseLocalSRT = model.GetBindPoseLocalSRTs();

            parentIndex = new int[boneCount];
            for (int b = 0; b < boneCount; b++)
            {
                parentIndex[b] = model.Bones[b].HasParent ? model.Bones[b].Parent.Index : -1;
            }
        }
        public LayerConfig this[string layerName] => GetOrAddLayer(layerName);

        public LayerConfig GetOrAddLayer(string name, LayerBlendType blendType = LayerBlendType.Replace, int? order = null)
        {
            if (!layers.TryGetValue(name, out var layer))
            {
                layer = new LayerConfig(model, blendType)
                {
                    Order = order ?? nextLayerOrder,
                    CreationSequence = nextCreationSequence++
                };
                nextLayerOrder = Math.Max(nextLayerOrder, layer.Order) + 1;
                layers[name] = layer;
                orderDirty = true;
            }
            else if (order.HasValue && layer.Order != order.Value)
            {
                layer.Order = order.Value;
                orderDirty = true;
            }
            return layer;
        }
        private LayerConfig[] GetOrderedLayers()
        {
            if (orderDirty || orderedLayersCache == null)
            {
                orderedLayersCache = layers.Values
                    .OrderBy(l => l.Order)
                    .ThenBy(l => l.CreationSequence)
                    .ToArray();
                orderDirty = false;
            }
            return orderedLayersCache;
        }

        public unsafe void Update(float dt)
        {
            if (dt == 0f || Paused) return;

            Array.Copy(basePoseLocalSRT, finalSRT, finalSRT.Length);

            var ordered = GetOrderedLayers();

            foreach (var layer in ordered)
            {
                layer.Update(dt);
                if (layer.Influence > 0f)
                {
                    layer.Blend(finalSRT, basePoseLocalSRT);
                }
            }

            int boneCount = finalSRT.Length;

            fixed (BoneSRT* pFinal = finalSRT)
            fixed (BoneSRT* pModelSRT = modelSpaceSRT)
            fixed (Matrix* pModel = modelSpace)
            fixed (int* pParent = parentIndex)
            {
                for (int b = 0; b < boneCount; b++)
                {
                    int parent = pParent[b];

                    pModelSRT[b] = parent >= 0
                        ? BoneSRT.ModelFromLocal(pFinal[b], pModelSRT[parent])
                        : pFinal[b];

                    pModel[b] = pModelSRT[b].ToMatrix();
                }
            }

            if (pendingPelvisShiftY != 0f)
            {
                for (int b = 0; b < modelSpace.Length; b++)
                {
                    var m = modelSpace[b];
                    m.Translation += new Vector3(0f, pendingPelvisShiftY, 0f);
                    modelSpace[b] = m;
                }
            }

            model.ModelTransforms = modelSpace;

            Array.Copy(modelSpace, preIKModelSpace, modelSpace.Length);

            if (OnAnimEvent != null)
            {
                foreach (var layer in ordered)
                {
                    layer.FireEvents(OnAnimEvent);
                }
            }
        }
        public Matrix GetPreIKBoneTransform(int boneIndex) => preIKModelSpace[boneIndex];

        public void SetPelvisShift(float shiftY)
        {
            pendingPelvisShiftY = shiftY;
        }
        internal void SyncIKCorrectedBone(int boneIndex, Matrix correctedModelSpace)
        {
            modelSpaceSRT[boneIndex] = BoneSRT.FromMatrix(correctedModelSpace);
            modelSpace[boneIndex] = correctedModelSpace;
        }

        internal void RepropagateFromChain(int topIndex, int midIndex, int endIndex)
        {
            Array.Clear(repropagateFlags, 0, repropagateFlags.Length);
            repropagateFlags[topIndex] = true;
            repropagateFlags[midIndex] = true;
            repropagateFlags[endIndex] = true;

            for (int b = topIndex + 1; b < parentIndex.Length; b++)
            {
                if (b == midIndex || b == endIndex) continue;

                int parent = parentIndex[b];
                if (parent < 0 || !repropagateFlags[parent]) continue;

                modelSpaceSRT[b] = BoneSRT.ModelFromLocal(finalSRT[b], modelSpaceSRT[parent]);
                modelSpace[b] = modelSpaceSRT[b].ToMatrix();
                repropagateFlags[b] = true;
            }
        }
        public void SetProceduralIKTarget(string chainName, Matrix effectorTargetModelSpace, float weight = 1f)
        {
            proceduralIKTargets[chainName] = (effectorTargetModelSpace, weight);
            proceduralIKTargetsSetThisTick.Add(chainName);
        }

        private (List<IKContribution> Group, float Weight) ResolveWinningIKGroup(string chainName)
        {
            var chain = model.FindIKChain(chainName);
            var ordered = GetOrderedLayers();
            float remainingVisibility = 1f;
            var effectiveWeight = new float[ordered.Length];

            for (int i = ordered.Length - 1; i >= 0; i--)
            {
                var layer = ordered[i];
                bool reducesThisChain = layer.BlendType == LayerBlendType.Replace
                    && chain != null && chain.EndBoneIndex >= 0
                    && layer.AffectsBone(chain.EndBoneIndex);

                if (reducesThisChain)
                {
                    effectiveWeight[i] = layer.Influence * remainingVisibility;
                    remainingVisibility *= (1f - layer.Influence);
                }
                else
                {
                    effectiveWeight[i] = layer.Influence * remainingVisibility;
                }
            }

            List<IKContribution> winningGroup = null;
            float bestWeight = 0f;

            for (int i = 0; i < ordered.Length; i++)
            {
                var layer = ordered[i];
                if (effectiveWeight[i] <= 0f) continue;
                if (!layer.IKContributions.TryGetValue(chainName, out var contributions) || contributions.Count == 0)
                {
                    continue;
                }

                var groups = new Dictionary<CIKAnimEvent, List<IKContribution>>();
                foreach (var c in contributions)
                {
                    if (!groups.TryGetValue(c.Event, out var list))
                    {
                        groups[c.Event] = list = new List<IKContribution>();
                    }
                    list.Add(c);
                }

                foreach (var group in groups.Values)
                {
                    float total = 0f;
                    foreach (var c in group)
                    {
                        total += c.Weight;
                    }

                    total *= effectiveWeight[i];

                    if (total > bestWeight)
                    {
                        bestWeight = total;
                        winningGroup = group;
                    }
                }
            }

            return (winningGroup, Math.Clamp(bestWeight, 0f, 1f));
        }

        public float GetIKWindowWeight(string chainName)
        {
            var (group, weight) = ResolveWinningIKGroup(chainName);
            return group == null ? 0f : weight;
        }

        public Matrix ResolveIKTarget(string chainName, out float resolvedWeight)
        {
            if (proceduralIKTargets.TryGetValue(chainName, out var proceduralEntry))
            {
                resolvedWeight = proceduralEntry.Weight;
                return proceduralEntry.Target;
            }

            var (winningGroup, weight) = ResolveWinningIKGroup(chainName);
            resolvedWeight = weight;

            if (winningGroup == null || weight <= 0f)
            {
                return Matrix.Identity;
            }

            var ikEvent = winningGroup[0].Event;

            if (string.IsNullOrEmpty(ikEvent.TargetReference))
            {
                resolvedWeight = 0f;
                return Matrix.Identity;
            }

            var chain = model.FindIKChain(chainName);
            BoneSRT blendedDelta = default;
            float accumulated = 0f;

            foreach (var c in winningGroup)
            {
                BoneSRT delta = c.Clip.GetOrCaptureIKDelta(model, chain, c.Event, i => modelSpace[i]);
                accumulated += c.Weight;
                blendedDelta = accumulated <= c.Weight
                    ? delta
                    : BoneSRT.Lerp(blendedDelta, delta, c.Weight / accumulated);
            }

            Matrix refWorld = ResolveModelSpaceReference(ikEvent.TargetReference);
            return blendedDelta.ToMatrix() * refWorld;
        }

        internal void BeginIKResultsFrame()
        {
            lastIKResults.Clear();
        }

        internal void RecordIKResult(string chainName, Matrix target, float weight)
        {
            lastIKResults[chainName] = new IKResult { Target = target, Weight = weight };
        }

        internal void EndIKResolutionFrame()
        {
            var stale = new List<string>();
            foreach (var key in proceduralIKTargets.Keys)
            {
                if (!proceduralIKTargetsSetThisTick.Contains(key))
                {
                    stale.Add(key);
                }
            }
            foreach (var key in stale)
            {
                proceduralIKTargets.Remove(key);
            }
            proceduralIKTargetsSetThisTick.Clear();
        }

        private Matrix ResolveModelSpaceReference(string reference)
        {
            var attachment = model.FindAttachment(reference);
            if (attachment != null)
            {
                return attachment.Offset * modelSpace[attachment.BoneID];
            }

            int boneIndex = model.Bones.FindIndex(b => b.Name == reference);
            return boneIndex >= 0 ? modelSpace[boneIndex] : Matrix.Identity;
        }
        public Vector3 GetAggregateRootMotionDelta()
        {
            Vector3 total = Vector3.Zero;
            foreach (var layer in GetOrderedLayers())
            {
                if (layer.Influence > 0f)
                {
                    total += layer.RootMotionDelta * layer.Influence;
                }
            }
            return total;
        }
        public Matrix[] GetFinalTransforms()
        {
            for (int b = 0; b < finalTransformsOutput.Length; b++)
            {
                finalTransformsOutput[b] = model.Bones[b].Offset * model.ModelTransforms[b];
            }
            return finalTransformsOutput;
        }
    }
}
