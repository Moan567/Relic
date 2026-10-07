using Engine.Compilation;
using Engine.Utils;
using Microsoft.Xna.Framework;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Entities
{
    [EntityDescriptor()]
    [ExposeEntityProperty("State to Set",Rockwall.EntityPropertyType.String)]
    [RegisterEntityInputs("TurnOn","TurnOff","Toggle")]
    public class EnvGlobal : WorldEntity
    {
        public EnvGlobal()
        {
            Controller = new EnvGlobalController();
            RegisterInputLocally("Toggle", (s, e) => { GlobalState.SetState(Array.Find(properties, p => p.Name == "State to Set").Value, !GlobalState.ReadState(Array.Find(properties, p => p.Name == "State to Set").Value)); });
            RegisterInputLocally("TurnOn", (s, e) => { GlobalState.SetState(Array.Find(properties, p => p.Name == "State to Set").Value, true); });
            RegisterInputLocally("TurnOff", (s, e) => { GlobalState.SetState(Array.Find(properties, p => p.Name == "State to Set").Value, false); });
        }
    }
    public class EnvGlobalController : EntityController
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
}
