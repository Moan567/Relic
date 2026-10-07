using Engine.Compilation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Entities
{
    public class DirectionalLightController : EntityController
    {
        public override void OnSpawn()
        {
            MainEngine.Instance.DirectionalLightDirection = Vector3.Transform(Vector3.Forward, Matrix.CreateFromQuaternion(entity.Rotation));
            MainEngine.Instance.DirectionalLightRightVector = Vector3.Transform(Vector3.Right, Matrix.CreateFromQuaternion(entity.Rotation));
            MainEngine.Instance.DirectionalLightForwardVector = Vector3.Transform(Vector3.Up, Matrix.CreateFromQuaternion(entity.Rotation));
            MainEngine.Instance.DirectionalLightRotation = entity.Rotation;

            string color = Array.Find(entity.properties, prop => prop.Name == "Color").Value;
            string[] colors = color.Split(',');

            string strength = Array.Find(entity.properties, prop => prop.Name == "Intensity").Value;

            MainEngine.Instance.DirectionalLightColor = new Color(byte.Parse(colors[0]), byte.Parse(colors[1]), byte.Parse(colors[2]), (byte)255);

            //tweak the colors to kinda match everything else, dunno why its kinda off
            MainEngine.Instance.DirectionalLightColor.R = (byte)(MainEngine.Instance.DirectionalLightColor.R);
            MainEngine.Instance.DirectionalLightColor.G = (byte)(MainEngine.Instance.DirectionalLightColor.G);
            MainEngine.Instance.DirectionalLightColor.B = (byte)(MainEngine.Instance.DirectionalLightColor.B);

            MainEngine.Instance.DirectionalLightStrength = float.Parse(strength, CultureInfo.InvariantCulture);


            color = Array.Find(entity.properties, prop => prop.Name == "Ambient Color").Value;
            colors = color.Split(',');

            strength = Array.Find(entity.properties, prop => prop.Name == "Ambient Intensity").Value;


            MainEngine.Instance.AmbientSkyColor = new Color(byte.Parse(colors[0]), byte.Parse(colors[1]), byte.Parse(colors[2]), (byte)255);
            MainEngine.Instance.AmbientSkylightStrength = float.Parse(strength, CultureInfo.InvariantCulture);


            MainEngine.ActiveStaticLights.Add(new Rockwall.Light
            {
                Type = Rockwall.Light.LightType.Directional,
                ID = int.Parse(Array.Find(entity.properties, prop => prop.Name == "ID").Value),
                TargetName = entity.Name,
            });

            string skybox = Array.Find(entity.properties, prop => prop.Name == "Skybox").Value;
            if (!string.IsNullOrEmpty(skybox)) Skybox.SetSkyTexture(Skybox.LoadSkybox(MainEngine.Instance.Content, MainEngine.Instance.GraphicsDevice, skybox));
        }
        public override void OnDespawn()
        {
        }

        public override void OnRender(GameTime gameTime)
        {
        }

        public override void OnUpdate(GameTime gameTime)
        {
        }
    }
    [EntityDescriptor()]
    [ExposeEntityProperty("Color", Rockwall.EntityPropertyType.Color)]
    [ExposeEntityProperty("Intensity", Rockwall.EntityPropertyType.Float)]
    [ExposeEntityProperty("Ambient Color", Rockwall.EntityPropertyType.Color)]
    [ExposeEntityProperty("Ambient Intensity", Rockwall.EntityPropertyType.Float)]
    [ExposeEntityProperty("Skybox", Rockwall.EntityPropertyType.Texture)]
    public class DirectionalLight : WorldEntity
    {
        public DirectionalLight()
        {
            Controller = new DirectionalLightController();
            IsLight = true;
            IgnoreCollision = true;
        }
    }

    [EntityDescriptor()]
    //[EntityVisualize(typeof(SphereVisualizer))]
    //[VisualizerProperty(nameof(SphereVisualizer.Radius), "Range")]
    //[VisualizerProperty(nameof(SphereVisualizer.Color), "Color")]
    [ExposeEntityProperty("Color", Rockwall.EntityPropertyType.Color)]
    [ExposeEntityProperty("Intensity", Rockwall.EntityPropertyType.Float)]
    [ExposeEntityProperty("Range", Rockwall.EntityPropertyType.Float)]
    public class PointLight : WorldEntity
    {
        public PointLight()
        {
            Controller = new PointLightController();
            IsLight = true;
            IgnoreCollision = true;
        }
    }
    public class PointLightController : EntityController
    {
        public override void OnDespawn()
        {
        }

        public override void OnRender(GameTime gameTime)
        {
        }

        public override void OnSpawn()
        {
            string color = Array.Find(entity.properties, prop => prop.Name == "Color").Value;
            string[] colors = color.Split(',');

            string strength = Array.Find(entity.properties, prop => prop.Name == "Intensity").Value;
            string range = Array.Find(entity.properties, prop => prop.Name == "Range").Value;

            MainEngine.ActiveStaticLights.Add(new Rockwall.Light
            {
                Color = new Color(byte.Parse(colors[0]), byte.Parse(colors[1]), byte.Parse(colors[2]), (byte)255),
                Intensity = float.Parse(strength, CultureInfo.InvariantCulture),
                Range = float.Parse(range, CultureInfo.InvariantCulture),
                Position = entity.Position,
                Type = Rockwall.Light.LightType.Point,
                TargetName = entity.Name,
                ID = int.Parse(Array.Find(entity.properties, prop => prop.Name == "ID").Value)
            });
        }

        public override void OnUpdate(GameTime gameTime)
        {
        }
    }
    [EntityDescriptor()]
    //[EntityVisualize(typeof(ConeVisualizer))]
    //[VisualizerProperty(nameof(ConeVisualizer.Angle), "Spot Angle")]
    //[VisualizerProperty(nameof(ConeVisualizer.Length), "Range")]
    //[VisualizerProperty(nameof(ConeVisualizer.Color), "Color")]
    [ExposeEntityProperty("Color", Rockwall.EntityPropertyType.Color)]
    [ExposeEntityProperty("Intensity", Rockwall.EntityPropertyType.Float)]
    [ExposeEntityProperty("Range", Rockwall.EntityPropertyType.Float)]
    [ExposeEntityProperty("Spot Angle", Rockwall.EntityPropertyType.Float)]
    [ExposeEntityProperty("Inner Angle", Rockwall.EntityPropertyType.Float)]
    public class SpotLight : WorldEntity
    {
        public SpotLight()
        {
            Controller = new SpotLightController();
            IsLight = true;
            IgnoreCollision = true;
        }
    }
    public class SpotLightController : EntityController
    {
        public override void OnDespawn()
        {
        }

        public override void OnRender(GameTime gameTime)
        {
        }

        public override void OnSpawn()
        {
            string color = Array.Find(entity.properties, prop => prop.Name == "Color").Value;
            string[] colors = color.Split(',');

            string strength = Array.Find(entity.properties, prop => prop.Name == "Intensity").Value;
            string range = Array.Find(entity.properties, prop => prop.Name == "Range").Value;
            string spotAngle = Array.Find(entity.properties, prop => prop.Name == "Spot Angle").Value;

            MainEngine.ActiveStaticLights.Add(new Rockwall.Light
            {
                Color = new Color(byte.Parse(colors[0]), byte.Parse(colors[1]), byte.Parse(colors[2]), (byte)255),
                Intensity = float.Parse(strength, CultureInfo.InvariantCulture),
                Range = float.Parse(range, CultureInfo.InvariantCulture),
                Position = entity.Position,
                Rotation = Matrix.CreateFromQuaternion(entity.Rotation).Forward,
                Angle = float.Parse(spotAngle, CultureInfo.InvariantCulture),
                Type = Rockwall.Light.LightType.SpotLight,
                TargetName = entity.Name,
                ID = int.Parse(Array.Find(entity.properties, prop => prop.Name == "ID").Value)
            });
        }

        public override void OnUpdate(GameTime gameTime)
        {
        }
    }
}
