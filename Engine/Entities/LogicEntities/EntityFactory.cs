using Engine.Compilation;
using System;

namespace Engine.Entities.LogicEntities;

[EntityDescriptor()]
[RegisterEntityInputs("CreateEntity")]
[ExposeEntityProperty("Entity To Create", Rockwall.EntityPropertyType.String, "Class name of the entity to spawn")]
public class EntityFactory : WorldEntity
{
    public EntityFactory()
    {
        AxisAlignedBox = true;
        IgnoreCollision = true;
        IsSimulated = false;

        RegisterInputLocally("CreateEntity", (s, f) =>
        {
            var ent = (WorldEntity)Activator.CreateInstance(EntityCompiler.EntityLookupTable[(string)ReadProperty("Entity To Create", Rockwall.EntityPropertyType.String)]);
            
            ent.Position = Position;
            ent.Rotation = Rotation;
            ent.Velocity = Velocity;
            ent.Scale = Scale;

            EntityManager.SpawnEntity(ent);
        });
    }
}
