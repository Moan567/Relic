using Chisel.Utils;
using Engine.Compilation;
using Engine.Utils;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Entities.BrushEntities;

[EntityDescriptor()]
[ExposeEntityProperty("Open on Use", Rockwall.EntityPropertyType.Bool, "If true, the Use input will fire Open and Close when appropriate.")]
[RegisterEntityInputs("Open","Close","Use")]
public class FuncDoor : BrushEntity
{
    public FuncDoor()
    {
        Controller = new FuncDoorController();
        var door = Controller as FuncDoorController;
        RegisterInputLocally("Open", (s,e) => door.Open());
        RegisterInputLocally("Close", (s,e) => door.Close());
        RegisterInputLocally("Use", (s,e) => door.Use());
    }

    internal class FuncDoorController : EntityController
    {
        [SaveValue("cur_rot")] float currentRotation;
        [SaveValue("targ_rot")] float targetRotation;
        [SaveValue("base_rot")] float? baseRotation;
        [SaveValue("opened")] bool isOpen;

        public override void OnDespawn()
        {
        }

        public override void OnRender(GameTime gameTime)
        {
        }

        public override void OnSpawn()
        {
            if (!baseRotation.HasValue) baseRotation = entity.SpawnRotation.Y;
        }

        public override void OnUpdate(GameTime gameTime)
        {
            currentRotation = CMath.MoveTowardsAngle(currentRotation,targetRotation,MainEngine.PreviousFrameDelta * 90);

            entity.Rotation = Quaternion.CreateFromAxisAngle(Vector3.Up,MathHelper.ToRadians(currentRotation + baseRotation.Value));
        }

        public void Open()
        {
            if(!isOpen)
            {
                targetRotation = 90;
            }

            isOpen = true;
        }
        public void Close()
        {
            if (isOpen)
            {
                targetRotation = 0;
            }

            isOpen = false;
        }
        public void Use()
        {
            if (!(bool)entity.ReadProperty("Open on Use", Rockwall.EntityPropertyType.Bool)) return;

            if (isOpen) Close();
            else Open();
        }
    }
}
