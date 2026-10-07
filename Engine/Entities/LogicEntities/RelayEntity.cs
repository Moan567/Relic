using Engine.Compilation;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Entities.LogicEntities
{
    [EntityDescriptor()]
    [RegisterEntityInputs("Trigger")]
    [RegisterEntityOutputs("OnTrigger")]
    public class LogicRelay : WorldEntity
    {
        public LogicRelay()
        {
            Controller = new RelayController();
            IsSimulated = false;
            IgnoreCollision = true;
            RegisterInputLocally("Trigger",(Controller as RelayController).OnTrigger);
        }
    }

    public class RelayController : EntityController
    {
        public override void OnDespawn()
        {
        }
        public override void OnRender(GameTime gameTime)
        {
        }
        public override void OnSpawn()
        {
            entity.IgnoreCollision = true;
        }

        public void OnTrigger(string param, WorldEntity from)
        {
            //var targ = Array.Find(entity.properties, p => p.Name == "Target");

            //if (string.IsNullOrEmpty(targ.Value)) return;

            //var ents = EntityManager.FindEntityIndexByName(targ.Value);

            //if (ents == null) return;

            //foreach (int index in ents)
            //{
            //    var info = EntityManager.entities[index].controller.GetType().GetMethod(funcTarget);

            //    if (info == null) continue;

            //    info.Invoke(EntityManager.entities[index].controller, new object[] { e, funcParams });
            //}
            entity.CallOutput("OnTrigger",from,$"arg:{param}");
        }

        public override void OnUpdate(GameTime gameTime)
        {
        }
    }
}
