using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Assimp.Metadata;

namespace Engine.Entities
{
    [EntityDescriptor()]
    [ExposeEntityProperty("Fog Color", Rockwall.EntityPropertyType.Color)]
    [ExposeEntityProperty("Fog Intensity", Rockwall.EntityPropertyType.Float)]
    [ExposeEntityProperty("Fog Start", Rockwall.EntityPropertyType.Float)]
    [ExposeEntityProperty("Fog End", Rockwall.EntityPropertyType.Float)]
    public class FogController : WorldEntity
    {
        public FogController()
        {
            Controller = new FogControllerController();
        }
    }
    internal class FogControllerController : EntityController
    {
        public override void OnDespawn()
        {
        }

        public override void OnRender(GameTime gameTime)
        {
        }

        public override void OnSpawn()
        {
            string color = Array.Find(entity.properties, prop => prop.Name == "Fog Color").Value;
            string[] colors = color.Split(',');

            string strength = Array.Find(entity.properties, prop => prop.Name == "Fog Intensity").Value;
            string start = Array.Find(entity.properties, prop => prop.Name == "Fog Start").Value;
            string end = Array.Find(entity.properties, prop => prop.Name == "Fog End").Value;

            MainEngine.Instance.FogColor = new Color(byte.Parse(colors[0]), byte.Parse(colors[1]), byte.Parse(colors[2]), (byte)255);
            MainEngine.Instance.FogStrength = float.Parse(strength, CultureInfo.InvariantCulture);
            MainEngine.Instance.FogBeginDepth = float.Parse(start, CultureInfo.InvariantCulture);
            MainEngine.Instance.FogEndDepth = float.Parse(end, CultureInfo.InvariantCulture);

            EntityManager.DespawnEntity(entity);
        }

        public override void OnUpdate(GameTime gameTime)
        {
        }
    }
}
