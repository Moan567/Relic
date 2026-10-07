using Chisel.Collision;
using Engine.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Entities;

[EntityDescriptor]
[ExposeEntityProperty("Decal Material",Rockwall.EntityPropertyType.Material,"The texture to project with this decal.")]
[ExposeEntityProperty("Decal Min Bounds", Rockwall.EntityPropertyType.Position, "The local-space minimum for the OBB that projects the decal.", defaultValue: "-0.5,-0.5,-0.25")]
[ExposeEntityProperty("Decal Max Bounds", Rockwall.EntityPropertyType.Position, "The local-space maximum for the OBB that projects the decal.", defaultValue: "0.5,0.5,0.25")]
[ExposeEntityProperty("Decal UV Scale X", Rockwall.EntityPropertyType.Float, "The X scale for the decal's UVs.", defaultValue: "1")]
[ExposeEntityProperty("Decal UV Scale Y", Rockwall.EntityPropertyType.Float, "The Y scale for the decal's UVs.", defaultValue: "1")]
[ExposeEntityProperty("Decal UV Offset X", Rockwall.EntityPropertyType.Float, "The X Offset for the decal's UVs.", defaultValue: "0")]
[ExposeEntityProperty("Decal UV Offset Y", Rockwall.EntityPropertyType.Float, "The Y Offset for the decal's UVs.", defaultValue: "0")]
public class EnvDecal : WorldEntity
{
    public EnvDecal()
    {
        IgnoreCollision = true;
        IsSimulated = false;

        Controller = new EnvDecalController();
    }
    private class EnvDecalController : EntityController
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

        public override void OnAllEntitiesSpawned()
        {
            Vector3 min = (Vector3)entity.ReadProperty("Decal Min Bounds", Rockwall.EntityPropertyType.Position);
            Vector3 max = (Vector3)entity.ReadProperty("Decal Max Bounds", Rockwall.EntityPropertyType.Position);

            Vector3 extents = (max - min) * 0.5f;
            Vector3 center = entity.Position + min + extents;

            var obb = new OrientedBoundingBox(new BoundingBox(-extents, extents));
            obb.Transformation = Matrix.CreateFromQuaternion(entity.Rotation) * Matrix.CreateTranslation(center);

            string materialName = (string)entity.ReadProperty("Decal Material", Rockwall.EntityPropertyType.Material);
            if (string.IsNullOrEmpty(materialName) || !GlobalMapData.MaterialNameToIndex.TryGetValue(materialName, out int materialIndex)) return;

            TextureMipGenerator.ReserveMaterial(materialIndex);

            Texture2D texture = GlobalMapData.LoadedMaterials[materialIndex].Texture;

            if (texture == null) return;

            Vector2 uvScale = new Vector2(
                (float)entity.ReadProperty("Decal UV Scale X", Rockwall.EntityPropertyType.Float),
                (float)entity.ReadProperty("Decal UV Scale Y", Rockwall.EntityPropertyType.Float));
            Vector2 uvOffset = new Vector2(
                (float)entity.ReadProperty("Decal UV Offset X", Rockwall.EntityPropertyType.Float),
                (float)entity.ReadProperty("Decal UV Offset Y", Rockwall.EntityPropertyType.Float));

            int decalIndex = DecalGenerator.ProjectDecal(obb, texture, -1, uvScale, uvOffset);
            DecalManager.RealtimeDecals[decalIndex].permanent = true;
        }
    }
}
