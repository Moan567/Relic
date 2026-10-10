using Avalonia.Threading;
using Relic;
using Relic.Models;
using Relic.Models.Data;
using Relic.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using Newtonsoft.Json;
using Rockwall;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Input;
using Rockwall2.Editor.Common.Utils;
using Rockwall2.Editor.Mapper;
using Rockwall2.Editor.Model.Utils;
using Rockwall2.Views;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Model;
public class ModelView : IEditorScene
{
    public static ModelView Instance;
    private EditorHost host;
    private SpriteBatch spriteBatch = default!;
    private Effect modelDisplay, skinnedModelDisplay, skinnedEye, eyeTextureGen;
    private int cameraLight;

    public bool EyeSetupActive = false;
    private Vector3? pickedEyeCenter;
    private Vector3? pickedEyeCenterNormal;
    private float eyeSetupDotSize = 0.006f;
    private const float EyeVertexPickRadius = 6f;

    private bool eyeVertexHovered = false;
    private bool eyeFaceCamera = true;
    private int hoveredEyeBodygroupIndex = -1;
    private int hoveredEyeVertexIndex = -1;
    private Vector3 hoveredEyeVertexPos;
    private Vector3 hoveredEyeVertexNormal;

    private int pickedEyeCenterBodygroupIndex = -1;
    private int pickedEyeCenterVertexIndex = -1;

    private Texture2D eyeSetupBlackTexture, eyeSetupWhiteTexture;
    private Texture2D eyeTextureColorPreview, eyeTextureDataPreview;

    private BasicEffect basicEffect;

    private PlaneGrid grid;

    public static FixedList<Light> realtimeLights = new FixedList<Light>(256);
    public static Vector4[] shaderRealtimeLightPositions;
    public static Vector4[] shaderRealtimeLightColors;
    public static Vector4[] shaderRealtimeLightSpotData;
    public static int shaderRealtimeLightCount;

    private const int MaxShaderEyes = 4;
    private static Vector4[] shaderEyeCenterRadius = new Vector4[MaxShaderEyes];
    private static Vector3[] shaderEyeForward = new Vector3[MaxShaderEyes];
    private static Vector3[] shaderEyeRight = new Vector3[MaxShaderEyes];
    private static Vector3[] shaderEyeUp = new Vector3[MaxShaderEyes]; 
    private static float[] shaderEyeThetaFOV = new float[MaxShaderEyes];

    private Vector3 sunDirection = Vector3.Normalize(new Vector3(0.25f, 2, 0.5f));
    private Vector3 sunRight = Vector3.Right;
    private Vector3 sunUp = Vector3.Forward;
    private Quaternion sunRotation;
    private Color sunColor = new Color(255, 255, 255), ambientColor = new Color(255, 255, 255), fogColor = new Color(0, 0, 0);
    private float sunStrength = 0.8f, ambientStrength = 0.0f, fogStart = 1f, fogEnd = 10f, fogStrength = 0f;

    private VertexPosition[] boxVerts;
    private VertexPosition[] crossVerts;
    private enum AttachHandleType { None, X, Y, Z }
    private AttachHandleType hoveredAttachHandle = AttachHandleType.None;
    private AttachHandleType draggingAttachHandle = AttachHandleType.None;
    private Vector2 hAttachX, hAttachY, hAttachZ;
    private const float AttachHandleLength = 0.18f;
    private const float AttachHandleHitRadius = 14f;
    private Vector2 attachProjectedAxis;
    private float attachPixelsPerWorldUnit;
    private Vector3 floatingAttachWorldPos;

    private enum AttachRotateHandleType { None, X, Y, Z }
    private AttachRotateHandleType hoveredAttachRotateHandle = AttachRotateHandleType.None;
    private AttachRotateHandleType draggingAttachRotateHandle = AttachRotateHandleType.None;
    private Vector3 dragRotateAxis;
    private Vector3 dragRotatePivot;
    private float previousRotateAngle;

    private const float AttachRingRadius = AttachHandleLength * 0.3f;
    private const float AttachRingThickness = AttachRingRadius * 0.05f;
    private const float AttachRingPickTolerance = AttachRingRadius * 0.18f;

    private List<VertexPositionColor> attachRingVertsX = new();
    private List<int> attachRingIndicesX = new();
    private List<VertexPositionColor> attachRingVertsY = new();
    private List<int> attachRingIndicesY = new();
    private List<VertexPositionColor> attachRingVertsZ = new();
    private List<int> attachRingIndicesZ = new();

    private static readonly Color ColAttachX = new Color(255, 60, 60);
    private static readonly Color ColAttachY = new Color(60, 220, 60);
    private static readonly Color ColAttachZ = new Color(60, 140, 255);

    private enum JointHandleType { None, NormalCone, PlaneCone, TwistMin, TwistMax }
    private JointHandleType _draggingHandle = JointHandleType.None;
    private Microsoft.Xna.Framework.Point dragStartMouse;
    private float dragStartValue;
    private Vector2 hNormalTip, hPlaneTip, hTwistMinTip, hTwistMaxTip;
    private const float HandleHitRadius = 12f;
    private const float HandleLength = 0.15f;
    private const float DragSensitivity = 0.005f;

    private static readonly Color ColNormal = new Color(80, 220, 100);
    private static readonly Color ColPlane = new Color(80, 160, 255);
    private static readonly Color ColTwist = new Color(255, 160, 30);
    private static readonly Color ColTwistMin = new Color(255, 70, 70);
    private static readonly Color ColTwistMax = new Color(220, 220, 220);
    private static readonly Color ColAxis = new Color(255, 230, 50);

    // Attachment preview stuff
    private Matrix[] previewBoneTransforms;
    public Relic.Models.CModel PreviewModel;
    public int PreviewAttachmentIndex = -1;

    int targWidth;
    int targHeight;

    // Camera
    private Orbit3DCamera orbitCamera = new Orbit3DCamera();

    public ModelView()
    {
        Instance = this;
    }

    public void Attach(EditorHost host)
    {
        Instance = this;

        this.host = host;

        spriteBatch = new SpriteBatch(host.GraphicsDevice);

        modelDisplay = host.Content.Load<Effect>("Shaders/ModelDefault");
        skinnedModelDisplay = host.Content.Load<Effect>("Shaders/SkinnedModelDefault");
        skinnedEye = host.Content.Load<Effect>("Shaders/EyeShader");
        eyeTextureGen = host.Content.Load<Effect>("Shaders/EyeGeneration");

        eyeSetupBlackTexture = new Texture2D(host.GraphicsDevice, 1, 1);
        eyeSetupBlackTexture.SetData(new[] { Color.Black });
        eyeSetupWhiteTexture = new Texture2D(host.GraphicsDevice, 1, 1);
        eyeSetupWhiteTexture.SetData(new[] { Color.White });

        grid = new PlaneGrid(host.GraphicsDevice, 1, 25, new Plane(Vector3.Up, 0));

        orbitCamera.RebuildMatrix();
        orbitCamera.position = new Vector3(0,8,8);

        cameraLight = realtimeLights.Add(new Light
        {
            Color = Color.White,
            Range = 20,
            Intensity = 0.03f,
            Type = Light.LightType.Point
        });

        boxVerts = CMath.GetDebugEdges(new BoundingBox(-Vector3.One * 0.5f, Vector3.One * 0.5f));
        crossVerts = new VertexPosition[6]
        {
            new(Vector3.Forward),
            new(-Vector3.Forward),
            new(Vector3.Right),
            new(-Vector3.Right),
            new(Vector3.Up),
            new(-Vector3.Up),
        };
        basicEffect = new BasicEffect(host.GraphicsDevice);

        BrushOperations.AddRing(attachRingVertsX, attachRingIndicesX, Vector3.UnitX, ColAttachX, AttachRingRadius, AttachRingThickness, 32);
        BrushOperations.AddRing(attachRingVertsY, attachRingIndicesY, Vector3.UnitY, ColAttachY, AttachRingRadius, AttachRingThickness, 32);
        BrushOperations.AddRing(attachRingVertsZ, attachRingIndicesZ, Vector3.UnitZ, ColAttachZ, AttachRingRadius, AttachRingThickness, 32);

        var control = MainWindow.Instance.modelEditor.gameControl;
        control.PointerPressed += OnControlPointerPressed;
        control.PointerReleased += OnControlPointerReleased;
    }
    public void Detach()
    {
        var control = MainWindow.Instance.modelEditor.gameControl;
        control.PointerPressed -= OnControlPointerPressed;
        control.PointerReleased -= OnControlPointerReleased;
    }
    private void OnControlPointerPressed(object sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        if (orbitCamera.flyPanMode) return; // don't steal input from camera
        GrabGrips();
    }
    private void OnControlPointerReleased(object sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (e.GetCurrentPoint(null).Properties.PointerUpdateKind
            != Avalonia.Input.PointerUpdateKind.LeftButtonReleased) return;
        ReleaseGrips();
    }
    public void Resize(int w, int h)
    {
        targWidth = w;
        targHeight = h;
        if (orbitCamera != null) orbitCamera.AspectRatio = w / (float)h;
    }
    public Vector2? GlobalToLocal(Vector2 global)
    {
        var rect = MainWindow.Instance.modelEditor.gameControl.Bounds;

        var local = global - new Vector2((float)rect.X, (float)rect.Y);
        if (local.X < 0 || local.Y < 0 || local.X > rect.Width || local.Y > rect.Height) return null;
        return local;
    }
    public void Update(GameTime gameTime)
    {
        orbitCamera.AspectRatio = targWidth / (float)targHeight;

        var mousePos = MouseManager.GetXnaPositionRelativeTo(MainWindow.Instance.modelEditor.gameControl).ToVector2();

        UpdateAttachHover();
        UpdateEyeSetupHover();

        if ((ModelEditorData.ViewingBone >= 0 || ModelEditorData.ViewingAttachment >= 0 || EyeSetupActive) && !orbitCamera.flyPanMode)
        {
            orbitCamera.blockOrbit =
                Vector2.Distance(mousePos, hNormalTip) <= HandleHitRadius ||
                Vector2.Distance(mousePos, hPlaneTip) <= HandleHitRadius ||
                Vector2.Distance(mousePos, hTwistMinTip) <= HandleHitRadius ||
                Vector2.Distance(mousePos, hTwistMaxTip) <= HandleHitRadius ||
                _draggingHandle != JointHandleType.None ||
                hoveredAttachHandle != AttachHandleType.None ||
                draggingAttachHandle != AttachHandleType.None ||
                hoveredAttachRotateHandle != AttachRotateHandleType.None ||
                draggingAttachRotateHandle != AttachRotateHandleType.None ||
                eyeVertexHovered;
        }
        else
        {
            orbitCamera.blockOrbit = false;
        }

        var rect = MainWindow.Instance.modelEditor.gameControl.Bounds;
        bool overControl = mousePos.X >= 0 && mousePos.Y >= 0 && mousePos.X <= rect.Width && mousePos.Y <= rect.Height;

        if (!MainWindow.Instance.modelEditor.IsPointerOver || !overControl)
            orbitCamera.blockOrbit = true;

        orbitCamera.Update(gameTime);

        realtimeLights[cameraLight] = new Light
        {
            Position = orbitCamera.position,
            Color = Color.White,
            Range = 20,
            Intensity = 0.1f,
            Type = Light.LightType.Point
        };

        PrepLightParams();

        if (ModelEditorData.PreviewAnimator != null)
        {
            ModelEditorData.PreviewAnimator.Update((float)gameTime.ElapsedGameTime.TotalSeconds);

            if (!ModelEditorData.PreviewAnimator.Paused)
            {
                ModelEditorData.PreviewRootMotionAccum += ModelEditorData.PreviewAnimator.GetAggregateRootMotionDelta();
            }

            CIKSolver.ApplyChains(ModelEditorData.ActiveModel, ModelEditorData.PreviewAnimator);
        }
        else
        {
            ModelEditorData.SequencePlayer?.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
        }

        if (ModelEditorData.MorphState?.IsDirty == true && ModelEditorData.ActiveModel != null)
        {
            foreach (var bg in ModelEditorData.ActiveModel.Bodygroups)
            {
                if (bg.MorphApplicator != null && bg.MorphTargets?.Count > 0)
                    bg.MorphApplicator.Apply(
                        bg.MeshData.Vertices, bg.MorphTargets, ModelEditorData.MorphState);
            }
            ModelEditorData.MorphState.ClearDirty();
        }

        if (ModelEditorData.SequencePlayer?.IsPlaying == true)
        {
            Dispatcher.UIThread.Post(MainWindow.Instance.modelEditor.RedrawTimeline);
        }

        if (orbitCamera.flyPanMode) return;

        MoveGrips();
    }
    private void DrawIKChainSkeletons()
    {
        var model = ModelEditorData.ActiveModel;
        if (model == null || model.ModelTransforms == null)
        {
            return;
        }

        foreach (var chain in model.IKChains)
        {
            if (chain.TopBoneIndex < 0)
            {
                continue;
            }

            Vector3 topPos = model.ModelTransforms[chain.TopBoneIndex].Translation;
            Vector3 midPos = model.ModelTransforms[chain.MidBoneIndex].Translation;
            Vector3 endPos = model.ModelTransforms[chain.EndBoneIndex].Translation;

            var dim = new Color(180, 180, 180);

            DrawLines(new[] { new VertexPosition(topPos), new VertexPosition(midPos) }, 1, dim, 0.35f);
            DrawLines(new[] { new VertexPosition(midPos), new VertexPosition(endPos) }, 1, dim, 0.35f);

            DrawDiamond(topPos, dim, eyeSetupDotSize * 0.9f);
            DrawDiamond(midPos, dim, eyeSetupDotSize * 0.9f);
        }
    }
    private void DrawFootOrientationGizmos()
    {
        var model = ModelEditorData.ActiveModel;
        if (model == null || model.ModelTransforms == null)
        {
            return;
        }

        foreach (var chain in model.IKChains)
        {
            if (chain.Role != CModel.CIKChainRole.Foot || chain.EndBoneIndex < 0)
            {
                continue;
            }

            Matrix effector = CIKSolver.GetEffectorTransform(chain, model.ModelTransforms[chain.EndBoneIndex]);
            effector.Decompose(out _, out Quaternion effectorRot, out Vector3 effectorPos);

            Quaternion effectiveRot = Quaternion.Normalize(effectorRot * chain.LocalOrientationOffset);
            Vector3 effectiveUp = Vector3.Transform(Vector3.Up, effectiveRot);

            const float rayLength = 0.15f;
            Vector3 tip = effectorPos + effectiveUp * rayLength;

            DrawLines(new[] { new VertexPosition(effectorPos), new VertexPosition(tip) }, 1, new Color(255, 230, 60), 0.9f);
            DrawDiamond(tip, new Color(255, 230, 60), eyeSetupDotSize * 1.1f);
        }
    }
    private void DrawIKSystemGizmos()
    {
        var model = ModelEditorData.ActiveModel;
        var animator = ModelEditorData.PreviewAnimator;
        if (model == null || animator == null)
        {
            return;
        }

        foreach (var chain in model.IKChains)
        {
            if (chain.EndBoneIndex < 0)
            {
                continue;
            }

            if (!animator.LastIKResults.TryGetValue(chain.ChainName, out var result))
            {
                continue;
            }

            Vector3 actualPos = CIKSolver.GetEffectorTransform(chain, model.ModelTransforms[chain.EndBoneIndex]).Translation;
            Vector3 targetPos = result.Target.Translation;

            Color chainColor = chain.Role == CModel.CIKChainRole.Foot
                ? new Color(80, 230, 120)
                : new Color(60, 180, 255);

            DrawDiamond(actualPos, chainColor, eyeSetupDotSize * 1.6f);
            DrawDiamond(targetPos, Color.White, eyeSetupDotSize * 1.2f);
            DrawLines(new[] { new VertexPosition(actualPos), new VertexPosition(targetPos) }, 1, chainColor, 0.25f + 0.65f * result.Weight);

            if (chain.Role == CModel.CIKChainRole.Foot)
            {
                var circleVerts = new List<VertexPositionColor>();
                GizmoShapes.AddCircle(circleVerts, targetPos, 0.12f, Vector3.Right, Vector3.Forward, chainColor);

                var circlePositions = new VertexPosition[circleVerts.Count];
                for (int i = 0; i < circleVerts.Count; i++)
                {
                    circlePositions[i] = new VertexPosition(circleVerts[i].Position);
                }

                DrawLines(circlePositions, circlePositions.Length / 2, chainColor, 0.5f + 0.5f * result.Weight);
            }
        }
    }
    private void MoveGrips()
    {
        if (draggingAttachRotateHandle != AttachRotateHandleType.None && ModelEditorData.ViewingAttachment >= 0)
        {
            var att = ModelEditorData.ActiveModel.AttachmentPoints[ModelEditorData.ViewingAttachment];
            var modelTransforms = ModelEditorData.SequencePlayer != null
                ? ModelEditorData.SequencePlayer.ModelSpaceTransforms.ToArray()
                : ModelEditorData.ActiveModel.BoneTransforms;
            var boneTransform = modelTransforms[att.BoneID];

            var cursor = MouseManager.GetXnaPositionRelativeTo(MainWindow.Instance.modelEditor.gameControl).ToVector2();
            float currentAngle = MeasureAttachRotationAngle(dragRotateAxis, dragRotatePivot, cursor);
            float delta = currentAngle - previousRotateAngle;
            while (delta > MathHelper.Pi) delta -= MathHelper.TwoPi;
            while (delta < -MathHelper.Pi) delta += MathHelper.TwoPi;
            previousRotateAngle = currentAngle;

            var currentWorld = att.GetTransform(modelTransforms);
            currentWorld.Decompose(out _, out Quaternion worldRot, out Vector3 worldPos);

            var deltaQuat = Quaternion.CreateFromAxisAngle(dragRotateAxis, delta);
            var newWorldRot = worldRot * deltaQuat;

            var newWorld = Matrix.CreateFromQuaternion(newWorldRot) * Matrix.CreateTranslation(worldPos);
            var newOffset = newWorld * Matrix.Invert(boneTransform);
            newOffset.Decompose(out _, out Quaternion newOffsetRot, out Vector3 newOffsetPos);

            att.Offset = Matrix.CreateFromQuaternion(newOffsetRot) * Matrix.CreateTranslation(newOffsetPos);

            Dispatcher.UIThread.Post(MainWindow.Instance.modelEditor.RefreshAttachmentFields);
            return;
        }

        if (draggingAttachHandle != AttachHandleType.None && ModelEditorData.ViewingAttachment >= 0)
        {
            var att = ModelEditorData.ActiveModel.AttachmentPoints[ModelEditorData.ViewingAttachment];
            var modelTransforms = ModelEditorData.SequencePlayer != null
                ? ModelEditorData.SequencePlayer.ModelSpaceTransforms.ToArray()
                : ModelEditorData.ActiveModel.BoneTransforms;
            var boneTransform = modelTransforms[att.BoneID];
            var attachWorld = att.GetTransform(modelTransforms);

            Vector3 axis = draggingAttachHandle switch
            {
                AttachHandleType.X => attachWorld.Right,
                AttachHandleType.Y => attachWorld.Up,
                AttachHandleType.Z => attachWorld.Backward,
                _ => Vector3.Zero
            };

            Vector2 mouseDelta = new Vector2((float)MouseManager.Delta.X, (float)MouseManager.Delta.Y);
            float movement = attachPixelsPerWorldUnit > 0.0001f
                ? Vector2.Dot(mouseDelta, attachProjectedAxis) / attachPixelsPerWorldUnit
                : 0f;

            floatingAttachWorldPos += axis * movement;

            var invBone = Matrix.Invert(boneTransform);
            var newLocalPos = Vector3.Transform(floatingAttachWorldPos, invBone);

            att.Offset.Decompose(out _, out Quaternion currentRot, out _);
            att.Offset = Matrix.CreateFromQuaternion(currentRot) * Matrix.CreateTranslation(newLocalPos);

            Dispatcher.UIThread.Post(MainWindow.Instance.modelEditor.RefreshAttachmentFields);
            return;
        }

        if (_draggingHandle == JointHandleType.None) return;
        if (ModelEditorData.ViewingBone == -1) { _draggingHandle = JointHandleType.None; return; }

        int dx = (int)(MouseManager.Position.X - dragStartMouse.X);
        float newVal = dragStartValue + dx * DragSensitivity;

        if (_draggingHandle == JointHandleType.NormalCone || _draggingHandle == JointHandleType.PlaneCone)
            newVal = Math.Clamp(newVal, 0f, MathHelper.Pi * 0.95f);
        else
            newVal = Math.Clamp(newVal, -MathHelper.Pi * 0.95f, MathHelper.Pi * 0.95f);

        var bdata = ModelEditorData.ActiveModel.BoneData[ModelEditorData.ViewingBone];
        switch (_draggingHandle)
        {
            case JointHandleType.NormalCone: bdata.JointNormalHalfCone = newVal; break;
            case JointHandleType.PlaneCone: bdata.JointPlaneHalfCone = newVal; break;
            case JointHandleType.TwistMin: bdata.JointTwistMin = newVal; break;
            case JointHandleType.TwistMax: bdata.JointTwistMax = newVal; break;
        }
        ModelEditorData.ActiveModel.BoneData[ModelEditorData.ViewingBone] = bdata;
        var bone = ModelEditorData.ActiveModel.Bones[ModelEditorData.ViewingBone];
        switch (_draggingHandle)
        {
            case JointHandleType.NormalCone: bone.JointNormalHalfCone = newVal; break;
            case JointHandleType.PlaneCone: bone.JointPlaneHalfCone = newVal; break;
            case JointHandleType.TwistMin: bone.JointTwistMin = newVal; break;
            case JointHandleType.TwistMax: bone.JointTwistMax = newVal; break;
        }

        var b = ModelEditorData.ActiveModel.Bones[ModelEditorData.ViewingBone];
        var bd = ModelEditorData.ActiveModel.BoneData[ModelEditorData.ViewingBone];

        if (_draggingHandle == JointHandleType.TwistMin && b.JointTwistMin > b.JointTwistMax)
        {
            b.JointTwistMin = b.JointTwistMax;
            bd.JointTwistMin = bd.JointTwistMax;
        }
        else if (_draggingHandle == JointHandleType.TwistMax && b.JointTwistMax < b.JointTwistMin)
        {
            b.JointTwistMax = b.JointTwistMin;
            bd.JointTwistMax = bd.JointTwistMin;
        }
        ModelEditorData.ActiveModel.BoneData[ModelEditorData.ViewingBone] = bd;
    }
    private void ReleaseGrips()
    {
        if (draggingAttachHandle != AttachHandleType.None)
        {
            draggingAttachHandle = AttachHandleType.None;
            return;
        }
        if (draggingAttachRotateHandle != AttachRotateHandleType.None)
        {
            draggingAttachRotateHandle = AttachRotateHandleType.None;
            return;
        }
        if (_draggingHandle != JointHandleType.None)
        {
            _draggingHandle = JointHandleType.None;
            return;
        }
    }
    private void GrabGrips()
    {
        var cursor = MouseManager.GetXnaPositionRelativeTo(MainWindow.Instance.modelEditor.gameControl).ToVector2();
        if (EyeSetupActive)
        {
            HandleEyeSetupClick();
            return;
        }

        if (ModelEditorData.ViewingAttachment >= 0 && ModelEditorData.ActiveModel?.AttachmentPoints?.Count > 0)
        {
            var att = ModelEditorData.ActiveModel.AttachmentPoints[ModelEditorData.ViewingAttachment];
            var modelTransforms = ModelEditorData.SequencePlayer != null
                ? ModelEditorData.SequencePlayer.ModelSpaceTransforms.ToArray()
                : ModelEditorData.ActiveModel.BoneTransforms;
            var attachWorld = att.GetTransform(modelTransforms);

            (AttachHandleType type, Vector2 tip)[] attHandles =
            {
                (AttachHandleType.X, hAttachX),
                (AttachHandleType.Y, hAttachY),
                (AttachHandleType.Z, hAttachZ),
            };
            foreach (var (type, tip) in attHandles)
            {
                if (Vector2.Distance(cursor, tip) <= AttachHandleHitRadius)
                {
                    draggingAttachHandle = type;
                    floatingAttachWorldPos = attachWorld.Translation;

                    Vector3 axis = type switch
                    {
                        AttachHandleType.X => attachWorld.Right,
                        AttachHandleType.Y => attachWorld.Up,
                        AttachHandleType.Z => attachWorld.Backward,
                        _ => Vector3.Zero
                    };
                    (attachProjectedAxis, attachPixelsPerWorldUnit) = ProjectAxisToScreen(floatingAttachWorldPos, axis);
                    return;
                }
            }

            if (hoveredAttachRotateHandle != AttachRotateHandleType.None)
            {
                draggingAttachRotateHandle = hoveredAttachRotateHandle;
                dragRotateAxis = draggingAttachRotateHandle switch
                {
                    AttachRotateHandleType.X => Vector3.UnitX,
                    AttachRotateHandleType.Y => Vector3.UnitY,
                    AttachRotateHandleType.Z => Vector3.UnitZ,
                    _ => Vector3.Zero
                };
                dragRotatePivot = attachWorld.Translation;
                previousRotateAngle = MeasureAttachRotationAngle(dragRotateAxis, dragRotatePivot, cursor);
                return;
            }
        }

        if (ModelEditorData.ViewingBone == -1) return;

        (JointHandleType type, Vector2 tip)[] handles =
        {
            (JointHandleType.NormalCone, hNormalTip),
            (JointHandleType.PlaneCone,  hPlaneTip),
            (JointHandleType.TwistMin,   hTwistMinTip),
            (JointHandleType.TwistMax,   hTwistMaxTip),
        };

        var bone = ModelEditorData.ActiveModel.Bones[ModelEditorData.ViewingBone];
        foreach (var (type, tip) in handles)
        {
            if (Vector2.Distance(cursor, tip) <= HandleHitRadius)
            {
                _draggingHandle = type;
                dragStartMouse = new Microsoft.Xna.Framework.Point((int)MouseManager.Position.X, (int)MouseManager.Position.Y);
                dragStartValue = type switch
                {
                    JointHandleType.NormalCone => bone.JointNormalHalfCone,
                    JointHandleType.PlaneCone => bone.JointPlaneHalfCone,
                    JointHandleType.TwistMin => bone.JointTwistMin,
                    JointHandleType.TwistMax => bone.JointTwistMax,
                    _ => 0f
                };
                return;
            }
        }
    }

    public void EnterEyeSetupMode()
    {
        EyeSetupActive = true;
        pickedEyeCenter = null;
        pickedEyeCenterNormal = null;
        pickedEyeCenterBodygroupIndex = -1;
        pickedEyeCenterVertexIndex = -1;
        FocusCameraOnEyeGeometry();
    }

    private void FocusCameraOnEyeGeometry()
    {
        var bones = ModelEditorData.ActiveModel.BoneTransforms;
        if (bones == null) return;

        List<Vector3> points = new();
        foreach (var bodygroup in ModelEditorData.ActiveModel.Bodygroups)
        {
            if (!bodygroup.IsEye) continue;
            foreach (var vertex in bodygroup.MeshData.Vertices)
                points.Add(SkinPosition(vertex, bones));
        }
        if (points.Count == 0) return;

        Vector3 center = Vector3.Zero;
        foreach (var p in points) center += p;
        center /= points.Count;

        float radius = 0.001f;
        foreach (var p in points) radius = MathF.Max(radius, Vector3.Distance(p, center));

        eyeSetupDotSize = MathF.Max(0.002f, radius * 0.02f);
        orbitCamera.FocusOn(center, radius);
    }

    public void ExitEyeSetupMode()
    {
        EyeSetupActive = false;
        pickedEyeCenter = null;
        pickedEyeCenterNormal = null;
        pickedEyeCenterBodygroupIndex = -1;
        pickedEyeCenterVertexIndex = -1;
    }

    private static Vector3 SkinPosition(CSkinnedVertex vertex, Matrix[] bones)
    {
        Vector4 indices = vertex.BlendIndices.ToVector4();
        Vector4 weights = vertex.BlendWeights;
        Vector3 result = Vector3.Zero;

        if (weights.X > 0) result += Vector3.Transform(vertex.Position, bones[(int)indices.X]) * weights.X;
        if (weights.Y > 0) result += Vector3.Transform(vertex.Position, bones[(int)indices.Y]) * weights.Y;
        if (weights.Z > 0) result += Vector3.Transform(vertex.Position, bones[(int)indices.Z]) * weights.Z;
        if (weights.W > 0) result += Vector3.Transform(vertex.Position, bones[(int)indices.W]) * weights.W;

        return result;
    }

    private static Vector3 SkinNormal(CSkinnedVertex vertex, Matrix[] bones)
    {
        Vector4 indices = vertex.BlendIndices.ToVector4();
        Vector4 weights = vertex.BlendWeights;
        Vector3 result = Vector3.Zero;

        if (weights.X > 0) result += Vector3.TransformNormal(vertex.Normal, bones[(int)indices.X]) * weights.X;
        if (weights.Y > 0) result += Vector3.TransformNormal(vertex.Normal, bones[(int)indices.Y]) * weights.Y;
        if (weights.Z > 0) result += Vector3.TransformNormal(vertex.Normal, bones[(int)indices.Z]) * weights.Z;
        if (weights.W > 0) result += Vector3.TransformNormal(vertex.Normal, bones[(int)indices.W]) * weights.W;

        return Vector3.Normalize(result);
    }

    private void HandleEyeSetupClick()
    {
        if (!eyeVertexHovered) return;

        if (pickedEyeCenter == null)
        {
            pickedEyeCenter = hoveredEyeVertexPos;
            pickedEyeCenterNormal = hoveredEyeVertexNormal;
            pickedEyeCenterBodygroupIndex = hoveredEyeBodygroupIndex;
            pickedEyeCenterVertexIndex = hoveredEyeVertexIndex;
            return;
        }

        FinalizeEyePick(pickedEyeCenter.Value, pickedEyeCenterNormal.Value, hoveredEyeVertexPos,
            pickedEyeCenterBodygroupIndex, pickedEyeCenterVertexIndex);
        ExitEyeSetupMode();
        Dispatcher.UIThread.Post(MainWindow.Instance.modelEditor.OnEyeSetupComplete);
    }

    public void RecomputeEyeOffset(int eyeIndex)
    {
        var model = ModelEditorData.ActiveModel;
        if (model?.EyeDefs == null || eyeIndex < 0 || eyeIndex >= model.EyeDefs.Count) return;

        var eyeDef = model.EyeDefs[eyeIndex];
        if (eyeDef.SetupVertexIndex < 0) return;

        eyeDef.HeadOffsetMatrix = BuildEyeOffsetMatrix(eyeDef.SetupLocalPosition, eyeDef.SetupLocalNormal, eyeDef.EyeRadius);
    }

    private void FinalizeEyePick(Vector3 centerPos, Vector3 centerNormal, Vector3 edgePos, int centerBodygroupIndex, int centerVertexIndex)
    {
        if (ModelEditorData.ViewingEye < 0) return;
        var eyeDef = ModelEditorData.ActiveModel.EyeDefs[ModelEditorData.ViewingEye];

        var modelTransforms = ModelEditorData.SequencePlayer != null
            ? ModelEditorData.SequencePlayer.ModelSpaceTransforms.ToArray()
            : ModelEditorData.ActiveModel.BoneTransforms;

        Matrix headBoneTransform = modelTransforms[eyeDef.HeadBoneID];
        Matrix invHead = Matrix.Invert(headBoneTransform);

        eyeDef.SetupLocalPosition = Vector3.Transform(centerPos, invHead);
        eyeDef.SetupLocalNormal = Vector3.Normalize(Vector3.TransformNormal(centerNormal, invHead));
        eyeDef.EyeRadius = Vector3.Distance(centerPos, edgePos);
        eyeDef.HeadOffsetMatrix = BuildEyeOffsetMatrix(eyeDef.SetupLocalPosition, eyeDef.SetupLocalNormal, eyeDef.EyeRadius);
        eyeDef.EyeMatrix = Matrix.Identity;
        eyeDef.SetupBodygroupIndex = centerBodygroupIndex;
        eyeDef.SetupVertexIndex = centerVertexIndex;

        Vector3 sphereCenterWorld = centerPos - Vector3.Normalize(centerNormal) * Vector3.Distance(centerPos, edgePos);
        Vector3 toEdge = Vector3.Normalize(edgePos - sphereCenterWorld);
        float thetaFOV = MathF.Acos(Vector3.Dot(toEdge, Vector3.Normalize(centerNormal)));

        eyeDef.ThetaFOV = thetaFOV;

        RecomputeEyeVertexIndices();
    }

    private static Matrix BuildEyeOffsetMatrix(Vector3 localPosition, Vector3 localNormal, float radius)
    {
        Vector3 forward = Vector3.Normalize(localNormal);
        Vector3 sphereCenter = localPosition - forward * radius; // FIXED!! My models had a fucky scale.

        Vector3 up = Vector3.Up - forward * Vector3.Dot(Vector3.Up, forward);
        if (up.LengthSquared() < 0.001f) up = Vector3.Right - forward * Vector3.Dot(Vector3.Right, forward);
        up = Vector3.Normalize(up);
        Vector3 right = Vector3.Normalize(Vector3.Cross(up, forward));
        up = Vector3.Cross(forward, right);

        return Matrix.CreateWorld(sphereCenter, -forward, up);
    }

    private void RecomputeEyeVertexIndices()
    {
        var model = ModelEditorData.ActiveModel;
        if (model?.EyeDefs == null) return;

        var bones = model.BoneTransforms;
        var modelTransforms = ModelEditorData.SequencePlayer != null
            ? ModelEditorData.SequencePlayer.ModelSpaceTransforms.ToArray()
            : model.BoneTransforms;
        if (bones == null || modelTransforms == null) return;

        for (int bg = 0; bg < model.Bodygroups.Count; bg++)
        {
            var bodygroup = model.Bodygroups[bg];
            if (!bodygroup.IsEye) continue;

            var verts = bodygroup.MeshData.Vertices;
            var indices = bodygroup.MeshData.Indices;

            var adjacency = new List<int>[verts.Length];
            for (int i = 0; i < adjacency.Length; i++) adjacency[i] = new List<int>();
            for (int t = 0; t < indices.Length; t += 3)
            {
                int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                adjacency[a].Add(b); adjacency[a].Add(c);
                adjacency[b].Add(a); adjacency[b].Add(c);
                adjacency[c].Add(a); adjacency[c].Add(b);
            }

            var assigned = new bool[verts.Length];
            for (int v = 0; v < verts.Length; v++) verts[v].EyeIndex = 0f;

            for (int e = 0; e < model.EyeDefs.Count; e++)
            {
                var eyeDef = model.EyeDefs[e];
                if (eyeDef.SetupBodygroupIndex != bg) continue;
                if (eyeDef.SetupVertexIndex < 0 || eyeDef.SetupVertexIndex >= verts.Length) continue;

                var queue = new Queue<int>();
                queue.Enqueue(eyeDef.SetupVertexIndex);
                assigned[eyeDef.SetupVertexIndex] = true;
                verts[eyeDef.SetupVertexIndex].EyeIndex = e;

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    foreach (var next in adjacency[cur])
                    {
                        if (assigned[next]) continue;
                        assigned[next] = true;
                        verts[next].EyeIndex = e;
                        queue.Enqueue(next);
                    }
                }
            }

            for (int v = 0; v < verts.Length; v++)
            {
                if (assigned[v]) continue;

                Vector3 pos = SkinPosition(verts[v], bones);
                int best = -1;
                float bestDist = float.MaxValue;
                for (int e = 0; e < model.EyeDefs.Count; e++)
                {
                    if (model.EyeDefs[e].SetupVertexIndex < 0) continue;
                    float dist = Vector3.Distance(pos, model.EyeDefs[e].GetTransform(modelTransforms).Translation);
                    if (dist < bestDist) { bestDist = dist; best = e; }
                }
                if (best >= 0) verts[v].EyeIndex = best;
            }

            bodygroup.Mesh.VertexBuffer.SetData(0, verts, 0, verts.Length, CSkinnedVertex.VertexDeclaration.VertexStride);
        }
    }

    public void MirrorEye(int index)
    {
        var model = ModelEditorData.ActiveModel;
        var source = model.EyeDefs[index];
        string mirroredName = MirrorEyeName(source.Name);
        if (mirroredName == null) return;

        int existing = model.EyeDefs.FindIndex(e => e.Name == mirroredName);
        Matrix mirror = Matrix.CreateScale(-1, 1, 1);
        Matrix mirroredOffset = mirror * source.HeadOffsetMatrix * mirror;

        if (existing >= 0)
        {
            model.EyeDefs[existing].HeadOffsetMatrix = mirroredOffset;
            model.EyeDefs[existing].HeadBoneID = source.HeadBoneID;
            model.EyeDefs[existing].EyeRadius = source.EyeRadius;
        }
        else
        {
            model.EyeDefs.Add(new CModel.CEyeDef
            {
                Name = mirroredName,
                HeadBoneID = source.HeadBoneID,
                HeadOffsetMatrix = mirroredOffset,
                EyeMatrix = Matrix.Identity,
                EyeRadius = source.EyeRadius
            });
        }
    }

    private static string MirrorEyeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        (string a, string b)[] pairs = { ("_L", "_R"), ("_l", "_r"), ("_Left", "_Right"), ("_left", "_right") };
        foreach (var (a, b) in pairs)
        {
            if (name.EndsWith(a, StringComparison.Ordinal)) return name[..^a.Length] + b;
            if (name.EndsWith(b, StringComparison.Ordinal)) return name[..^b.Length] + a;
        }
        return null;
    }

    private void DrawEyeGizmos(Matrix[] modelTransforms)
    {
        if (ModelEditorData.ActiveModel?.EyeDefs == null || modelTransforms == null) return;
        if (ModelEditorData.ViewingEye == -1 || ModelEditorData.ViewingEye >= ModelEditorData.ActiveModel.EyeDefs.Count) return;

        for (int i = 0; i < ModelEditorData.ActiveModel.EyeDefs.Count; i++)
        {
            var eyeDef = ModelEditorData.ActiveModel.EyeDefs[i];
            if (eyeDef.HeadBoneID < 0 || eyeDef.HeadBoneID >= modelTransforms.Length) continue;

            Matrix eyeWorld = eyeDef.GetTransform(modelTransforms);
            Vector3 center = eyeWorld.Translation;

            bool selected = i == ModelEditorData.ViewingEye;
            Color color = selected ? new Color(255, 220, 60) : new Color(120, 200, 255);
            float alpha = selected ? 1f : 0.5f;

            var sphereVerts = GizmoShapes.WireSphere(center, eyeDef.EyeRadius, color);
            var positions = new VertexPosition[sphereVerts.Length];
            for (int v = 0; v < sphereVerts.Length; v++) positions[v] = new VertexPosition(sphereVerts[v].Position);
            DrawLines(positions, positions.Length / 2, color, alpha);

            var equatorVerts = new List<VertexPositionColor>();

            GizmoShapes.AddCircle(equatorVerts, center, eyeDef.EyeRadius, Vector3.Right, Vector3.Up, color);

            var equator = new VertexPosition[equatorVerts.Count];

            for (int j = 0; j < equatorVerts.Count; j++)
                equator[j] = new VertexPosition(equatorVerts[j].Position);

            DrawLines(equator, equator.Length / 2, color, alpha);
        }
    }

    private void DrawEyeSetupOverlay()
    {
        var bones = ModelEditorData.ActiveModel.BoneTransforms;
        if (bones == null) return;

        foreach (var bodygroup in ModelEditorData.ActiveModel.Bodygroups)
        {
            if (!bodygroup.IsEye || !bodygroup.IsSkinned) continue;

            skinnedModelDisplay.Parameters["Bones"]?.SetValue(bones);
            PrepModelShaders(bodygroup.MaterialID, Matrix.Identity);
            skinnedModelDisplay.Parameters["MainTex"]?.SetValue(eyeSetupBlackTexture);

            host.GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            host.GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            foreach (var pass in skinnedModelDisplay.CurrentTechnique.Passes)
            {
                pass.Apply();
                bodygroup.Mesh.Draw();
            }

            skinnedModelDisplay.Parameters["MainTex"]?.SetValue(eyeSetupWhiteTexture);
            host.GraphicsDevice.RasterizerState = new RasterizerState { FillMode = FillMode.WireFrame, CullMode = CullMode.None };
            host.GraphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
            foreach (var pass in skinnedModelDisplay.CurrentTechnique.Passes)
            {
                pass.Apply();
                bodygroup.Mesh.Draw();
            }

            host.GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
            host.GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        }
    }

    private void UpdateEyeSetupHover()
    {
        eyeVertexHovered = false;
        hoveredEyeBodygroupIndex = -1;
        hoveredEyeVertexIndex = -1;
        if (!EyeSetupActive) return;

        var bones = ModelEditorData.ActiveModel?.BoneTransforms;
        if (bones == null) return;

        var mousePos = MouseManager.GetXnaPositionRelativeTo(MainWindow.Instance.modelEditor.gameControl).ToVector2();

        float bestDist = float.MaxValue;
        var bodygroups = ModelEditorData.ActiveModel.Bodygroups;
        for (int b = 0; b < bodygroups.Count; b++)
        {
            if (!bodygroups[b].IsEye) continue;
            var verts = bodygroups[b].MeshData.Vertices;
            for (int v = 0; v < verts.Length; v++)
            {
                Vector3 pos = SkinPosition(verts[v], bones);
                Vector2 screen = WorldToScreen(pos);
                float dist = Vector2.Distance(mousePos, screen);
                if (dist <= EyeVertexPickRadius && dist < bestDist)
                {
                    bestDist = dist;
                    hoveredEyeBodygroupIndex = b;
                    hoveredEyeVertexIndex = v;
                    hoveredEyeVertexPos = pos;
                    hoveredEyeVertexNormal = SkinNormal(verts[v], bones);
                    eyeVertexHovered = true;
                }
            }
        }
    }

    private void DrawEyeSetupVertexDots()
    {
        var bones = ModelEditorData.ActiveModel.BoneTransforms;
        if (bones == null) return;

        var bodygroups = ModelEditorData.ActiveModel.Bodygroups;
        for (int b = 0; b < bodygroups.Count; b++)
        {
            if (!bodygroups[b].IsEye) continue;
            var verts = bodygroups[b].MeshData.Vertices;
            for (int v = 0; v < verts.Length; v++)
            {
                bool isHovered = eyeVertexHovered && b == hoveredEyeBodygroupIndex && v == hoveredEyeVertexIndex;
                Color color = isHovered ? new Color(120, 255, 120) : new Color(80, 220, 255);
                float size = isHovered ? eyeSetupDotSize * 2.2f : eyeSetupDotSize;
                DrawDiamond(SkinPosition(verts[v], bones), color, size);
            }
        }

        if (pickedEyeCenter.HasValue)
            DrawDiamond(pickedEyeCenter.Value, new Color(255, 220, 60), eyeSetupDotSize * 1.6f);
    }
    public void RegenerateEyeTexture()
    {
        if (ModelEditorData.ActiveModel == null || eyeTextureGen == null) return;

        eyeTextureColorPreview?.Dispose();
        eyeTextureDataPreview?.Dispose();

        var result = EyeTextureGenerator.Generate(host.GraphicsDevice, eyeTextureGen, ModelEditorData.ActiveModel.EyeTexture);
        eyeTextureColorPreview = result.Color;
        eyeTextureDataPreview = result.Data;
    }

    public void EyesFaceCamera(bool val)
    {
        if (ModelEditorData.ActiveModel == null || eyeTextureGen == null) return;

        eyeFaceCamera = val;
    }

    public void LoadPreviewModel(string path)
    {
        byte[] data = System.IO.File.ReadAllBytes(path);
        PreviewModel = CCMDLWriter.LoadFromCCMDL(data, host.GraphicsDevice);
        PreviewAttachmentIndex = -1;

        // Try to put it in its bindpose
        var bindpose = PreviewModel.Animations?.Find(a => a.Name.ToLower() == "bindpose");
        if (bindpose != null)
        {
            var player = new Relic.Utils.Animation.CAnimationPlayer(PreviewModel);
            player.AnimDef = bindpose;
            player.IsPlaying = false;
            player.Update(0f);
            previewBoneTransforms = player.BoneSpaceTransforms.ToArray();
            PreviewModel.BoneTransforms = previewBoneTransforms;
        }
        else
        {
            previewBoneTransforms = PreviewModel.BoneTransforms;
        }
    }
    public void ClearPreviewModel() => PreviewModel = null;


    protected void PrepLightParams()
    {
        var vals = realtimeLights.GetValues();
        shaderRealtimeLightCount = int.Min(vals.Length, 16);

        shaderRealtimeLightPositions = new Vector4[shaderRealtimeLightCount];
        shaderRealtimeLightColors = new Vector4[shaderRealtimeLightCount];
        shaderRealtimeLightSpotData = new Vector4[shaderRealtimeLightCount];

        for (int i = 0; i < shaderRealtimeLightCount; i++)
        {
            shaderRealtimeLightPositions[i] = new Vector4(vals[i].Position, vals[i].Range);
            shaderRealtimeLightColors[i] = new Vector4(vals[i].Color.ToVector3(), vals[i].Intensity);
            shaderRealtimeLightSpotData[i] = new Vector4(vals[i].Rotation, MathHelper.ToRadians(vals[i].Angle));
        }
    }
    protected void PrepEyeShaderParams(Relic.Models.CModel model, Matrix[] modelTransforms)
    {
        if (model == null || modelTransforms == null) return;

        for (int i = 0; i < MaxShaderEyes; i++)
        {
            if (model.EyeDefs != null && i < model.EyeDefs.Count)
            {
                var eyeDef = model.EyeDefs[i];
                Matrix eyeWorld = eyeDef.GetTransform(modelTransforms);
                shaderEyeCenterRadius[i] = new Vector4(eyeWorld.Translation, eyeDef.EyeRadius);
                shaderEyeForward[i] = eyeWorld.Forward;
                shaderEyeRight[i] = eyeWorld.Right;
                shaderEyeUp[i] = eyeWorld.Up;
                shaderEyeThetaFOV[i] = eyeDef.ThetaFOV;
            }
            else
            {
                shaderEyeCenterRadius[i] = Vector4.Zero;
                shaderEyeForward[i] = Vector3.Forward;
                shaderEyeRight[i] = Vector3.Right;
                shaderEyeUp[i] = Vector3.Up;
            }
        }
        skinnedEye.Parameters["MainTex"]?.SetValue(eyeTextureColorPreview);
        skinnedEye.Parameters["DataTex"]?.SetValue(eyeTextureDataPreview);
        skinnedEye.Parameters["EyeCenterRadius"]?.SetValue(shaderEyeCenterRadius);
        skinnedEye.Parameters["EyeForward"]?.SetValue(shaderEyeForward);
        skinnedEye.Parameters["EyeRight"]?.SetValue(shaderEyeRight);
        skinnedEye.Parameters["EyeUp"]?.SetValue(shaderEyeUp);
        skinnedEye.Parameters["shine"]?.SetValue(25);

        skinnedEye.Parameters["ThetaFOV"]?.SetValue(shaderEyeThetaFOV);
        skinnedEye.Parameters["IrisSize"]?.SetValue(model.EyeTexture.IrisSize);
    }
    protected void PrepModelShaders(int material, Matrix worldOffset)
    {
        var shaders = new Effect[] { modelDisplay, skinnedModelDisplay, skinnedEye };

        foreach (var shader in shaders)
        {
            shader.Parameters["DiffuseLightDirection"]?.SetValue(Vector3.Normalize(sunDirection));
            shader.Parameters["DiffuseColor"]?.SetValue(sunColor.ToVector3());

            var world = worldOffset * orbitCamera.worldMatrix;

            shader.Parameters["World"].SetValue(world);
            shader.Parameters["View"].SetValue(orbitCamera.viewMatrix);
            shader.Parameters["Projection"].SetValue(orbitCamera.projectionMatrix);

            if (shader != skinnedEye)
            {
                shader.Parameters["MainTex"]?.SetValue(GlobalMapData.LoadedMaterials[material].Texture);
                shader.Parameters["SpecTex"]?.SetValue(GlobalMapData.LoadedMaterials[material].Specular);
                shader.Parameters["NormalTex"]?.SetValue(GlobalMapData.LoadedMaterials[material].Normal);
                shader.Parameters["shine"]?.SetValue(GlobalMapData.LoadedMaterials[material].Reflectivity);
            }

            if (shaderRealtimeLightPositions != null)
            {
                shader.Parameters["realtimeLightPositions"]?.SetValue(shaderRealtimeLightPositions);
                shader.Parameters["realtimeLightColors"]?.SetValue(shaderRealtimeLightColors);
                shader.Parameters["realtimeLightSpotData"]?.SetValue(shaderRealtimeLightSpotData);
                shader.Parameters["realtimeLightCount"]?.SetValue(shaderRealtimeLightCount);
            }

            shader.Parameters["cameraPos"]?.SetValue(orbitCamera.position);
            shader.Parameters["cameraForward"]?.SetValue(Matrix.CreateFromQuaternion(orbitCamera.rotation).Forward);
            shader.Parameters["WorldInverseTranspose"]?.SetValue(Matrix.Invert(Matrix.Transpose(world)));
            shader.Parameters["DiffuseIntensity"]?.SetValue(sunStrength);

            shader.Parameters["AmbientColor"]?.SetValue(new Vector4(ambientColor.ToVector3() * 2, 1f));
            shader.Parameters["AmbientIntensity"]?.SetValue(ambientStrength);

            shader.CurrentTechnique = shader.Techniques["High"];
        }
    }
    public void Draw(GameTime gameTime)
    {
        if (!MainWindow.Instance.modelEditor.IsVisible) return;

        host.GraphicsDevice.Viewport = new Viewport(0, 0, targWidth, targHeight);
        host.GraphicsDevice.Clear(new Color(15, 15, 15));

        host.GraphicsDevice.DepthStencilState = DepthStencilState.Default;

        grid.Draw(orbitCamera.viewMatrix, orbitCamera.projectionMatrix, Vector3.Zero, 0.5f, true, -ModelEditorData.PreviewRootMotionAccum);

        host.GraphicsDevice.BlendState = BlendState.NonPremultiplied;

        if (ModelEditorData.ActiveModel != null && ModelEditorData.ActiveModel.Bodygroups != null)
        {
            Matrix[] modelTransforms = ModelEditorData.ActiveModel.BoneTransforms;
            if (ModelEditorData.PreviewAnimator != null)
            {
                ModelEditorData.ActiveModel.BoneTransforms = ModelEditorData.PreviewAnimator.GetFinalTransforms();
            }
            else if (ModelEditorData.SequencePlayer != null && ModelEditorData.ActiveModel.Animations?.Count > 0)
            {
                ModelEditorData.ActiveModel.BoneTransforms = ModelEditorData.SequencePlayer.BoneSpaceTransforms.ToArray();
                modelTransforms = ModelEditorData.SequencePlayer.ModelSpaceTransforms.ToArray();
            }
            else
            {
                ModelEditorData.ActiveModel.BoneTransforms = new Matrix[ModelEditorData.ActiveModel.Bones.Count];
                Array.Fill(ModelEditorData.ActiveModel.BoneTransforms, Matrix.Identity);
            }

            PrepEyeShaderParams(ModelEditorData.ActiveModel, modelTransforms);

            foreach (var bodygroup in ModelEditorData.ActiveModel.Bodygroups)
            {
                var shader = bodygroup.IsSkinned ? bodygroup.IsEye? skinnedEye : skinnedModelDisplay : modelDisplay;

                shader.Parameters["Bones"]?.SetValue(ModelEditorData.ActiveModel.BoneTransforms);

                PrepModelShaders(bodygroup.MaterialID, Matrix.Identity);

                foreach (var pass in shader.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    if (bodygroup.MorphApplicator != null)
                        bodygroup.MorphApplicator.Draw(host.GraphicsDevice, bodygroup.Mesh.IndexBuffer);
                    else
                        bodygroup.Mesh.Draw();
                }
            }
            if (ModelEditorData.PreviewAnimator == null)
            {
                basicEffect.View = orbitCamera.viewMatrix;
                basicEffect.Projection = orbitCamera.projectionMatrix;
                for (int b = 0; b < ModelEditorData.ActiveModel.Bones.Count; b++)
                {
                    var bone = ModelEditorData.ActiveModel.Bones[b];
                    var boneTransform = modelTransforms == null ? Matrix.Identity : modelTransforms[b];

                    var bbMatrix = Matrix.CreateScale(bone.BoundsSize) * Matrix.CreateTranslation(bone.BoundsCenter);

                    basicEffect.World = bbMatrix * orbitCamera.worldMatrix * boneTransform;
                    basicEffect.Alpha = 0.75f;
                    basicEffect.DiffuseColor = b == ModelEditorData.ViewingBone ? new Vector3(0.3f, 1f, 0.3f) : Vector3.One * 0.8f;

                    foreach (var pass in basicEffect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        host.GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, boxVerts, 0, boxVerts.Length / 2);
                    }
                }
            }

            // Preview model parented to selected attachment
            if (PreviewModel != null && ModelEditorData.ViewingAttachment >= 0 && modelTransforms != null)
            {
                var att = ModelEditorData.ActiveModel.AttachmentPoints[ModelEditorData.ViewingAttachment];
                var attachWorld = att.GetTransform(modelTransforms);

                // If a preview attachment is selected, offset so that attachment
                // lands at the main model's attachment rather than the preview's origin
                Matrix previewWorld;
                if (PreviewAttachmentIndex >= 0
                    && PreviewAttachmentIndex < PreviewModel.AttachmentPoints.Count
                    && PreviewModel.BoneTransforms != null)
                {
                    var previewAtt = PreviewModel.AttachmentPoints[PreviewAttachmentIndex];
                    var previewAttLocal = previewAtt.GetTransform(PreviewModel.ModelTransforms ?? previewBoneTransforms ?? new Matrix[PreviewModel.Bones.Count]);

                    previewAttLocal.Decompose(out _, out Quaternion previewAttRot, out Vector3 previewAttPos);
                    var previewAttRigid = Matrix.CreateFromQuaternion(previewAttRot) * Matrix.CreateTranslation(previewAttPos);

                    previewWorld = Matrix.Invert(previewAttRigid) * attachWorld;
                }
                else
                {
                    previewWorld = attachWorld;
                }

                host.GraphicsDevice.DepthStencilState = DepthStencilState.Default;

                foreach (var bodygroup in PreviewModel.Bodygroups)
                {
                    var shader = bodygroup.IsSkinned ? bodygroup.IsEye ? skinnedEye : skinnedModelDisplay : modelDisplay;

                    shader.Parameters["Bones"]?.SetValue(PreviewModel.BoneTransforms);
                    PrepModelShaders(bodygroup.MaterialID, previewWorld);
                    foreach (var pass in shader.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        bodygroup.Mesh.Draw();
                    }
                }

                host.GraphicsDevice.DepthStencilState = DepthStencilState.None;
            }

            if (modelTransforms != null)
            {
                host.GraphicsDevice.DepthStencilState = DepthStencilState.None;
                basicEffect.VertexColorEnabled = false;
                basicEffect.LightingEnabled = false;
                basicEffect.TextureEnabled = false;
                basicEffect.View = orbitCamera.viewMatrix;
                basicEffect.Projection = orbitCamera.projectionMatrix;

                for (int ai = 0; ai < ModelEditorData.ActiveModel.AttachmentPoints.Count; ai++)
                {
                    var att = ModelEditorData.ActiveModel.AttachmentPoints[ai];
                    var attachWorld = att.GetTransform(modelTransforms);
                    DrawAttachmentGizmo(attachWorld, ai == ModelEditorData.ViewingAttachment);
                }
                DrawEyeGizmos(modelTransforms);

                if (ModelEditorData.PreviewAnimator != null)
                {
                    DrawIKSystemGizmos();
                    DrawIKChainSkeletons();
                    DrawFootOrientationGizmos();
                }

                if (eyeFaceCamera)
                {
                    foreach (var eye in ModelEditorData.ActiveModel.EyeDefs)
                    {
                        Matrix headBone = modelTransforms[eye.HeadBoneID];
                        Matrix restWorld = eye.HeadOffsetMatrix * headBone;

                        Vector3 dirToCam = orbitCamera.position - restWorld.Translation;
                        if (dirToCam.LengthSquared() < 0.0001f) continue;
                        dirToCam.Normalize();

                        Matrix invHeadBone = Matrix.Invert(headBone);
                        Vector3 localDir = Vector3.Normalize(Vector3.TransformNormal(dirToCam, invHeadBone));
                        Vector3 localUp = Vector3.Normalize(Vector3.TransformNormal(Vector3.Up, invHeadBone));

                        if (MathF.Abs(Vector3.Dot(localDir, localUp)) > 0.999f)
                            localUp = Vector3.Normalize(Vector3.TransformNormal(Vector3.Right, invHeadBone));

                        eye.EyeMatrix = Matrix.CreateWorld(Vector3.Zero, localDir, localUp);
                    }
                }
            }
            if (EyeSetupActive)
            {
                DrawEyeSetupOverlay();
                DrawEyeSetupVertexDots();
            }

            //if (eyeTextureColorPreview != null)
            //{
            //    spriteBatch.Begin();
            //    spriteBatch.Draw(eyeTextureColorPreview, new Rectangle(10, 10, 128, 128), Color.White);
            //    if (eyeTextureDataPreview != null)
            //        spriteBatch.Draw(eyeTextureDataPreview, new Rectangle(148, 10, 128, 128), Color.White);
            //    spriteBatch.End();
            //}

            if (ModelEditorData.ViewingBone >= 0 && modelTransforms != null)
            {
                int bi = ModelEditorData.ViewingBone;
                var bone = ModelEditorData.ActiveModel.Bones[bi];

                var boneWorld = modelTransforms[bi];
                var parentWorld = bone.HasParent ? modelTransforms[bone.Parent.Index] : Matrix.Identity;

                GetJointAxes(boneWorld, parentWorld,
                             out Vector3 jOrigin, out Vector3 twistAxis,
                             out Vector3 planeAxis, out Vector3 normalAxis);

                host.GraphicsDevice.DepthStencilState = DepthStencilState.None;
                basicEffect.VertexColorEnabled = false;
                basicEffect.LightingEnabled = false;
                basicEffect.TextureEnabled = false;

                DrawEllipticalCone(jOrigin, twistAxis, normalAxis, bone.JointNormalHalfCone,
                                        planeAxis, bone.JointPlaneHalfCone);

                DrawTwistGizmo(jOrigin, twistAxis, planeAxis, normalAxis,
                               bone.JointTwistMin, bone.JointTwistMax);

                float r = HandleLength;
                float rt = HandleLength * 0.75f;

                hNormalTip = WorldToScreen(
                    jOrigin + twistAxis * (MathF.Cos(bone.JointNormalHalfCone) * r)
                            + normalAxis * (MathF.Sin(bone.JointNormalHalfCone) * r));

                hPlaneTip = WorldToScreen(
                    jOrigin + twistAxis * (MathF.Cos(bone.JointPlaneHalfCone) * r)
                            + planeAxis * (MathF.Sin(bone.JointPlaneHalfCone) * r));

                hTwistMinTip = WorldToScreen(
                    jOrigin + (planeAxis * MathF.Cos(bone.JointTwistMin)
                             + normalAxis * MathF.Sin(bone.JointTwistMin)) * rt);
                hTwistMaxTip = WorldToScreen(
                    jOrigin + (planeAxis * MathF.Cos(bone.JointTwistMax)
                             + normalAxis * MathF.Sin(bone.JointTwistMax)) * rt);
            }
        }
    }

    private Vector2 WorldToScreen(Vector3 world)
    {
        var vp = host.GraphicsDevice.Viewport;
        var projected = vp.Project(world, orbitCamera.projectionMatrix, orbitCamera.viewMatrix, orbitCamera.worldMatrix);
        return new Vector2(projected.X, projected.Y);
    }
    private static void GetJointAxes(Matrix boneWorld, Matrix parentWorld,
                                 out Vector3 origin, out Vector3 twistAxis,
                                 out Vector3 planeAxis, out Vector3 normalAxis)
    {
        origin = boneWorld.Translation;

        var raw = origin - parentWorld.Translation;
        twistAxis = raw.LengthSquared() > 0.0001f
            ? Vector3.Normalize(raw)
            : boneWorld.Forward;

        // Project world Forward perpendicular to the twist axis.
        // This makes twist=0 correspond to the bone facing world forward,
        // which is the natural "front of character" reference.
        var fwd = Vector3.Forward;
        planeAxis = fwd - twistAxis * Vector3.Dot(fwd, twistAxis);
        if (planeAxis.LengthSquared() < 0.001f) // twistAxis is nearly parallel to Forward
            planeAxis = Vector3.Up - twistAxis * Vector3.Dot(Vector3.Up, twistAxis);
        planeAxis = Vector3.Normalize(planeAxis);

        normalAxis = Vector3.Normalize(Vector3.Cross(twistAxis, planeAxis));
    }

    private void DrawLines(VertexPosition[] verts, int primitiveCount, Color color, float alpha = 1f)
    {
        if (primitiveCount == 0) return;

        basicEffect.World = orbitCamera.worldMatrix;
        basicEffect.DiffuseColor = color.ToVector3();
        basicEffect.Alpha = alpha;
        foreach (var pass in basicEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            host.GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, verts, 0, primitiveCount);
        }
    }
    private void DrawEllipticalCone(Vector3 origin, Vector3 twistAxis,
                                 Vector3 normalAxis, float normalHalf,
                                 Vector3 planeAxis, float planeHalf,
                                 int segments = 48)
    {
        float r = HandleLength;

        Vector3 RimPt(float t) =>
            origin
            + twistAxis * r * MathF.Cos(MathF.Max(normalHalf, planeHalf))
            + normalAxis * r * MathF.Sin(normalHalf) * MathF.Cos(t)
            + planeAxis * r * MathF.Sin(planeHalf) * MathF.Sin(t);

        var tris = new VertexPosition[segments * 3];
        for (int i = 0; i < segments; i++)
        {
            float t0 = MathHelper.TwoPi * i / segments;
            float t1 = MathHelper.TwoPi * (i + 1) / segments;
            tris[i * 3 + 0] = new VertexPosition(origin);
            tris[i * 3 + 1] = new VertexPosition(RimPt(t0));
            tris[i * 3 + 2] = new VertexPosition(RimPt(t1));
        }
        basicEffect.World = orbitCamera.worldMatrix;
        basicEffect.DiffuseColor = Color.White.ToVector3();
        basicEffect.Alpha = 0.15f;
        host.GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        foreach (var pass in basicEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            host.GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, tris, 0, segments);
        }

        var rim = new VertexPosition[segments * 2];
        for (int i = 0; i < segments; i++)
        {
            rim[i * 2] = new VertexPosition(RimPt(MathHelper.TwoPi * i / segments));
            rim[i * 2 + 1] = new VertexPosition(RimPt(MathHelper.TwoPi * (i + 1) / segments));
        }
        DrawLines(rim, segments, Color.White, 0.9f);

        Vector3 normalTip = origin + twistAxis * (MathF.Cos(normalHalf) * r) + normalAxis * (MathF.Sin(normalHalf) * r);
        Vector3 planeTip = origin + twistAxis * (MathF.Cos(planeHalf) * r) + planeAxis * (MathF.Sin(planeHalf) * r);

        DrawLines(new[] { new VertexPosition(origin), new VertexPosition(normalTip) }, 1, ColNormal, 1f);
        DrawLines(new[] { new VertexPosition(origin), new VertexPosition(planeTip) }, 1, ColPlane, 1f);
        DrawDiamond(normalTip, ColNormal);
        DrawDiamond(planeTip, ColPlane);

        DrawLines(new[] { new VertexPosition(RimPt(0)), new VertexPosition(RimPt(MathHelper.Pi)) }, 1, ColNormal, 0.4f);
        DrawLines(new[] { new VertexPosition(RimPt(MathHelper.PiOver2)), new VertexPosition(RimPt(MathHelper.Pi + MathHelper.PiOver2)) }, 1, ColPlane, 0.4f);
    }
    private void DrawTwistGizmo(Vector3 origin, Vector3 twistAxis,
                             Vector3 planeAxis, Vector3 normalAxis,
                             float twistMin, float twistMax,
                             int segments = 48)
    {
        float r = HandleLength * 0.75f;

        var axisLine = new[]
        {
            new VertexPosition(origin - twistAxis * r * 0.4f),
            new VertexPosition(origin + twistAxis * r * 1.3f),
        };
        DrawLines(axisLine, 1, ColAxis, 0.8f);

        int circSeg = 48;
        var circle = new VertexPosition[circSeg * 2];
        for (int i = 0; i < circSeg; i++)
        {
            float t0 = MathHelper.TwoPi * i / circSeg;
            float t1 = MathHelper.TwoPi * (i + 1) / circSeg;
            Vector3 P(float a) => origin + (planeAxis * MathF.Cos(a) + normalAxis * MathF.Sin(a)) * r;
            circle[i * 2 + 0] = new VertexPosition(P(t0));
            circle[i * 2 + 1] = new VertexPosition(P(t1));
        }
        DrawLines(circle, circSeg, ColTwist, 0.2f);

        int fanSegs = Math.Max(2, (int)(segments * Math.Abs(twistMax - twistMin) / MathHelper.TwoPi));
        var wedge = new VertexPosition[fanSegs * 3];
        for (int i = 0; i < fanSegs; i++)
        {
            float a0 = MathHelper.Lerp(twistMin, twistMax, (float)i / fanSegs);
            float a1 = MathHelper.Lerp(twistMin, twistMax, (float)(i + 1) / fanSegs);
            Vector3 P(float a) => origin + (planeAxis * MathF.Cos(a) + normalAxis * MathF.Sin(a)) * r;
            wedge[i * 3 + 0] = new VertexPosition(origin);
            wedge[i * 3 + 1] = new VertexPosition(P(a0));
            wedge[i * 3 + 2] = new VertexPosition(P(a1));
        }
        basicEffect.World = orbitCamera.worldMatrix;
        basicEffect.DiffuseColor = ColTwist.ToVector3();
        basicEffect.Alpha = 0.35f;
        host.GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        foreach (var pass in basicEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            host.GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, wedge, 0, fanSegs);
        }

        var arc = new VertexPosition[segments * 2];
        for (int i = 0; i < segments; i++)
        {
            float t0 = MathHelper.Lerp(twistMin, twistMax, (float)i / segments);
            float t1 = MathHelper.Lerp(twistMin, twistMax, (float)(i + 1) / segments);
            Vector3 P(float a) => origin + (planeAxis * MathF.Cos(a) + normalAxis * MathF.Sin(a)) * r;
            arc[i * 2 + 0] = new VertexPosition(P(t0));
            arc[i * 2 + 1] = new VertexPosition(P(t1));
        }
        DrawLines(arc, segments, ColTwist, 1f);

        Vector3 TwistTip(float a) => origin + (planeAxis * MathF.Cos(a) + normalAxis * MathF.Sin(a)) * r;

        Vector3 minTip = TwistTip(twistMin);
        Vector3 maxTip = TwistTip(twistMax);
        DrawLines(new[] { new VertexPosition(origin), new VertexPosition(minTip) }, 1, ColTwistMin, 1f);
        DrawLines(new[] { new VertexPosition(origin), new VertexPosition(maxTip) }, 1, ColTwistMax, 1f);
        DrawDiamond(minTip, ColTwistMin);
        DrawDiamond(maxTip, ColTwistMax);

        Vector3 arrowTip = origin + twistAxis * r * 1.3f;
        Vector3 arrowBase = arrowTip - twistAxis * r * 0.15f;
        var cam = orbitCamera.viewMatrix;
        var right = new Vector3(cam.M11, cam.M21, cam.M31) * r * 0.05f;
        DrawLines(new[] { new VertexPosition(origin - twistAxis * r * 0.4f), new VertexPosition(arrowTip) }, 1, ColAxis, 0.8f);
        DrawLines(new[] { new VertexPosition(arrowTip), new VertexPosition(arrowBase + right) }, 1, ColAxis, 0.8f);
        DrawLines(new[] { new VertexPosition(arrowTip), new VertexPosition(arrowBase - right) }, 1, ColAxis, 0.8f);

        Vector3 zeroTip = origin + planeAxis * r;
        DrawLines(new[]
        {
            new VertexPosition(origin),
            new VertexPosition(zeroTip)
        }, 1, new Color(255, 255, 80), 0.9f);

        var viewRight = new Vector3(cam.M11, cam.M21, cam.M31) * r * 0.08f;
        DrawLines(new[]
        {
            new VertexPosition(zeroTip - viewRight),
            new VertexPosition(zeroTip + viewRight)
        }, 1, new Color(255, 255, 80), 0.9f);
    }
    private void DrawDiamond(Vector3 center, Color color, float size = 0.012f)
    {
        var cam = orbitCamera.viewMatrix;
        var right = new Vector3(cam.M11, cam.M21, cam.M31) * size;
        var up = new Vector3(cam.M12, cam.M22, cam.M32) * size;

        Vector3 t = center + up;
        Vector3 b = center - up;
        Vector3 l = center - right;
        Vector3 rr = center + right;

        var tris = new[]
        {
        new VertexPosition(t),  new VertexPosition(rr), new VertexPosition(b),
        new VertexPosition(b),  new VertexPosition(l),  new VertexPosition(t),
    };
        basicEffect.World = orbitCamera.worldMatrix;
        basicEffect.DiffuseColor = color.ToVector3();
        basicEffect.Alpha = 1f;
        host.GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        foreach (var pass in basicEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            host.GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, tris, 0, 2);
        }
    }
    private void DrawAttachmentGizmo(Matrix attachWorld, bool isSelected)
    {
        var pos = attachWorld.Translation;
        float len = AttachHandleLength;
        float lineAlpha = isSelected ? 1f : 0.45f;
        float scale = isSelected ? 1f : 0.6f;

        Vector3 xAxis = attachWorld.Right;
        Vector3 yAxis = attachWorld.Up;
        Vector3 zAxis = attachWorld.Backward;

        Color xColor = (hoveredAttachHandle == AttachHandleType.X || draggingAttachHandle == AttachHandleType.X) ? Color.Lerp(ColAttachX, Color.White, 0.6f) : ColAttachX;
        Color yColor = (hoveredAttachHandle == AttachHandleType.Y || draggingAttachHandle == AttachHandleType.Y) ? Color.Lerp(ColAttachY, Color.White, 0.6f) : ColAttachY;
        Color zColor = (hoveredAttachHandle == AttachHandleType.Z || draggingAttachHandle == AttachHandleType.Z) ? Color.Lerp(ColAttachZ, Color.White, 0.6f) : ColAttachZ;

        DrawLines(new[] { new VertexPosition(pos), new VertexPosition(pos + xAxis * len * scale) }, 1, xColor, lineAlpha);
        DrawLines(new[] { new VertexPosition(pos), new VertexPosition(pos + yAxis * len * scale) }, 1, yColor, lineAlpha);
        DrawLines(new[] { new VertexPosition(pos), new VertexPosition(pos + zAxis * len * scale) }, 1, zColor, lineAlpha);

        if (isSelected)
        {
            DrawDiamond(pos + xAxis * len, xColor);
            DrawDiamond(pos + yAxis * len, yColor);
            DrawDiamond(pos + zAxis * len, zColor);

            hAttachX = WorldToScreen(pos + xAxis * len);
            hAttachY = WorldToScreen(pos + yAxis * len);
            hAttachZ = WorldToScreen(pos + zAxis * len);

            host.GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            basicEffect.World = Matrix.CreateWorld(pos, Vector3.Forward, Vector3.Up) * orbitCamera.worldMatrix;
            basicEffect.VertexColorEnabled = true;
            basicEffect.Alpha = 1f;

            Vector3 dim = Vector3.One * 0.25f;
            DrawRing(attachRingVertsX, attachRingIndicesX, (hoveredAttachRotateHandle == AttachRotateHandleType.X || draggingAttachRotateHandle == AttachRotateHandleType.X) ? dim : Vector3.One);
            DrawRing(attachRingVertsY, attachRingIndicesY, (hoveredAttachRotateHandle == AttachRotateHandleType.Y || draggingAttachRotateHandle == AttachRotateHandleType.Y) ? dim : Vector3.One);
            DrawRing(attachRingVertsZ, attachRingIndicesZ, (hoveredAttachRotateHandle == AttachRotateHandleType.Z || draggingAttachRotateHandle == AttachRotateHandleType.Z) ? dim : Vector3.One);

            basicEffect.VertexColorEnabled = false;
        }
    }

    private (Vector2 dir, float pixelsPerUnit) ProjectAxisToScreen(Vector3 worldPos, Vector3 axis)
    {
        Vector2 basePt = WorldToScreen(worldPos);
        Vector2 tipPt = WorldToScreen(worldPos + axis * 0.1f);
        Vector2 delta = tipPt - basePt;
        float len = delta.Length();
        return len > 0.0001f ? (delta / len, len / 0.1f) : (Vector2.UnitX, 0f);
    }
    private void UpdateAttachHover()
    {
        hoveredAttachHandle = AttachHandleType.None;
        hoveredAttachRotateHandle = AttachRotateHandleType.None;

        if (ModelEditorData.ViewingAttachment < 0 || ModelEditorData.ActiveModel?.AttachmentPoints == null) return;
        if (draggingAttachHandle != AttachHandleType.None) { hoveredAttachHandle = draggingAttachHandle; return; }
        if (draggingAttachRotateHandle != AttachRotateHandleType.None) { hoveredAttachRotateHandle = draggingAttachRotateHandle; return; }

        var mousePos = MouseManager.GetXnaPositionRelativeTo(MainWindow.Instance.modelEditor.gameControl).ToVector2();

        if (Vector2.Distance(mousePos, hAttachX) <= AttachHandleHitRadius) { hoveredAttachHandle = AttachHandleType.X; return; }
        if (Vector2.Distance(mousePos, hAttachY) <= AttachHandleHitRadius) { hoveredAttachHandle = AttachHandleType.Y; return; }
        if (Vector2.Distance(mousePos, hAttachZ) <= AttachHandleHitRadius) { hoveredAttachHandle = AttachHandleType.Z; return; }

        var att = ModelEditorData.ActiveModel.AttachmentPoints[ModelEditorData.ViewingAttachment];
        var modelTransforms = ModelEditorData.SequencePlayer != null
            ? ModelEditorData.SequencePlayer.ModelSpaceTransforms.ToArray()
            : ModelEditorData.ActiveModel.BoneTransforms;
        var attachPos = att.GetTransform(modelTransforms).Translation;

        var ray = CalculateRay(mousePos);
        AttachRotateHandleType best = AttachRotateHandleType.None;
        float bestT = float.MaxValue;

        void TestRing(Vector3 axis, AttachRotateHandleType type)
        {
            var plane = new Plane(axis, -Vector3.Dot(axis, attachPos));
            float? t = ray.Intersects(plane);
            if (!t.HasValue) return;
            Vector3 hit = ray.Position + ray.Direction * t.Value;
            float dist = Vector3.Distance(hit, attachPos);
            if (MathF.Abs(dist - AttachRingRadius) < AttachRingPickTolerance && t.Value < bestT)
            {
                bestT = t.Value;
                best = type;
            }
        }
        TestRing(Vector3.UnitX, AttachRotateHandleType.X);
        TestRing(Vector3.UnitY, AttachRotateHandleType.Y);
        TestRing(Vector3.UnitZ, AttachRotateHandleType.Z);

        hoveredAttachRotateHandle = best;
    }
    private Ray CalculateRay(Vector2 mouseLocation)
    {
        Vector3 nearPoint = host.GraphicsDevice.Viewport.Unproject(new Vector3(mouseLocation.X, mouseLocation.Y, 0f), orbitCamera.projectionMatrix, orbitCamera.viewMatrix, orbitCamera.worldMatrix);
        Vector3 farPoint = host.GraphicsDevice.Viewport.Unproject(new Vector3(mouseLocation.X, mouseLocation.Y, 1f), orbitCamera.projectionMatrix, orbitCamera.viewMatrix, orbitCamera.worldMatrix);
        return new Ray(nearPoint, Vector3.Normalize(farPoint - nearPoint));
    }

    private float MeasureAttachRotationAngle(Vector3 axis, Vector3 pivot, Vector2 mouseScreen)
    {
        var ray = CalculateRay(mouseScreen);
        var plane = new Plane(axis, -Vector3.Dot(axis, pivot));
        float? t = ray.Intersects(plane);
        if (!t.HasValue) return previousRotateAngle;

        Vector3 local = (ray.Position + ray.Direction * t.Value) - pivot;
        Vector3 u = Vector3.Cross(axis, Vector3.Up);
        if (u.LengthSquared() < 0.01f) u = Vector3.Cross(axis, Vector3.Right);
        u.Normalize();
        Vector3 v = Vector3.Cross(axis, u);
        return MathF.Atan2(Vector3.Dot(local, v), Vector3.Dot(local, u));
    }

    private void DrawRing(List<VertexPositionColor> verts, List<int> indices, Vector3 colorMultiplier)
    {
        basicEffect.DiffuseColor = colorMultiplier;
        foreach (var pass in basicEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            host.GraphicsDevice.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, verts.ToArray(), 0, verts.Count, indices.ToArray(), 0, indices.Count / 3);
        }
    }
}
