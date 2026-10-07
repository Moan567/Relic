using Engine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace Engine.Entities
{
    [EntityDescriptor()]
    public class EnvCubemap : WorldEntity
    {
        public static List<EnvCubemap> Cubemaps = new List<EnvCubemap>();
        public static bool cubeRendering = false;

        public TextureCube[] diffusionMaps;

        public EnvCubemap()
        {
            Controller = new CubemapController();
            IsSimulated = false;
            IgnoreCollision = true;
        }

        internal class CubemapController : EntityController
        {
            public override void OnSpawn()
            {
                Cubemaps.Add((EnvCubemap)entity);
            }

            public override void OnDespawn()
            {
            }

            public override void OnUpdate(GameTime gameTime)
            {
            }

            public override void OnRender(GameTime gameTime)
            {
            }
        }
    }
}