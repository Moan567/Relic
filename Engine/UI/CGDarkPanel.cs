using Gum.Wireframe;
using Microsoft.Xna.Framework.Graphics;
using MonoGameGum.GueDeriving;
using RenderingLibrary.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.UI
{
    public class CGDarkPanel : InteractiveGue
    {
        public CGDarkPanel(int basecolor = 35) : base(new InvisibleRenderable())
        {
            var Background = new NineSliceRuntime();
            Background.Name = "Background";
            this.Children.Add(Background);

            Background.Texture = MainEngine.Instance.Content.Load<Texture2D>("UI/cguiinput");
            Background.TextureAddress = Gum.Managers.TextureAddress.EntireTexture;
            Background.Color = new Microsoft.Xna.Framework.Color(basecolor, basecolor, basecolor);
            Background.Width = -1;
            Background.Height = -1;
            Background.WidthUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent;
            Background.HeightUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent;
        }
    }
}
