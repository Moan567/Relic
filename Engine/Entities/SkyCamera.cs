using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Entities
{
    [EntityDescriptor()]
    public class SkyCamera : WorldEntity
    {
        public static WorldEntity activeSkyCamera;

        public SkyCamera()
        {
            activeSkyCamera = this;
        }
    }
}
