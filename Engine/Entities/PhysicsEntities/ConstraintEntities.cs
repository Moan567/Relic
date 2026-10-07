using Engine.Physics.Constraints;
using Engine.Compilation;
using Rockwall;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Engine.Console;

namespace Engine.Entities.PhysicsEntities;

[EntityDescriptor()]
public class PhysicsAnchorEntity : WorldEntity
{
    public PhysicsAnchorEntity()
    {
        IsSimulated = false;
        AxisAlignedBox = false;
        IgnoreCollision = true;

        Bounds = new BoundingBox(-Vector3.One * 1, Vector3.One * 1);

        Scale = Vector3.One;

        Controller = new AnchorController();
    }
}
public class AnchorController : EntityController
{
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
    }
}

[EntityDescriptor]
[ExposeEntityPropertyTarget("Entity A", "Target name of the first entity to be constrained")]
[ExposeEntityPropertyTarget("Entity B", "Target name of the second entity to be constrained")]
public class FixedConstraintEntity : WorldEntity
{
    internal class FixedConstraintController : EntityController
    {
        FixedConstraint constraint;
        public override void OnDespawn()
        {
            constraint.Detach();
        }

        public override void OnRender(GameTime gameTime)
        {
        }

        public override void OnSpawn()
        {
            constraint = new FixedConstraint();
        }

        public override void OnUpdate(GameTime gameTime)
        {
            if(!constraint.IsAttached)
            {
                var nameA = (string)entity.ReadProperty("Entity A", Rockwall.EntityPropertyType.String);
                var nameB = (string)entity.ReadProperty("Entity B", Rockwall.EntityPropertyType.String);
                var entityA = EntityManager.entities.FindIndex(e=>e?.Name == nameA);
                var entityB = EntityManager.entities.FindIndex(e=>e?.Name == nameB);

                if (entityA == -1 || entityB == -1)
                {
                    Logger.AppendError($"Fixed Constraint cannot find entities! {nameA}, {nameB}");
                    EntityManager.DespawnEntity(entity);
                    return;
                }

                constraint.Attach(EntityManager.entities[entityA], EntityManager.entities[entityB]);
            }
        }
    }

    public FixedConstraintEntity()
    {
        Bounds = new BoundingBox(Vector3.Zero,Vector3.One);
        IsSimulated = false;
        IgnoreCollision = true;
        Controller = new FixedConstraintController();
    }
}

[EntityDescriptor]
[ExposeEntityPropertyTarget("Entity A", "Target name of the first entity to be constrained")]
[ExposeEntityPropertyTarget("Entity B", "Target name of the second entity to be constrained")]
[ExposeEntityProperty("Auto Max Distance", Rockwall.EntityPropertyType.Bool, "If true, max distance is computed from the entities' starting distance apart")]
[ExposeEntityProperty("Min Distance", Rockwall.EntityPropertyType.Float, "Minimum distance allowed between the entities")]
[ExposeEntityProperty("Max Distance", Rockwall.EntityPropertyType.Float, "Maximum distance allowed between the entities, ignored if Auto Max Distance is true")]
[ExposeEntityProperty("Damping", Rockwall.EntityPropertyType.Float, "Spring damping ratio")]
[ExposeEntityProperty("Frequency", Rockwall.EntityPropertyType.Float, "Spring oscillation frequency")]
public class SpringConstraintEntity : WorldEntity
{
    internal class SpringConstraintController : EntityController
    {
        SpringConstraint constraint;
        public override void OnDespawn()
        {
            constraint.Detach();
        }
        public override void OnRender(GameTime gameTime)
        {
        }
        public override void OnSpawn()
        {
            constraint = new SpringConstraint();
        }
        public override void OnUpdate(GameTime gameTime)
        {
            if (!constraint.IsAttached)
            {
                var nameA = (string)entity.ReadProperty("Entity A", Rockwall.EntityPropertyType.String);
                var nameB = (string)entity.ReadProperty("Entity B", Rockwall.EntityPropertyType.String);
                var entityA = EntityManager.entities.FindIndex(e => e?.Name == nameA);
                var entityB = EntityManager.entities.FindIndex(e => e?.Name == nameB);
                if (entityA == -1 || entityB == -1)
                {
                    Logger.AppendError($"Spring Constraint cannot find entities! {nameA}, {nameB}");
                    EntityManager.DespawnEntity(entity);
                    return;
                }

                constraint.AutoMaxDistance = (bool)entity.ReadProperty("Auto Max Distance", Rockwall.EntityPropertyType.Bool);
                constraint.MinDistance = (float)entity.ReadProperty("Min Distance", Rockwall.EntityPropertyType.Float);
                constraint.MaxDistance = (float)entity.ReadProperty("Max Distance", Rockwall.EntityPropertyType.Float);
                constraint.Damping = (float)entity.ReadProperty("Damping", Rockwall.EntityPropertyType.Float);
                constraint.Frequency = (float)entity.ReadProperty("Frequency", Rockwall.EntityPropertyType.Float);

                constraint.Attach(EntityManager.entities[entityA], EntityManager.entities[entityB]);
            }
        }
    }
    public SpringConstraintEntity()
    {
        Bounds = new BoundingBox(Vector3.Zero, Vector3.One);
        IsSimulated = false;
        IgnoreCollision = true;
        Controller = new SpringConstraintController();
    }
}

[EntityDescriptor]
[ExposeEntityPropertyTarget("Entity A", "Target name of the first entity to be constrained")]
[ExposeEntityPropertyTarget("Entity B", "Target name of the second entity to be constrained")]
public class PointConstraintEntity : WorldEntity
{
    internal class PointConstraintController : EntityController
    {
        PointConstraint constraint;
        public override void OnDespawn()
        {
            constraint.Detach();
        }
        public override void OnRender(GameTime gameTime)
        {
        }
        public override void OnSpawn()
        {
            constraint = new PointConstraint();
        }
        public override void OnUpdate(GameTime gameTime)
        {
            if (!constraint.IsAttached)
            {
                var nameA = (string)entity.ReadProperty("Entity A", Rockwall.EntityPropertyType.String);
                var nameB = (string)entity.ReadProperty("Entity B", Rockwall.EntityPropertyType.String);
                var entityA = EntityManager.entities.FindIndex(e => e?.Name == nameA);
                var entityB = EntityManager.entities.FindIndex(e => e?.Name == nameB);
                if (entityA == -1 || entityB == -1)
                {
                    Logger.AppendError($"Point Constraint cannot find entities! {nameA}, {nameB}");
                    EntityManager.DespawnEntity(entity);
                    return;
                }
                constraint.Attach(EntityManager.entities[entityA], EntityManager.entities[entityB]);
            }
        }
    }
    public PointConstraintEntity()
    {
        Bounds = new BoundingBox(Vector3.Zero, Vector3.One);
        IsSimulated = false;
        IgnoreCollision = true;
        Controller = new PointConstraintController();
    }
}
