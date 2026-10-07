using Engine.Compilation;
using Engine.Entities.LogicEntities;
using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Entities.BrushEntities
{
    [EntityDescriptor()]
    [ExposeEntityPropertyTarget("Filter Name", "The filter to use to filter out entities, if any.")]
    [RegisterEntityOutputs("OnTriggerEnter","OnTriggerExit","OnTriggerStay")]
    public class FuncTrigger : BrushEntity
    {
        public FuncTrigger() 
        {
            Controller = new FuncTriggerController();
            IgnoreCollision = true;
        }
        public override void OnEntityEnter(WorldEntity entity)
        {
            var filter = EntityManager.FindEntityIndexByName(ReadProperty("Filter Name", EntityPropertyType.String) as string);
            if (filter != null && filter.Length > 0)
            {
                var filterEntity = EntityManager.entities[filter[0]];
                if (filterEntity is LogicFilter logicFilter && !logicFilter.Test(entity))
                {
                    insideBrush.Remove(entity);
                    return;
                }
            }
            CallOutput("OnTriggerEnter", entity, $"from_class:{entity.GetType()}");
        }
        public override void OnEntityExit(WorldEntity entity)
        {
            CallOutput("OnTriggerExit", entity, $"from_class:{entity.GetType()}");
        }
        internal class FuncTriggerController : EntityController
        {
            [ExposeValue("inside_count")]
            private int entitiesInside;

            public override void OnDespawn()
            {
            }

            public override void OnRender(GameTime gameTime)
            {
            }

            public override void OnSpawn()
            {
            }
            public override void OnUpdate(GameTime gameTime)
            {
                var bEnt = (BrushEntity)entity;
                entitiesInside = bEnt.insideBrush.Count;
                for (int i = bEnt.insideBrush.Count - 1; i >= 0; i--)
                {
                    entity.CallOutput("OnTriggerStay", bEnt.insideBrush[i],
                        $"from_class:{bEnt.insideBrush[i].GetType()}");
                }
            }
        }
    }


    [EntityDescriptor()]
    [ExposeEntityPropertyTarget("Filter Name", "The filter to use to filter out entities, if any.")]
    [RegisterEntityOutputs("OnTriggerEnter", "OnTriggerExit")]
    public class FuncTriggerOnce : BrushEntity
    {
        [SaveValue("enter_triggered")]
        bool hasEntered;
        [SaveValue("exit_triggered")]
        bool hasExited;
        public FuncTriggerOnce()
        {
            Controller = new FuncTriggerOnceController();
            IgnoreCollision = true;
        }
        public override void OnEntityEnter(WorldEntity entity)
        {
            var filter = EntityManager.FindEntityIndexByName(ReadProperty("Filter Name", EntityPropertyType.String) as string);
            if (filter != null && filter.Length > 0)
            {
                var filterEntity = EntityManager.entities[filter[0]];
                if (filterEntity is LogicFilter logicFilter && !logicFilter.Test(entity))
                {
                    insideBrush.Remove(entity);
                    return;
                }
            }
            if(!hasEntered) CallOutput("OnTriggerEnter", entity, $"from_class:{entity.GetType()}");
            hasEntered = true;
        }
        public override void OnEntityExit(WorldEntity entity)
        {
            if (!hasExited) CallOutput("OnTriggerExit", entity, $"from_class:{entity.GetType()}");
            hasExited = true;
        }
        internal class FuncTriggerOnceController : EntityController
        {
            public override void OnDespawn()
            {
            }

            public override void OnRender(GameTime gameTime)
            {
            }

            public override void OnSpawn()
            {
                // Bounds are already set correctly by BrushEntity.Spawn().
            }
            public override void OnUpdate(GameTime gameTime)
            {
            }
        }
    }

    [EntityDescriptor()]
    [ExposeEntityPropertyTarget("Filter Name", "The filter to use to filter out entities, if any.")]
    [RegisterEntityOutputs("OnTriggerEnter", "OnTriggerExit")]
    public class FuncTriggerAll : BrushEntity
    {
        public FuncTriggerAll()
        {
            Controller = new FuncTriggerAllController();
            IgnoreCollision = true;
        }
        public override void OnEntityEnter(WorldEntity entity)
        {
            var filter = EntityManager.FindEntityIndexByName(ReadProperty("Filter Name", EntityPropertyType.String) as string);
            if (filter != null && filter.Length > 0)
            {
                var filterEntity = EntityManager.entities[filter[0]];
                if (filterEntity is LogicFilter logicFilter && !logicFilter.Test(entity))
                {
                    insideBrush.Remove(entity);
                    return;
                }
            }
            if (insideBrush.Count == 0) CallOutput("OnTriggerEnter", entity, $"from_class:{entity.GetType()}");
        }
        public override void OnEntityExit(WorldEntity entity)
        {
            if (insideBrush.Count == 0) CallOutput("OnTriggerExit", entity, $"from_class:{entity.GetType()}");
        }
        internal class FuncTriggerAllController : EntityController
        {
            public override void OnDespawn()
            {
            }

            public override void OnRender(GameTime gameTime)
            {
            }

            public override void OnSpawn()
            {
                // Bounds are already set correctly by BrushEntity.Spawn().
            }
            public override void OnUpdate(GameTime gameTime)
            {
            }
        }
    }
}
