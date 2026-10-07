using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Entities
{
    [EntityDescriptor()]
    public class GroundNode : WorldEntity
    {
        public GroundNode()
        {
            //This is merely for placing nodes, it doesnt have any game function, so lets not waste space
            EntityManager.DespawnEntity(this);
        }
    }
}
