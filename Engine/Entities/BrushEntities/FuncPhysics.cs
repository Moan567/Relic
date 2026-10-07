using Engine.Compilation;
using Engine.Physics;
using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Entities.BrushEntities
{
    [EntityDescriptor()]
    [ExposeEntityProperty("Frozen On Spawn", EntityPropertyType.Bool, "False to have the brush start as a dynamic object, true to make it kinematic at first and unfrozen later.")]
    [RegisterEntityInputs("BecomeDynamic")]
    public class FuncPhysics : BrushEntity
    {
        public FuncPhysics()
        {
            Controller = new FuncPhysicsController();
            IsSimulated = false;
            AxisAlignedBox = false;

            RegisterInputLocally("BecomeDynamic", (s, e) =>
            {
                (Controller as FuncPhysicsController).CalculateMass();
            });
        }
        public override void OnEntityEnter(WorldEntity entity)
        {
        }
    }
    public class FuncPhysicsController : EntityController
    {
        bool frozen = false;
        public override void OnDespawn()
        {
        }

        public override void OnRender(GameTime gameTime)
        {
        }

        public override void OnSpawn()
        {
            frozen = (bool)(entity.ReadProperty("Frozen On Spawn", EntityPropertyType.Bool) ?? false);

            if (!frozen) CalculateMass();
        }

        public void CalculateMass()
        {
            PhysicsEngine.BodyInterface.SetMotionType(entity.PhysicsBodyID,JoltPhysicsSharp.MotionType.Dynamic,JoltPhysicsSharp.Activation.Activate);

            entity.IsSimulated = true;
        }

        public override void OnUpdate(GameTime gameTime)
        {
        }
    }
}
