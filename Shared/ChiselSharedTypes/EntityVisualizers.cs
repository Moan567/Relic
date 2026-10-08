using Microsoft.Xna.Framework;

namespace Rockwall
{
    public abstract class EntityVisualizer { }

    public class SphereVisualizer : EntityVisualizer
    {
        public float Radius;
        public Color Color = Color.White;
    }

    public class ConeVisualizer : EntityVisualizer
    {
        public float Length;
        public float Angle;
        public Color Color = Color.White;
    }

    public class ArrowVisualizer : EntityVisualizer
    {
        public Vector3 Direction;
        public float Length = 1f;
        public Color Color = Color.White;
    }
    public class SpriteVisualizer : EntityVisualizer
    {
        public string Material;
        public float Size = 1f;
        public Color Color = Color.White;
        public string RenderMode = "Normal";
    }
    public class ModelVisualizer : EntityVisualizer
    {
        public string Model;
    }
}
