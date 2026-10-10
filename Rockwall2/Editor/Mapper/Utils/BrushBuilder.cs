using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Mapper.Utils;
public abstract class BrushBuilder
{
    public bool IsOpen { get; private set; }

    public virtual void Open() { IsOpen = true; }
    public virtual void Close() { IsOpen = false; }

    public abstract void OnRender(float delta, GraphicsDevice graphicsDevice, BasicEffect basicEffect);
    public abstract void Commit();
    public abstract void RegenerateGeometry();
}
[AttributeUsage(AttributeTargets.Field)]
public class BuilderParamAttribute : Attribute
{
    public string Label;
    public float Min, Max, Increment = 1f;
    public string FormatString = "0.###";

    public BuilderParamAttribute(string label, float min = float.MinValue, float max = float.MaxValue, float increment = 1f)
    {
        Label = label; Min = min; Max = max; Increment = increment;
    }
}