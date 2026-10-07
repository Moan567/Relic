using Chisel.Utils;
using Engine.Compilation;
using Engine.Rendering;
using Engine.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using System;

namespace Engine.Entities;

[EntityDescriptor]
[ExposeEntityProperty("Sprite Material", Rockwall.EntityPropertyType.Material, "Material for the sprite.")]
[ExposeEntityProperty("Sprite Size", Rockwall.EntityPropertyType.Float, "Size in units that this sprite will render as.", defaultValue: "1")]
[ExposeEntityPropertyEnum("Render Mode", "Changes how this sprite renders. See docs for more info.", "General",
    "Normal", "Additive")]
[ExposeEntityProperty("Alpha Multiplier", Rockwall.EntityPropertyType.Float, "A multiplier to the texture's alpha.", defaultValue: "1")]
[ExposeEntityProperty("Color Multiplier", Rockwall.EntityPropertyType.Color, "A multiplier to the texture's color.", defaultValue: "255,255,255")]
[RegisterEntityInputs("Show", "Hide", "Toggle", "SetSize", "SetAlpha", "SetColor")]
[EntityVisualize(typeof(SpriteVisualizer))]
[VisualizerProperty(nameof(SpriteVisualizer.Material), "Sprite Material")]
[VisualizerProperty(nameof(SpriteVisualizer.Size), "Sprite Size")]
[VisualizerProperty(nameof(SpriteVisualizer.Color), "Color Multiplier")]
[VisualizerProperty(nameof(SpriteVisualizer.RenderMode), "Render Mode")]
public class EnvSprite : WorldEntity
{
    private string spriteMaterial;
    private float spriteSize;
    private string renderMode;
    private Color renderColor;
    private float renderAlpha;

    public EnvSprite()
    {
        IsSimulated = false;
        IgnoreCollision = true;

        Controller = new EnvSpriteController(this);

        var c = (EnvSpriteController)Controller;
        RegisterInputLocally("Show", (s, e) => c.SetVisible(true));
        RegisterInputLocally("Hide", (s, e) => c.SetVisible(false));
        RegisterInputLocally("Toggle", (s, e) => c.ToggleVisible());
        RegisterInputLocally("SetSize", (s, e) => { if (float.TryParse(s, out var v)) c.SetSize(v); });
        RegisterInputLocally("SetAlpha", (s, e) => { if (float.TryParse(s, out var v)) c.SetAlpha(v); });
        RegisterInputLocally("SetColor", (s, e) => c.SetColor(s));
    }

    public class EnvSpriteController(EnvSprite sprite) : EntityController
    {
        EnvSprite sprite = sprite;

        enum RenderMode
        {
            Normal,
            Add
        }
        RenderMode curRenderMode;

        bool visible = true;

        Texture2D texture;

        private static ShaderHandle spriteShader;

        private static void EnsureResources(GraphicsDevice device)
        {
            spriteShader ??= ShaderBuilder.BuildParticlesShader(device);
        }

        public override void OnSpawn()
        {
            sprite.spriteMaterial = (string)entity.ReadProperty("Sprite Material", Rockwall.EntityPropertyType.Material);
            sprite.spriteSize = (float)entity.ReadProperty("Sprite Size", Rockwall.EntityPropertyType.Float);
            sprite.renderMode = (string)entity.ReadProperty("Render Mode", Rockwall.EntityPropertyType.String);
            sprite.renderAlpha = (float)entity.ReadProperty("Alpha Multiplier", Rockwall.EntityPropertyType.Float);
            sprite.renderColor = (Color)entity.ReadProperty("Color Multiplier", Rockwall.EntityPropertyType.Color);

            curRenderMode = sprite.renderMode == "Additive" ? RenderMode.Add : RenderMode.Normal;

            if (!string.IsNullOrEmpty(sprite.spriteMaterial) && GlobalMapData.MaterialNameToIndex.TryGetValue(sprite.spriteMaterial, out int materialIndex))
            {
                TextureMipGenerator.ReserveMaterial(materialIndex);
                texture = GlobalMapData.LoadedMaterials[materialIndex].Texture;
            }

            EnsureResources(MainEngine.Instance.GraphicsDevice);
        }

        public override void OnDespawn()
        {
        }

        public override void OnUpdate(GameTime gameTime)
        {
        }

        public override void OnRender(GameTime gameTime)
        {
            if (texture == null || !visible) return;

            float alpha = sprite.renderAlpha;
            if (alpha <= 0.001f) return;

            var tint = new Color(sprite.renderColor.R, sprite.renderColor.G, sprite.renderColor.B, (byte)Math.Clamp(alpha * 255f, 0f, 255f));
            var worldPos = entity.Position;
            var size = sprite.spriteSize;
            var tex = texture;
            var mode = curRenderMode;

            TransparentRenderQueue.RegisterFreeform(worldPos, () => DrawSprite(worldPos, size, tex, tint, mode));
        }

        private static void DrawSprite(Vector3 worldPos, float size, Texture2D tex, Color tint, RenderMode mode)
        {
            var device = MainEngine.Instance.GraphicsDevice;
            var cameraPos = RenderEngine.CameraPosition;

            Matrix world = Matrix.CreateScale(size) * Matrix.CreateWorld(worldPos, RenderEngine.CameraForward, RenderEngine.CameraUp);

            device.BlendState = mode == RenderMode.Add ? BlendState.Additive : BlendState.NonPremultiplied;
            device.DepthStencilState = DepthStencilState.DepthRead;
            device.RasterizerState = RasterizerState.CullNone;

            spriteShader.SetTechnique("Particles");
            spriteShader.Param("World").SetValue(world);
            spriteShader.Param("View").SetValue(RenderEngine.ViewMatrix);
            spriteShader.Param("Projection").SetValue(RenderEngine.ProjectionMatrix);
            spriteShader.Param("MainTex").SetValue(tex);
            spriteShader.Param("AlphaClip").SetValue(false);
            spriteShader.Param("UseVertColor").SetValue(true);

            DrawTintedQuad(device, tint);
        }

        private static void DrawTintedQuad(GraphicsDevice device, Color tint)
        {
            var tinted = new VertexPositionColorTexture[]
            {
                new(new Vector3(-0.5f, -0.5f, 0f), tint, new Vector2(0, 1)),
                new(new Vector3(-0.5f,  0.5f, 0f), tint, new Vector2(0, 0)),
                new(new Vector3( 0.5f, -0.5f, 0f), tint, new Vector2(1, 1)),
                new(new Vector3( 0.5f,  0.5f, 0f), tint, new Vector2(1, 0)),
            };
            spriteShader.RenderEachPass(() => device.DrawUserPrimitives(PrimitiveType.TriangleStrip, tinted, 0, 2));
        }

        public void SetVisible(bool v) => visible = v;
        public void ToggleVisible() => visible = !visible;
        public void SetSize(float v) => sprite.spriteSize = v;
        public void SetAlpha(float v) => sprite.renderAlpha = v;
        public void SetColor(string csv)
        {
            var parts = csv.Split(',');
            if (parts.Length < 3) return;
            sprite.renderColor = new Color(byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2]), (byte)255);
        }
    }
}