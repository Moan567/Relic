using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapCompiler
{
    public static class Outside
    {
        public static Dictionary<uint, List<int>> bspNodeOccupants = new Dictionary<uint, List<int>>();
        public static List<uint> floodSeed = new List<uint>();
        public static bool PlaceOccupant(int num, Vector3 point)
        {
            var nodeID = BSPRoot.Traverse(point);
            var node = BSPRoot.Nodes[nodeID];

            if (node.solid) return false;

            if(!bspNodeOccupants.TryGetValue(nodeID, out var occupants))
            {
                occupants = new List<int>();
                bspNodeOccupants.Add(nodeID,occupants);
            }

            occupants.Add(num);
            floodSeed.Add(nodeID);
            return true;
        }
    }
}
