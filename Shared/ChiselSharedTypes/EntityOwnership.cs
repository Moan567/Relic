using System;
using System.Collections.Generic;

namespace Rockwall
{
    public static class EntityOwnership
    {
        public static int[] Sync(Brush[] brushes, EntityReference[] entities)
        {
            var owner = new int[brushes.Length];
            for (int i = 0; i < owner.Length; i++) owner[i] = -1;

            for (int i = 0; i < brushes.Length; i++) brushes[i].IsEntity = false;

            if (entities != null)
            {
                for (int e = 0; e < entities.Length; e++)
                {
                    var idxs = entities[e]?.BrushIndices;
                    if (idxs == null) continue;

                    for (int k = 0; k < idxs.Count; k++)
                    {
                        int bi = idxs[k];
                        if (bi < 0 || bi >= brushes.Length) continue;

                        brushes[bi].IsEntity = true;
                        owner[bi] = e;
                    }
                }
            }

            return owner;
        }

        public static bool IsPartOfEntity(int[] ownerMap, int brushIndex)
        {
            return brushIndex >= 0 && brushIndex < ownerMap.Length && ownerMap[brushIndex] >= 0;
        }
    }
}
