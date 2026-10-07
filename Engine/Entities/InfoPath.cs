using Engine.Compilation;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Entities;
[EntityDescriptor]
[ExposeEntityProperty("Move Smoothly", Rockwall.EntityPropertyType.Bool, "Move smoothly from point to point.")]
[ExposeEntityProperty("Move Speed", Rockwall.EntityPropertyType.Float, "How fast the path follower moves at this point.")]
[ExposeEntityProperty("Rotate Speed", Rockwall.EntityPropertyType.Float, "How fast the path follower moves at this point.")]
[ExposeEntityPropertyTarget("Next Waypoint", "The targetname of the next InfoPath in this specific sequence.")]
[AutoConnectOnDuplicate("Next Waypoint")]
[RegisterEntityOutputs("OnPassed")]
internal class InfoPath : WorldEntity
{
    public InfoPathController PathController;
    public InfoPath()
    {
        PathController = new InfoPathController();
        Controller = PathController;
        IsSimulated = false;
        IgnoreCollision = true;
    }
    public class InfoPathController : EntityController
    {
        public float MoveSpeed;
        public float RotateSpeed;
        public bool MoveSmooth;
        public override void OnDespawn()
        {
        }
        public override void OnRender(GameTime gameTime)
        {
        }
        public override void OnSpawn()
        {
            MoveSpeed = (float)(entity.ReadProperty("Move Speed", Rockwall.EntityPropertyType.Float) ?? 0f);
            RotateSpeed = (float)(entity.ReadProperty("Rotate Speed", Rockwall.EntityPropertyType.Float) ?? 0f);
            MoveSmooth = (bool)(entity.ReadProperty("Move Smoothly", Rockwall.EntityPropertyType.Bool) ?? false);
        }
        public override void OnUpdate(GameTime gameTime)
        {
        }
    }
    public InfoPath GetNext()
    {
        return EntityManager.FindSingleEntityByName(ReadProperty("Next Waypoint", Rockwall.EntityPropertyType.String) as string) as InfoPath;
    }
}
