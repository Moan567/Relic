using Engine.Compilation;
using Engine.Rendering;
using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Entities;

[EntityDescriptor()]
[ExposeEntityPropertyTarget("Target", "Light(s) to add to the group. Shared names will count all lights with that shared name.")]
[ExposeEntityProperty("Style", Rockwall.EntityPropertyType.String, "Name of a registered LightStyle, or empty for plain on/off.")]
[ExposeEntityProperty("Default Color", Rockwall.EntityPropertyType.Color, defaultValue: "255,255,255")]
[ExposeEntityProperty("Default Intensity", Rockwall.EntityPropertyType.Float, defaultValue: "1")]
[ExposeEntityProperty("Start Enabled", Rockwall.EntityPropertyType.Bool, defaultValue: "1")]
[RegisterEntityInputs("Enable", "Disable", "Toggle", "SetStyle", "SetColor", "SetIntensity")]
public class LightGroup : WorldEntity
{
    public LightGroup()
    {
        Controller = new LightGroupController();
        IsSimulated = false;
        IgnoreCollision = true;

        var c = (LightGroupController)Controller;
        RegisterInputLocally("Enable", (s, e) => c.SetEnabled(true));
        RegisterInputLocally("Disable", (s, e) => c.SetEnabled(false));
        RegisterInputLocally("Toggle", (s, e) => c.Toggle());
        RegisterInputLocally("SetStyle", (s, e) => c.SetStyle(s));
        RegisterInputLocally("SetColor", (s, e) => c.SetColor(s));
        RegisterInputLocally("SetIntensity", (s, e) => c.SetIntensity(s));
    }
}

public class LightGroupController : EntityController
{
    public string ArchiveKey { get; private set; }

    public override void OnSpawn()
    {
        ArchiveKey = string.IsNullOrEmpty(entity.Name) ? LightGroupRuntime.NextUnnamedKey() : entity.Name;

        var defaultColor = (Color)entity.ReadProperty("Default Color", EntityPropertyType.Color);
        float defaultIntensity = (float)entity.ReadProperty("Default Intensity", EntityPropertyType.Float);
        bool startEnabled = (bool)entity.ReadProperty("Start Enabled", EntityPropertyType.Bool);
        string style = (string)entity.ReadProperty("Style", EntityPropertyType.String) ?? "";
        string targets = (string)entity.ReadProperty("Target", EntityPropertyType.String) ?? "";

        LightGroupRuntime.RegisterGroup(ArchiveKey, targets, startEnabled, defaultColor, defaultIntensity, style);
    }

    public override void OnDespawn() => LightGroupRuntime.UnregisterGroup(ArchiveKey);
    public override void OnRender(GameTime gameTime) { }
    public override void OnUpdate(GameTime gameTime) { }

    public void SetEnabled(bool enabled) => LightGroupRuntime.SetEnabled(ArchiveKey, enabled);

    public void Toggle()
    {
        if (LightGroupRuntime.TryGetState(ArchiveKey, out var state))
            LightGroupRuntime.SetEnabled(ArchiveKey, !state.enabled);
    }

    public void SetStyle(string styleName) => LightGroupRuntime.SetStyle(ArchiveKey, styleName);

    public void SetColor(string csv)
    {
        var parts = csv.Split(',');
        if (parts.Length < 3) return;
        LightGroupRuntime.SetColor(ArchiveKey, new Color(byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2]), (byte)255));
    }

    public void SetIntensity(string s)
    {
        if (float.TryParse(s, out var val)) LightGroupRuntime.SetIntensity(ArchiveKey, val);
    }
}