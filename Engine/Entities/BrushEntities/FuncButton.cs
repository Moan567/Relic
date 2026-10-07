using Chisel.Utils;
using Engine.Compilation;
using Engine.Utils;
using FontStashSharp;
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
    [ExposeEntityProperty("Move Direction", EntityPropertyType.Direction, "The direction this button moves when pressed (0,1,0 is down, along the Y axis).", defaultValue = "0,1,0")]
    [RegisterEntityInputs("Use")]
    [RegisterEntityOutputs("OnInteracted")]
    [EntityVisualize(typeof(ArrowVisualizer))]
    [VisualizerProperty(nameof(ArrowVisualizer.Direction), "Move Direction")]
    public class FuncButton : BrushEntity
    {
        public FuncButton()
        {
            Controller = new FuncButtonController();
            RegisterInputLocally("Use", (s, e) => ((FuncButtonController)Controller).OnInteract(e));
        }
        public override void OnEntityEnter(WorldEntity entity)
        {
        }
    }
    public class FuncButtonController : EntityController
    {
        Vector3 startPosition;
        Vector3 direction;
        float pressedTime;
        float animationTime;
        float targetAnimationTime;
        public override void OnDespawn()
        {
        }

        public override void OnRender(GameTime gameTime)
        {
        }

        public override void OnSpawn()
        {
            startPosition = entity.Position;
            direction = (Vector3)entity.ReadProperty("Move Direction", EntityPropertyType.Direction);
        }

        public override void OnUpdate(GameTime gameTime)
        {
            entity.Position = startPosition + direction * animationTime * 0.1f;

            if(pressedTime > 0f)
            {
                pressedTime -= MainEngine.PreviousFrameDelta;
                if(pressedTime <= 0f)
                {
                    pressedTime = 0f;
                    targetAnimationTime = 0;
                }
            }

            animationTime = CMath.MoveTowards(animationTime, targetAnimationTime, MainEngine.PreviousFrameDelta*8);
        }

        public void OnInteract(WorldEntity e)
        {
            entity.CallOutput("OnInteracted",e);
            targetAnimationTime = -1;
            pressedTime = 1f;
        }
    }
}
