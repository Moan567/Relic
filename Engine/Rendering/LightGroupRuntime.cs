using Engine.Console;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Rendering;

public static class LightGroupRuntime
{
    private const int BatchSize = 4;

    public struct GroupState
    {
        public bool enabled;
        public Color color;
        public float intensity;
        public string styleName;
        public float styleStartTime;
        public Texture2D texture;
        public Vector2 uvMin, uvMax;
    }

    private static readonly Dictionary<string, GroupState> groups = new();
    private static int unnamedCounter;
    private static bool dirty;

    public struct ActiveGroupSample
    {
        public int groupIndex;
        public Vector3 colorTint;
        public float intensity;
    }

    private static readonly Dictionary<string, int> groupKeyIndex = new();
    private static readonly Dictionary<string, string> lightNameToGroup = new();
    private static readonly List<ActiveGroupSample> activeGroupSamples = new();
    public static IReadOnlyList<ActiveGroupSample> ActiveGroupSamples => activeGroupSamples;

    // Every enabled group's resolved color/intensity this frame, keyed by archive
    // key rather than a light-node group index
    private static readonly Dictionary<string, (Vector3 colorTint, float intensity)> resolvedGroupTints = new();
    public static bool TryGetResolvedTint(string archiveKey, out Vector3 colorTint, out float intensity)
    {
        if (resolvedGroupTints.TryGetValue(archiveKey, out var t))
        {
            colorTint = t.colorTint;
            intensity = t.intensity;
            return true;
        }
        colorTint = Vector3.Zero;
        intensity = 0f;
        return false;
    }
    public static string GetCorrespondingGroup(string lightName)
    {
        if (lightNameToGroup.TryGetValue(lightName, out var val)) return val;

        return null;
    }

    public static float Now { get; private set; }

    public static RenderTarget2D CurrentB1 { get; private set; }
    public static RenderTarget2D CurrentB2 { get; private set; }
    public static RenderTarget2D CurrentB3 { get; private set; }

    private static Texture2D indexB1, indexB2, indexB3;
    private static int resolution;
    private static ShaderHandle compositeShader;
    private static VertexBuffer compositeQuad;

    private const float UpdateRate = 1 / 60f;
    private static float curUpdTime;

    public static void ResetForNewMap()
    {
        groups.Clear();
        lightNameToGroup.Clear();
        unnamedCounter = 0;
        Now = 0f;
        dirty = true;
        activeGroupSamples.Clear();
    }
    public static void SetGroupKeyOrder(string[] keys)
    {
        groupKeyIndex.Clear();
        if (keys == null) return;
        for (int i = 0; i < keys.Length; i++) groupKeyIndex[keys[i]] = i;
    }

    public static string NextUnnamedKey() => $"unnamed{unnamedCounter++}";

    public static void RegisterGroup(string archiveKey, string targetLights, bool startEnabled, Color defaultColor, float defaultIntensity, string style)
    {
        var state = groups.TryGetValue(archiveKey, out var existing) ? existing : new GroupState();
        state.enabled = startEnabled;
        state.color = defaultColor;
        state.intensity = defaultIntensity;
        state.styleName = style;
        state.styleStartTime = Now;
        groups[archiveKey] = state;
        lightNameToGroup[targetLights] = archiveKey;
        dirty = true;

        Logger.AppendBasic($"LightGroup '{archiveKey}' registered: enabled={startEnabled}, color={defaultColor}, intensity={defaultIntensity}, style='{style}'");
    }

    public static void UnregisterGroup(string archiveKey)
    {
        if (groups.Remove(archiveKey)) dirty = true;
    }

    public static void SetEnabled(string archiveKey, bool enabled)
    {
        if (!groups.TryGetValue(archiveKey, out var state) || state.enabled == enabled) return;
        state.enabled = enabled;
        groups[archiveKey] = state;
        dirty = true;
    }

    public static bool TryGetState(string archiveKey, out GroupState state) => groups.TryGetValue(archiveKey, out state);

    public static void SetColor(string archiveKey, Color color)
    {
        if (!groups.TryGetValue(archiveKey, out var state)) return;
        state.color = color;
        groups[archiveKey] = state;
        dirty = true;
    }

    public static void SetIntensity(string archiveKey, float intensity)
    {
        if (!groups.TryGetValue(archiveKey, out var state)) return;
        state.intensity = intensity;
        groups[archiveKey] = state;
        dirty = true;
    }

    public static void SetStyle(string archiveKey, string styleName)
    {
        if (!groups.TryGetValue(archiveKey, out var state)) return;
        state.styleName = styleName;
        state.styleStartTime = Now;
        groups[archiveKey] = state;
        dirty = true;
    }

    public static void LoadLayer(string archiveKey, Texture2D texture, Vector2 uvMin, Vector2 uvMax)
    {
        var state = groups.TryGetValue(archiveKey, out var existing) ? existing : new GroupState { color = Color.White, intensity = 1f };
        state.texture = texture;
        state.uvMin = uvMin;
        state.uvMax = uvMax;
        groups[archiveKey] = state;
        dirty = true;
    }

    public static void SetIndexLayer(GraphicsDevice gd, Texture2D b1, Texture2D b2, Texture2D b3)
    {
        indexB1 = b1; indexB2 = b2; indexB3 = b3;
        resolution = b1.Width;

        CurrentB1?.Dispose(); CurrentB2?.Dispose(); CurrentB3?.Dispose();
        CurrentB1 = new RenderTarget2D(gd, resolution, resolution, false, SurfaceFormat.HdrBlendable, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        CurrentB2 = new RenderTarget2D(gd, resolution, resolution, false, SurfaceFormat.HdrBlendable, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        CurrentB3 = new RenderTarget2D(gd, resolution, resolution, false, SurfaceFormat.HdrBlendable, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);

        dirty = true;
    }

    public static void SetCompositeShader(ShaderHandle shader) => compositeShader = shader;
    public static void Update(GraphicsDevice gd, SpriteBatch spriteBatch, GameTime gameTime)
    {
        Now += (float)gameTime.ElapsedGameTime.TotalSeconds;

        curUpdTime -= (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (curUpdTime > 0f) return;

        curUpdTime = UpdateRate;

        bool anyStyleRunning = false;
        foreach (var kvp in groups)
        {
            if (kvp.Value.enabled && !string.IsNullOrEmpty(kvp.Value.styleName)) { anyStyleRunning = true; break; }
        }

        if (!dirty && !anyStyleRunning) return;
        dirty = false;

        // Resolved color/intensity for every enabled group this frame.
        var active = new List<(string key, GroupState state, Vector3 colorTint, float intensity)>();
        foreach (var kvp in groups)
        {
            if (!kvp.Value.enabled) continue;

            float styleMul = 1f;
            Color styleTint = Color.White;
            if (!string.IsNullOrEmpty(kvp.Value.styleName) && LightStyleRegistry.TryGet(kvp.Value.styleName, out var style))
            {
                var sample = LightPageResolver.Evaluate(style, Now - kvp.Value.styleStartTime);
                styleMul = sample.Intensity;
                styleTint = sample.Tint;
            }

            var colorTint = new Vector3(
                kvp.Value.color.R / 255f * styleTint.R / 255f,
                kvp.Value.color.G / 255f * styleTint.G / 255f,
                kvp.Value.color.B / 255f * styleTint.B / 255f);

            active.Add((kvp.Key, kvp.Value, colorTint, kvp.Value.intensity * styleMul));
        }

        activeGroupSamples.Clear();
        resolvedGroupTints.Clear();
        foreach (var (key, _, colorTint, intensity) in active)
        {
            resolvedGroupTints[key] = (colorTint, intensity);

            if (!groupKeyIndex.TryGetValue(key, out int groupIndex)) continue;
            activeGroupSamples.Add(new ActiveGroupSample { groupIndex = groupIndex, colorTint = colorTint, intensity = intensity });
        }

        if (CurrentB1 == null || compositeShader == null) return;

        var fullRect = new Rectangle(0, 0, resolution, resolution);

        BlitOne(gd, spriteBatch, CurrentB1, indexB1, fullRect);
        BlitOne(gd, spriteBatch, CurrentB2, indexB2, fullRect);
        BlitOne(gd, spriteBatch, CurrentB3, indexB3, fullRect);

        var textured = active.Where(a => a.state.texture != null).ToList();
        if (textured.Count == 0) return;

        gd.SetRenderTargets(CurrentB1, CurrentB2, CurrentB3);

        for (int i = 0; i < textured.Count; i += BatchSize)
        {
            int count = Math.Min(BatchSize, textured.Count - i);
            Vector2 unionMin = new(1f, 1f), unionMax = new(0f, 0f);

            for (int slot = 0; slot < BatchSize; slot++)
            {
                Vector4 tint = Vector4.Zero;
                Texture2D tex = indexB1;

                if (slot < count)
                {
                    var (_, state, colorTint, intensity) = textured[i + slot];
                    tint = new Vector4(colorTint.X, colorTint.Y, colorTint.Z, intensity);
                    tex = state.texture;
                    unionMin = Vector2.Min(unionMin, state.uvMin);
                    unionMax = Vector2.Max(unionMax, state.uvMax);
                }

                compositeShader.Param($"Tint{slot}").SetValue(tint);
                compositeShader.Param($"Group{slot}").SetValue(tex);
            }

            if (unionMax.X < unionMin.X) { unionMin = Vector2.Zero; unionMax = Vector2.One; }

            var pixelRect = new Rectangle(
                (int)(unionMin.X * resolution), (int)(unionMin.Y * resolution),
                (int)MathF.Ceiling((unionMax.X - unionMin.X) * resolution),
                (int)MathF.Ceiling((unionMax.Y - unionMin.Y) * resolution));

            DrawCompositeQuad(gd, pixelRect);
        }
    }

    private static void EnsureCompositeQuad(GraphicsDevice gd)
    {
        if (compositeQuad != null) return;

        var verts = new[]
        {
            new VertexPositionTexture(new Vector3(-1, -1, 0), new Vector2(0, 1)),
            new VertexPositionTexture(new Vector3(-1, 1, 0), new Vector2(0, 0)),
            new VertexPositionTexture(new Vector3(1, -1, 0), new Vector2(1, 1)),
            new VertexPositionTexture(new Vector3(1, 1, 0), new Vector2(1, 0)),
        };
        compositeQuad = new VertexBuffer(gd, typeof(VertexPositionTexture), 4, BufferUsage.WriteOnly);
        compositeQuad.SetData(verts);
    }

    private static void DrawCompositeQuad(GraphicsDevice gd, Rectangle pixelRect)
    {
        EnsureCompositeQuad(gd);

        var oldViewport = gd.Viewport;
        gd.Viewport = new Viewport(pixelRect);
        var oldBlend = gd.BlendState;
        gd.BlendState = BlendState.Additive;
        gd.SamplerStates[0] = SamplerState.PointClamp;
        gd.SetVertexBuffer(compositeQuad);

        compositeShader.RenderEachPass(() => gd.DrawPrimitives(PrimitiveType.TriangleStrip, 0, 2));

        gd.BlendState = oldBlend;
        gd.Viewport = oldViewport;
    }

    private static void BlitOne(GraphicsDevice gd, SpriteBatch spriteBatch, RenderTarget2D target, Texture2D source, Rectangle rect)
    {
        for (var i = 0; i < 15; i++)
            gd.Textures[i] = null;

        gd.SetRenderTarget(target);
        spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        spriteBatch.Draw(source, rect, Color.White);
        spriteBatch.End();
    }
}