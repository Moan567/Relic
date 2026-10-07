using Engine.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Entities
{
    [EntityDescriptor()]
    [ExposeEntityProperty("Model", EntityPropertyType.Model)]
    internal class DetailModel : WorldEntity
    {
        public DetailModel() 
        { 
            Controller = new DetailModelController();
        }
    }

    public class DetailModelController : EntityController
    {
        //CModelDisplay model;

        public override void OnDespawn()
        {
        }

        public override void OnRender(GameTime gameTime)
        {
            //model.CheckForLights(entity.Position);
            //model.Draw();
        }
        public override void OnBeforeRender(GameTime gameTime)
        {
        }

        public override void OnSpawn()
        {
            EntityManager.DespawnEntity(entity);
            //string modelName = entity.properties[0].Value;

            //model = new CModelDisplay(modelName);
            //model.Transform = Matrix.CreateFromQuaternion(entity.Rotation) * Matrix.CreateTranslation(entity.Position);
        }

        public override void OnUpdate(GameTime gameTime)
        {
        }
    }
}
