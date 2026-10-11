using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using Rockwall2.Editor.Common.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Rockwall2.Editor.Common;

/// <summary>
/// Live 3D preview of the material currently open in the Material Editor.
///
/// Follows the same pattern as ModelView/ParticleView: an IEditorScene rendered into the
/// MonoGameControl's private RenderTarget2D. Device state is global and shared with the map
/// and model editors, so Draw sets Viewport/Rasterizer/Blend/Depth/Sampler explicitly rather
/// than assuming whatever the last scene left behind.
/// </summary>
public sealed class MaterialPreviewView : IEditorScene
{
    public enum PreviewShape { Sphere, Cube, Plane }

    EditorHost host;
    BasicEffect effect;
    Control inputSurface;

    readonly Orbit3DCamera camera = new();

    VertexBuffer vertexBuffer;
    IndexBuffer indexBuffer;
    int indexCount;

    Texture2D materialTexture;
    Texture2D fallbackTexture;
    string loadedTexturePath;
    string previewOverridePath;

    // PBR path. Falls back to BasicEffect when the compiled shader isn't available,
    // so a missing .xnb degrades to the old flat preview instead of failing to load.
    Effect pbrEffect;
    TextureCube environmentMap;
    bool pbrReady;

    public bool UsePbr
    {
        get => pbrReady;
        set
        {
            usePbr = value;
        }
    }

    bool usePbr = true;

    float currentReflectivity = 1f;
    float currentShine = 0.5f;

    /// <summary>Feeds the material's fields into the preview shader.</summary>
    public void SetSurface(float reflectivity, float shine)
    {
        currentReflectivity = MathHelper.Clamp(reflectivity, 0f, 4f);
        currentShine = MathHelper.Clamp(shine, 0f, 4f);
    }

    PreviewShape shape = PreviewShape.Sphere;
    Vector3 lightDirection = Vector3.Normalize(new Vector3(-0.4f, -0.7f, -0.6f));
    float lightYaw = -35f;
    float lightPitch = -45f;

    bool attached;
    int width = 1, height = 1;

    /// <summary>Control used for hit-testing mouse input, so orbiting only happens over this panel.</summary>
    public void SetInputSurface(Control control)
    {
        if (inputSurface == null && control != null)
        {
            control.PointerPressed += OnPointerPressed;
            control.PointerMoved += OnPointerMoved;
            control.PointerReleased += OnPointerReleased;
            control.PointerEntered += OnPointerEnter;

            // Control exposes no CLR PointerWheel/PointerLeft wrappers in this Avalonia
            // version, so go through the routed events directly.
            control.AddHandler(Control.PointerWheelChangedEvent, OnPointerWheel);
            control.AddHandler(Control.PointerExitedEvent, OnPointerLeave);
        }

        inputSurface = control;
    }

    public PreviewShape Shape
    {
        get => shape;
        set
        {
            shape = value;
            RebuildMesh();
        }
    }

    public void ResetLight()
    {
        lightYaw = -35f;
        lightPitch = -45f;
        lightDirection = DirectionFromAngles();
        RebuildGizmo();
    }

    /// <summary>Points the preview at a material. The texture is (re)loaded lazily on the next draw.</summary>
    public void SetMaterial(Material material)
    {
        string path = ContentAssetStore.FindTextureFile(material.TextureName);

        if (path == loadedTexturePath) return;

        loadedTexturePath = path;
        SwapTexture(path);
    }

    /// <summary>
    /// Re-reads the texture from disk. Needed after the Image tab writes it, because the
    /// path hasn't changed and SetMaterial would otherwise keep serving the stale GPU copy.
    /// </summary>
    public void ReloadTexture()
    {
        if (previewOverridePath == null)
        {
            SwapTexture(loadedTexturePath);
            return;
        }

        TryDeletePreviewOverride();
        SwapTexture(loadedTexturePath);
    }

    /// <summary>
    /// Shows an unsaved working image in the preview. The override wins over the on-disk
    /// texture until <see cref="ClearPreviewOverride"/> is called, so live edits are visible
    /// without writing over the real texture.
    /// </summary>
    public void SetPreviewImage(string tempPngPath)
    {
        if (previewOverridePath != tempPngPath) TryDeletePreviewOverride();

        previewOverridePath = tempPngPath;
        SwapTexture(tempPngPath);
    }

    public void ClearPreviewOverride()
    {
        TryDeletePreviewOverride();
        SwapTexture(loadedTexturePath);
    }

    void TryDeletePreviewOverride()
    {
        if (previewOverridePath == null) return;

        try { File.Delete(previewOverridePath); }
        catch { /* still locked or read-only; harmless */ }

        previewOverridePath = null;
    }

    // ---- IEditorScene ------------------------------------------------------------------

    public void Attach(EditorHost host)
    {
        this.host = host;

        LoadPbrEffect();

        if (pbrReady)
            environmentMap = BuildEnvironmentMap(host.GraphicsDevice);

        effect = new BasicEffect(host.GraphicsDevice)
        {
            TextureEnabled = true,
            LightingEnabled = true,
            // Must stay false: the sphere/cube/plane use VertexPositionNormalTexture, which
            // has no colour channel. Enabling it makes BasicEffect read a colour that isn't
            // in the input layout and everything renders black.
            VertexColorEnabled = false,
            AmbientLightColor = Vector3.One * 0.25f,
            World = Matrix.Identity,
            Projection = camera.projectionMatrix
        };

        effect.DirectionalLight0.Enabled = true;
        effect.DirectionalLight0.DiffuseColor = Vector3.One;
        effect.DirectionalLight0.SpecularColor = Vector3.Zero;
        effect.DirectionalLight0.Direction = lightDirection;

        fallbackTexture = BuildCheckerTexture(host.GraphicsDevice);
        materialTexture = fallbackTexture;

        // SetMaterial can run before the control attaches (the editor selects the first
        // material on load), and SwapTexture is a no-op without a device. Re-apply now.
        if (loadedTexturePath != null) SwapTexture(loadedTexturePath);

        ResetCamera();
        camera.FocusOn(Vector3.Zero, 1.0f, 2.2f);
        camera.AspectRatio = (float)width / height;
        camera.RebuildMatrix();

        RebuildGizmo();

        RebuildMesh();
        attached = true;
    }

    public void Detach()
    {
        attached = false;

        vertexBuffer?.Dispose();
        indexBuffer?.Dispose();
        gizmoBuffer?.Dispose();
        vertexBuffer = null;
        indexBuffer = null;
        gizmoBuffer = null;

        if (materialTexture != null && materialTexture != fallbackTexture)
            materialTexture.Dispose();
        materialTexture = null;

        fallbackTexture?.Dispose();
        fallbackTexture = null;

        environmentMap?.Dispose();
        environmentMap = null;

        testVertexBuffer?.Dispose();
        testIndexBuffer?.Dispose();
        testVertexBuffer = null;
        testIndexBuffer = null;

        pbrEffect?.Dispose();
        pbrEffect = null;

        effect?.Dispose();
        effect = null;

        lineEffect?.Dispose();
        lineEffect = null;
    }

    public void Resize(int w, int h)
    {
        width = Math.Max(1, w);
        height = Math.Max(1, h);
        camera.AspectRatio = (float)width / height;

        // Keep the mesh framed as the panel changes shape, unless the user has zoomed.
        if (!userZoomed) FrameMesh();
    }

    // ---- pointer-driven orbit ------------------------------------------------------------
    //
    // Deliberately not Orbit3DCamera. That class reads the process-global MouseManager,
    // which only gets fed for the focused editor and is warped by CaptureCursor during
    // drags -- so from a second, unfocused panel it either ignores the mouse entirely or
    // drifts when idle. Owning the pointer events outright keeps this panel independent:
    // it reacts only to its own control and cannot perturb the map editor's camera.

    enum DragMode { None, Orbit, Pan, Light }

    DragMode dragMode = DragMode.None;
    Avalonia.Point lastPointerPos;
    bool pointerInside;

    const float MinPitch = -89f, MaxPitch = 89f;
    float yaw = 5f, pitch = -30f;
    float targetDist;
    float currentDist;
    Vector3 panOffset = Vector3.Zero;
    bool userZoomed;

    // ---- light gizmo --------------------------------------------------------------------
    VertexBuffer gizmoBuffer;
    BasicEffect lineEffect;
    readonly List<VertexPositionColor> gizmoVerts = new();
    readonly List<Vector3> gizmoHandlePoints = new();
    VertexPositionColor[] gizmoArray = System.Array.Empty<VertexPositionColor>();
    bool gizmoHovered;
    bool gizmoWasHovered;

    static readonly Color GizmoColor = new(1f, 0.85f, 0.35f);
    static readonly Color GizmoHoverColor = new(1f, 0.98f, 0.75f);
    static readonly Color GizmoRingColor = new(0.55f, 0.45f, 0.20f);

    /// <summary>Distance in pixels within which the pointer counts as grabbing the gizmo.</summary>
    const float GizmoGrabRadius = 14f;

    void RebuildGizmo()
    {
        gizmoVerts.Clear();
        gizmoHandlePoints.Clear();

        Color shaft = gizmoHovered ? GizmoHoverColor : GizmoColor;
        Color ring = gizmoHovered ? GizmoHoverColor : GizmoRingColor;
        RebuildGizmoGeometry(shaft, ring);

        gizmoArray = gizmoVerts.ToArray();

        if (host == null) return;

        if (gizmoBuffer == null || gizmoBuffer.IsDisposed || gizmoBuffer.VertexCount < gizmoArray.Length)
        {
            gizmoBuffer?.Dispose();
            gizmoBuffer = new VertexBuffer(host.GraphicsDevice, typeof(VertexPositionColor),
                Math.Max(gizmoArray.Length, 1), BufferUsage.WriteOnly);
        }

        gizmoBuffer.SetData(gizmoArray);
    }

    void RebuildGizmoGeometry(Color shaft, Color ring)
    {
        var from = -lightDirection * 2.5f;
        var to = -lightDirection * 1.45f;

        gizmoVerts.AddRange(GizmoShapes.WireArrow(from, Vector3.Normalize(to - from), Vector3.Distance(from, to), shaft));

        for (int i = 0; i <= 8; i++)
            gizmoHandlePoints.Add(Vector3.Lerp(from, to, i / 8f));

        GizmoShapes.AddCircle(gizmoVerts, Vector3.Zero, 2.5f, Vector3.Right, Vector3.Forward, ring);

        for (int i = 0; i <= 24; i++)
        {
            float t = i / 24f * MathHelper.TwoPi;
            gizmoHandlePoints.Add(from + (Vector3.Right * MathF.Cos(t) + Vector3.Forward * MathF.Sin(t)) * 2.5f);
        }
    }

    /// <summary>World -> preview pixels. Returns false when the point is behind the camera.</summary>
    bool ProjectToScreen(Vector3 world, out Avalonia.Point screen)
    {
        screen = default;

        var viewProjection = camera.viewMatrix * camera.projectionMatrix;
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);

        if (clip.W <= 0.0001f) return false;

        var ndc = new Vector3(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W);

        screen = new Avalonia.Point(
            (ndc.X * 0.5f + 0.5f) * width,
            (0.5f - ndc.Y * 0.5f) * height);

        return true;
    }

    /// <summary>
    /// Screen-space hit test against the gizmo's sampled geometry. Projecting the handle
    /// points and measuring pixel distance is far more predictable than ray/primitive maths
    /// and stays accurate at any zoom level.
    /// </summary>
    bool IsPointerOnGizmo(Avalonia.Point p)
    {
        const float GrabRadiusSq = GizmoGrabRadius * GizmoGrabRadius;

        foreach (var world in gizmoHandlePoints)
        {
            if (!ProjectToScreen(world, out var screen)) continue;

            float dx = (float)(screen.X - p.X);
            float dy = (float)(screen.Y - p.Y);
            if (dx * dx + dy * dy <= GrabRadiusSq) return true;
        }

        return false;
    }

    void SetGizmoHovered(bool value)
    {
        if (gizmoHovered == value) return;

        gizmoHovered = value;

        if (inputSurface != null)
            inputSurface.Cursor = value ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
    }

    void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (!IsPreviewPointer(e)) return;

        lastPointerPos = e.GetPosition(inputSurface);
        bool rightDown = e.GetCurrentPoint(inputSurface).Properties.IsRightButtonPressed;

        // Left-dragging the light gizmo repositions the light; anywhere else it orbits.
        bool grabbedGizmo = !rightDown && IsPointerOnGizmo(lastPointerPos);

        dragMode = rightDown || grabbedGizmo ? DragMode.Light
                 : e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? DragMode.Pan
                 : DragMode.Orbit;

        // In Avalonia 11 capture is a method on IPointer, not on the control.
        e.Pointer.Capture((IInputElement)inputSurface);
        e.Handled = true;
    }

    void OnPointerMoved(object sender, PointerEventArgs e)
    {
        if (inputSurface == null) return;

        var pos = e.GetPosition(inputSurface);

        if (dragMode == DragMode.None)
        {
            // Track hover even when idle so the gizmo lights up under the cursor.
            SetGizmoHovered(IsPointerOnGizmo(pos));
            lastPointerPos = pos;
            return;
        }

        float dx = (float)(pos.X - lastPointerPos.X);
        float dy = (float)(pos.Y - lastPointerPos.Y);
        lastPointerPos = pos;

        switch (dragMode)
        {
            case DragMode.Orbit:
                yaw -= dx * 0.3f;
                pitch = MathHelper.Clamp(pitch - dy * 0.3f, MinPitch, MaxPitch);
                break;

            case DragMode.Pan:
                // Scale panning with distance so it feels the same at any zoom level.
                var basis = Matrix.CreateFromQuaternion(Rotation);
                panOffset += basis.Up * (dy * currentDist * 0.002f)
                           + basis.Right * (-dx * currentDist * 0.002f);
                break;

            case DragMode.Light:
                lightYaw += dx * 0.3f;
                lightPitch = MathHelper.Clamp(lightPitch + dy * 0.3f, MinPitch, MaxPitch);
                lightDirection = DirectionFromAngles();
                RebuildGizmo();
                break;
        }

        e.Handled = true;
    }

    void OnPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (dragMode == DragMode.None) return;

        dragMode = DragMode.None;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    void OnPointerWheel(object sender, PointerWheelEventArgs e)
    {
        if (!pointerInside || inputSurface == null) return;

        targetDist *= e.Delta.Y < 0 ? 1.12f : 1f / 1.12f;
        targetDist = MathHelper.Clamp(targetDist, 1.4f, 24f);
        userZoomed = true;
        e.Handled = true;
    }

    void OnPointerEnter(object sender, PointerEventArgs e) => pointerInside = true;
    void OnPointerLeave(object sender, PointerEventArgs e)
    {
        pointerInside = false;
        dragMode = DragMode.None;
    }

    /// <summary>True when this event targets the preview rather than an overlay it drew.</summary>
    bool IsPreviewPointer(PointerEventArgs e)
    {
        if (inputSurface == null || !inputSurface.IsEffectivelyVisible) return false;

        var pos = e.GetPosition(inputSurface);
        return pos.X >= 0 && pos.Y >= 0
            && pos.X < inputSurface.Bounds.Width && pos.Y < inputSurface.Bounds.Height;
    }

    public void ResetCamera()
    {
        yaw = 5f;
        pitch = -30f;
        panOffset = Vector3.Zero;
        FrameMesh();
    }

    /// <summary>Mesh radius used for framing, in world units.</summary>
    const float MeshRadius = 1.45f; // a unit sphere's diagonal, so no shape clips when rotated

    /// <summary>
    /// Places the camera at whatever distance actually fits the mesh in the current
    /// viewport. Deriving it beats a hard-coded constant: the preview panel is much taller
    /// than it is wide, so a distance tuned for a square viewport crops badly here.
    /// </summary>
    void FrameMesh()
    {
        float aspect = (float)width / Math.Max(1, height);

        // Vertical half-FOV, and the horizontal one implied by the aspect ratio.
        float halfV = MathHelper.ToRadians(camera.FOV) * 0.5f;
        float halfH = MathF.Atan(MathF.Tan(halfV) * Math.Max(aspect, 0.01f));

        float limiting = MathF.Min(halfV, halfH);
        targetDist = MeshRadius / MathF.Max(MathF.Sin(limiting), 0.05f) * 1.15f;
        targetDist = MathHelper.Clamp(targetDist, 2.5f, 24f);

        // Snap on first frame; ease only on explicit zoom changes.
        if (currentDist <= 0.001f) currentDist = targetDist;
    }

    Quaternion Rotation => Quaternion.CreateFromYawPitchRoll(
        MathHelper.ToRadians(yaw), MathHelper.ToRadians(pitch), 0);

    public void Update(GameTime gameTime)
    {
        if (!attached || host == null) return;

        // Ease the zoom so wheel steps don't snap. Nothing else is time-dependent, which is
        // what keeps the view rock-steady when the pointer is idle.
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        currentDist = OtherMath.MoveTowards(currentDist, targetDist,
            dt * Math.Abs(currentDist - targetDist) * 25 + 0.01f);

        var rotation = Rotation;
        camera.rotation = rotation;
        camera.posOffset = panOffset;
        camera.targetPosOffset = panOffset;
        camera.position = Matrix.CreateFromQuaternion(rotation).Forward * -currentDist + panOffset;
        camera.viewMatrix = Matrix.CreateTranslation(-camera.position) * Matrix.Invert(Matrix.CreateFromQuaternion(rotation));
        camera.worldMatrix = Matrix.CreateWorld(Vector3.Zero, Vector3.Forward, Vector3.Up);
    }

    public void Draw(GameTime gameTime)
    {
        if (!attached || host == null || effect == null) return;

        var device = host.GraphicsDevice;

        // The host shares one device across every editor's scene; bind state explicitly.
        device.Viewport = new Viewport(0, 0, width, height);
        device.RasterizerState = RasterizerState.CullNone;
        device.BlendState = BlendState.AlphaBlend;
        device.DepthStencilState = DepthStencilState.Default;
        device.SamplerStates[0] = SamplerState.LinearWrap;

        device.Clear(new Color(0x1e, 0x1e, 0x1e, 0xff));

        if (vertexBuffer != null && indexCount > 0)
        {
            if (usePbr && pbrReady)
                DrawPbr(device, camera.worldMatrix);
            else
                DrawFlat(device);
        }

        DrawTestScene(device);
        DrawGizmo(device);

        // Leave the device clean for whichever scene runs next.
        device.Indices = null;
        device.SetVertexBuffer(null);
    }

    void DrawFlat(GraphicsDevice device)
    {
        effect.World = camera.worldMatrix;
        effect.View = camera.viewMatrix;
        effect.Projection = camera.projectionMatrix;
        effect.Texture = materialTexture ?? fallbackTexture;
        effect.TextureEnabled = true;
        effect.DirectionalLight0.Direction = lightDirection;

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.Indices = indexBuffer;
            device.SetVertexBuffer(vertexBuffer);
            device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, indexCount / 3);
        }
    }

    /// <summary>Albedo + specular highlight + fresnel-weighted environment reflection.</summary>
    void DrawPbr(GraphicsDevice device, Matrix world)
    {
        var parameters = pbrEffect.Parameters;

        parameters["World"]?.SetValue(world);
        parameters["View"]?.SetValue(camera.viewMatrix);
        parameters["Projection"]?.SetValue(camera.projectionMatrix);
        parameters["cameraPos"]?.SetValue(camera.position);
        parameters["LightDir"]?.SetValue(lightDirection);
        parameters["LightColor"]?.SetValue(new Vector3(1.05f, 1.02f, 0.98f));
        parameters["AmbientColor"]?.SetValue(new Vector3(0.16f, 0.17f, 0.20f));
        parameters["Reflectivity"]?.SetValue(currentReflectivity);
        parameters["Shine"]?.SetValue(currentShine);
        parameters["Tint"]?.SetValue(Vector3.One);
        parameters["AlbedoTex"]?.SetValue(materialTexture ?? fallbackTexture);
        parameters["EnvMap"]?.SetValue(environmentMap);

        foreach (var pass in pbrEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.Indices = indexBuffer;
            device.SetVertexBuffer(vertexBuffer);
            device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, indexCount / 3);
        }
    }

    // ---- test scene ----------------------------------------------------------------------

    /// <summary>
    /// A field of cubes for eyeballing how a material reflects.
    ///
    /// A single sphere is a poor test for reflections: one smooth surface, one highlight,
    /// one reflection direction. Scattered rotated cubes give flat faces at many angles,
    /// which is what actually reveals fresnel falloff and a mirror finish.
    /// </summary>
    public bool TestSceneEnabled { get; set; }

    VertexBuffer testVertexBuffer;
    IndexBuffer testIndexBuffer;
    int testIndexCount;
    Matrix[] testInstanceWorlds;

    void BuildTestScene(int cubeCount)
    {
        if (host == null) return;

        if (testVertexBuffer == null)
        {
            var (vertices, indices) = BuildCube();
            testVertexBuffer = new VertexBuffer(host.GraphicsDevice, typeof(VertexPositionNormalTexture),
                vertices.Length, BufferUsage.WriteOnly);
            testVertexBuffer.SetData(vertices);
            testIndexBuffer = new IndexBuffer(host.GraphicsDevice, IndexElementSize.ThirtyTwoBits,
                indices.Length, BufferUsage.WriteOnly);
            testIndexBuffer.SetData(indices);
            testIndexCount = indices.Length;
        }

        // Deterministic pseudo-random layout: same scene every launch, so a change in
        // reflectivity is the only thing that can move.
        var random = new Random(12345);
        testInstanceWorlds = new Matrix[cubeCount];

        for (int i = 0; i < cubeCount; i++)
        {
            float angle = i * 2.399963f; // golden angle, even ring spacing
            float radius = 2.2f + (i / 8) * 0.9f;
            float y = (random.NextSingle() - 0.5f) * 3.2f;

            var position = new Vector3(MathF.Cos(angle) * radius, y, MathF.Sin(angle) * radius);
            var rotation = Quaternion.CreateFromYawPitchRoll(
                random.NextSingle() * MathHelper.TwoPi,
                random.NextSingle() * MathHelper.TwoPi,
                random.NextSingle() * MathHelper.TwoPi);

            float scale = 0.35f + random.NextSingle() * 0.45f;
            testInstanceWorlds[i] = Matrix.CreateFromQuaternion(rotation) * Matrix.CreateScale(scale) * Matrix.CreateTranslation(position);
        }
    }

    void DrawTestScene(GraphicsDevice device)
    {
        if (!TestSceneEnabled || testInstanceWorlds == null || testVertexBuffer == null) return;

        device.RasterizerState = RasterizerState.CullNone;
        device.DepthStencilState = DepthStencilState.Default;

        foreach (var world in testInstanceWorlds)
        {
            if (usePbr && pbrReady) DrawPbr(device, world);
            else DrawFlatTestCube(device, world);
        }

        device.RasterizerState = RasterizerState.CullNone;
    }

    void DrawFlatTestCube(GraphicsDevice device, Matrix world)
    {
        effect.World = world;
        effect.View = camera.viewMatrix;
        effect.Projection = camera.projectionMatrix;
        effect.Texture = materialTexture ?? fallbackTexture;
        effect.TextureEnabled = true;
        effect.DirectionalLight0.Direction = lightDirection;

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.Indices = testIndexBuffer;
            device.SetVertexBuffer(testVertexBuffer);
            device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, testIndexCount / 3);
        }
    }

    public void SetTestSceneCubeCount(int count)
    {
        TestSceneCubeCount = Math.Clamp(count, 1, 64);
        BuildTestScene(TestSceneCubeCount);
    }

    public int TestSceneCubeCount { get; private set; } = 24;
    /// <summary>
    /// Draws the light gizmo on top of the mesh. Depth testing is disabled so the handle
    /// stays clickable even when it sits behind the sphere.
    /// </summary>
    void DrawGizmo(GraphicsDevice device)
    {
        if (gizmoBuffer == null) return;

        lineEffect ??= new BasicEffect(device)
        {
            VertexColorEnabled = true,
            LightingEnabled = false,
            TextureEnabled = false,
            World = camera.worldMatrix,
            View = camera.viewMatrix,
            Projection = camera.projectionMatrix
        };

        lineEffect.World = camera.worldMatrix;
        lineEffect.View = camera.viewMatrix;
        lineEffect.Projection = camera.projectionMatrix;

        device.DepthStencilState = DepthStencilState.DepthRead;
        device.BlendState = BlendState.AlphaBlend;

        if (gizmoArray.Length < 2) return;

        foreach (var pass in lineEffect.CurrentTechnique.Passes)
        {
            pass.Apply();

            // The final argument is primitiveCount, and LineList consumes two vertices
            // per line -- passing the vertex count draws past the buffer.
            device.DrawUserPrimitives(PrimitiveType.LineList, gizmoArray, 0, gizmoArray.Length / 2);
        }

        device.DepthStencilState = DepthStencilState.Default;
    }

    // ---- PBR path ------------------------------------------------------------------------

    /// <summary>
    /// Loads the compiled preview shader. Loaded eagerly in Attach because the shared
    /// EditorHost's Content.RootDirectory gets repointed between the editor's own content
    /// and the mounted game content, and a lazy load can land on the wrong one.
    /// </summary>
    void LoadPbrEffect()
    {
        try
        {
            var content = host.Content;
            string previousRoot = content.RootDirectory;

            // RootDirectory may currently be the mounted game content, which has no
            // Shaders/MaterialPreview.xnb. Try both roots before giving up.
            foreach (var root in new[] { "Content", GlobalEditorData.MountedContentPath })
            {
                if (string.IsNullOrEmpty(root)) continue;

                content.RootDirectory = root;
                try
                {
                    pbrEffect = content.Load<Effect>("Shaders/MaterialPreview");
                    pbrReady = pbrEffect != null;
                    if (pbrReady) break;
                }
                catch
                {
                    pbrEffect = null;
                }
            }

            content.RootDirectory = previousRoot;

            if (pbrReady)
            {
                var technique = pbrEffect.Techniques["MaterialPreview"];
                if (technique != null) pbrEffect.CurrentTechnique = technique;
                else pbrReady = false;
            }

            if (!pbrReady)
                Log.Warn("Material editor: Shaders/MaterialPreview.xnb not found - " +
                    "preview falls back to flat lighting. Run mgcb on Rockwall2\\Content\\Content.mgcb.");
        }
        catch (Exception e)
        {
            pbrReady = false;
            Log.Warn("Material editor: failed to load preview shader: " + e.Message);
        }
    }

    /// <summary>
    /// Renders the shader's SkyGeneration technique into a cubemap, once.
    ///
    /// The repo ships no .dds/.hdr cubemap, so the environment is generated. The cube's raw
    /// vertex positions double as the per-texel direction, which is exactly what a cubemap
    /// face expects.
    /// </summary>
    TextureCube BuildEnvironmentMap(GraphicsDevice device)
    {
        const int FaceSize = 128;

        try
        {
            var sky = pbrEffect.Techniques["SkyGeneration"];
            if (sky == null) return null;

            var cube = new RenderTargetCube(device, FaceSize, true, SurfaceFormat.Color, DepthFormat.None);

            var faces = new[]
            {
                CubeMapFace.PositiveX, CubeMapFace.NegativeX,
                CubeMapFace.PositiveY, CubeMapFace.NegativeY,
                CubeMapFace.PositiveZ, CubeMapFace.NegativeZ
            };

            var vertices = new VertexPosition[8];
            var indices = new short[36];
            BuildCubeGeometry(vertices, indices);

            var vertexBuffer = new VertexBuffer(device, typeof(VertexPosition), vertices.Length, BufferUsage.WriteOnly);
            var indexBuffer = new IndexBuffer(device, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);

            vertexBuffer.SetData(vertices);
            indexBuffer.SetData(indices);

            var oldIndices = device.Indices;

            foreach (var face in faces)
            {
                device.SetRenderTarget(cube, face);

                // No depth and no culling: each face is drawn as the inside of a cube.
                device.DepthStencilState = DepthStencilState.None;
                device.RasterizerState = RasterizerState.CullNone;
                device.Clear(Color.Black);

                foreach (var pass in sky.Passes)
                {
                    pass.Apply();
                    device.Indices = indexBuffer;
                    device.SetVertexBuffer(vertexBuffer);
                    device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, indices.Length / 3);
                }
            }

            device.SetRenderTarget(null);
            device.Indices = oldIndices;
            device.SetVertexBuffer(null);

            vertexBuffer.Dispose();
            indexBuffer.Dispose();

            return cube;
        }
        catch (Exception e)
        {
            Log.Warn("Material editor: failed to build environment map: " + e.Message);
            return null;
        }
    }

    // ---- mesh ---------------------------------------------------------------------------

    /// <summary>
    /// Unit cube as a shared 8-vertex / 36-index mesh, reused for the environment map and
    /// the test scene so both draw from one buffer pair.
    /// </summary>
    static void BuildCubeGeometry(VertexPosition[] vertices, short[] indices)
    {
        vertices[0] = new VertexPosition(new Vector3(-1, -1, -1));
        vertices[1] = new VertexPosition(new Vector3( 1, -1, -1));
        vertices[2] = new VertexPosition(new Vector3( 1,  1, -1));
        vertices[3] = new VertexPosition(new Vector3(-1,  1, -1));
        vertices[4] = new VertexPosition(new Vector3(-1, -1,  1));
        vertices[5] = new VertexPosition(new Vector3( 1, -1,  1));
        vertices[6] = new VertexPosition(new Vector3( 1,  1,  1));
        vertices[7] = new VertexPosition(new Vector3(-1,  1,  1));

        int[] idx =
        {
            0, 1, 2, 0, 2, 3,   // back
            5, 4, 7, 5, 7, 6,   // front
            4, 0, 3, 4, 3, 7,   // left
            1, 5, 6, 1, 6, 2,   // right
            3, 2, 6, 3, 6, 7,   // top
            4, 5, 1, 4, 1, 0,   // bottom
        };

        for (int i = 0; i < idx.Length; i++) indices[i] = (short)idx[i];
    }

    void RebuildMesh()
    {
        if (host == null) return;

        var (vertices, indices) = shape switch
        {
            PreviewShape.Cube => BuildCube(),
            PreviewShape.Plane => BuildPlane(),
            _ => BuildSphere(48, 32)
        };

        vertexBuffer?.Dispose();
        indexBuffer?.Dispose();

        vertexBuffer = new VertexBuffer(host.GraphicsDevice, typeof(VertexPositionNormalTexture),
            vertices.Length, BufferUsage.WriteOnly);
        vertexBuffer.SetData(vertices);

        indexBuffer = new IndexBuffer(host.GraphicsDevice, IndexElementSize.ThirtyTwoBits,
            indices.Length, BufferUsage.WriteOnly);
        indexBuffer.SetData(indices);

        indexCount = indices.Length;
    }

    Vector3 DirectionFromAngles()
    {
        float yaw = MathHelper.ToRadians(lightYaw);
        float pitch = MathHelper.ToRadians(lightPitch);

        return Vector3.Normalize(new Vector3(
            MathF.Cos(pitch) * MathF.Sin(yaw),
            MathF.Sin(pitch),
            MathF.Cos(pitch) * MathF.Cos(yaw)));
    }

    static (VertexPositionNormalTexture[], int[]) BuildSphere(int segments, int rings)
    {
        var vertices = new List<VertexPositionNormalTexture>();
        var indices = new List<int>();

        for (int ring = 0; ring <= rings; ring++)
        {
            float v = ring / (float)rings;
            float phi = v * MathHelper.Pi;

            for (int segment = 0; segment <= segments; segment++)
            {
                float u = segment / (float)segments;
                float theta = u * MathHelper.TwoPi;

                var normal = new Vector3(
                    MathF.Sin(phi) * MathF.Cos(theta),
                    MathF.Cos(phi),
                    MathF.Sin(phi) * MathF.Sin(theta));

                vertices.Add(new VertexPositionNormalTexture(normal, normal, new Vector2(u, v)));
            }
        }

        for (int ring = 0; ring < rings; ring++)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                int current = ring * (segments + 1) + segment;
                int next = current + segments + 1;

                // Wound so the outside faces the camera with backface culling.
                indices.Add(current); indices.Add(next); indices.Add(current + 1);
                indices.Add(current + 1); indices.Add(next); indices.Add(next + 1);
            }
        }

        return (vertices.ToArray(), indices.ToArray());
    }

    static (VertexPositionNormalTexture[], int[]) BuildCube()
    {
        var vertices = new List<VertexPositionNormalTexture>();
        var indices = new List<int>();

        void Face(Vector3 normal, Vector3 right, Vector3 up)
        {
            int start = vertices.Count;

            foreach (var uv in new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) })
            {
                var position = normal + (right * (uv.X * 2 - 1)) + (up * (uv.Y * 2 - 1));
                vertices.Add(new VertexPositionNormalTexture(position, normal, uv));
            }

            indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
            indices.Add(start); indices.Add(start + 2); indices.Add(start + 3);
        }

        Face(Vector3.Forward, Vector3.Right, Vector3.Up);
        Face(Vector3.Backward, Vector3.Left, Vector3.Up);
        Face(Vector3.Right, Vector3.Backward, Vector3.Up);
        Face(Vector3.Left, Vector3.Forward, Vector3.Up);
        Face(Vector3.Up, Vector3.Right, Vector3.Forward);
        Face(Vector3.Down, Vector3.Right, Vector3.Backward);

        return (vertices.ToArray(), indices.ToArray());
    }

    static (VertexPositionNormalTexture[], int[]) BuildPlane()
    {
        var vertices = new VertexPositionNormalTexture[]
        {
            new(new Vector3(-1, 0, -1), Vector3.Up, new Vector2(0, 0)),
            new(new Vector3( 1, 0, -1), Vector3.Up, new Vector2(1, 0)),
            new(new Vector3( 1, 0,  1), Vector3.Up, new Vector2(1, 1)),
            new(new Vector3(-1, 0,  1), Vector3.Up, new Vector2(0, 1)),
        };

        return (vertices, new[] { 0, 1, 2, 0, 2, 3 });
    }

    // ---- texture ------------------------------------------------------------------------

    void SwapTexture(string path)
    {
        if (host == null) return;

        if (materialTexture != null && materialTexture != fallbackTexture)
            materialTexture.Dispose();

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            materialTexture = fallbackTexture;
            return;
        }

        try
        {
            // FromStream rather than Content.Load: a freshly imported texture has no .xnb
            // until the game is rebuilt, and this must reflect the file as edited on disk.
            using var stream = File.OpenRead(path);
            materialTexture = Texture2D.FromStream(host.GraphicsDevice, stream);
        }
        catch
        {
            materialTexture = fallbackTexture;
        }
    }

    static Texture2D BuildCheckerTexture(GraphicsDevice device)
    {
        const int size = 64;
        const int cell = 8;

        var texture = new Texture2D(device, size, size, false, SurfaceFormat.Color);

        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = ((x / cell + y / cell) % 2 == 0) ? Color.Gray : Color.DarkGray;

        texture.SetData(pixels);
        return texture;
    }
}