using Gum.Forms.Controls;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGameGum;
using MonoGameGum.Forms;
using MonoGameGum.Forms.Controls;
using MonoGameGum.GueDeriving;
using RenderingLibrary.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.UI
{
    public class CGWindow : InteractiveGue
    {
        public CGWindow(string windowName,bool fullInstantiation = true, bool tryCreateFormsObject = true) : base(new InvisibleRenderable())
        {
            if (fullInstantiation)
            {
                this.Width = 256;
                this.Height = 256;
                
                var background = new NineSliceRuntime();
                background.Name = "WindowBackground";
                background.Dock(Gum.Wireframe.Dock.Fill);
                // This is too thick, looks bad:
                //background.Width = -2 * borderWidth;
                //background.Height = -2 * borderWidth;

                background.Color = new Color(255,255,255);
                background.Texture = MainEngine.Instance.Content.Load<Texture2D>("UI/cguipanel");
                background.TextureWidth = 128;
                background.TextureHeight = 128;
                this.AddChild(background);

                float borderWidth = 10;

                var titleBar = new Gum.Forms.Controls.Label();
                titleBar.Anchor(Gum.Wireframe.Anchor.TopLeft);
                titleBar.Text = windowName;
                //titleBar.TextComponent.UseCustomFont = true;
                //titleBar.TextComponent.CustomFontFile = $"Fonts/default_small.fnt";
                //titleBar.TextComponent.FontSize = 16;
                titleBar.X = 10;
                titleBar.Y = 10;
                this.AddChild(titleBar);
                ((TextRuntime)titleBar.Visual).BitmapFont = Styling.Default18;

                InnerPanel = new ContainerRuntime();
                InnerPanel.Name = "InnerPanelInstance";
                InnerPanel.Dock(Gum.Wireframe.Dock.Fill);
                InnerPanel.Width = -24;
                InnerPanel.Height = -48;
                InnerPanel.Y = 12;
                this.AddChild(InnerPanel);

                // Do this first so it sits behind the resize panel:
                var titlePanel = new Gum.Forms.Controls.Panel();
                titlePanel.Dock(Gum.Wireframe.Dock.Top);
                titlePanel.Height = 32;
                titlePanel.Name = "TitleBarInstance";
                this.AddChild(titlePanel);

                var borderTopLeft = new Gum.Forms.Controls.Panel();
                borderTopLeft.Name = "BorderTopLeftInstance";
                borderTopLeft.Anchor(Gum.Wireframe.Anchor.TopLeft);
                borderTopLeft.Width = borderWidth;
                borderTopLeft.Height = borderWidth;
                borderTopLeft.CustomCursor = Cursors.SizeNWSE;
                this.AddChild(borderTopLeft);

                var borderTopRight = new Gum.Forms.Controls.Panel();
                borderTopRight.Name = "BorderTopRightInstance";
                borderTopRight.Anchor(Gum.Wireframe.Anchor.TopRight);
                borderTopRight.Width = borderWidth;
                borderTopRight.Height = borderWidth;
                borderTopRight.CustomCursor = Cursors.SizeNESW;
                this.AddChild(borderTopRight);

                var borderBottomLeft = new Gum.Forms.Controls.Panel();
                borderBottomLeft.Name = "BorderBottomLeftInstance";
                borderBottomLeft.Anchor(Gum.Wireframe.Anchor.BottomLeft);
                borderBottomLeft.Width = borderWidth;
                borderBottomLeft.Height = borderWidth;
                borderBottomLeft.CustomCursor = Cursors.SizeNESW;
                this.AddChild(borderBottomLeft);

                var borderBottomRight = new Gum.Forms.Controls.Panel();
                borderBottomRight.Name = "BorderBottomRightInstance";
                borderBottomRight.Anchor(Gum.Wireframe.Anchor.BottomRight);
                borderBottomRight.Width = borderWidth;
                borderBottomRight.Height = borderWidth;
                borderBottomRight.CustomCursor = Cursors.SizeNWSE;
                this.AddChild(borderBottomRight);

                var borderTop = new Gum.Forms.Controls.Panel();
                borderTop.Name = "BorderTopInstance";
                borderTop.Dock(Gum.Wireframe.Dock.Top);
                borderTop.Height = borderWidth;
                borderTop.Width = -borderWidth * 2;
                borderTop.CustomCursor = Cursors.SizeNS;
                this.AddChild(borderTop);

                var borderBottom = new Gum.Forms.Controls.Panel();
                borderBottom.Name = "BorderBottomInstance";
                borderBottom.Dock(Gum.Wireframe.Dock.Bottom);
                borderBottom.Height = borderWidth;
                borderBottom.Width = -borderWidth * 2;
                borderBottom.CustomCursor = Cursors.SizeNS;
                this.AddChild(borderBottom);

                var borderLeft = new Gum.Forms.Controls.Panel();
                borderLeft.Name = "BorderLeftInstance";
                borderLeft.Dock(Gum.Wireframe.Dock.Left);
                borderLeft.Width = borderWidth;
                borderLeft.Height = -borderWidth * 2;
                borderLeft.CustomCursor = Cursors.SizeWE;
                this.AddChild(borderLeft);

                var borderRight = new Gum.Forms.Controls.Panel();
                borderRight.Name = "BorderRightInstance";
                borderRight.Dock(Gum.Wireframe.Dock.Right);
                borderRight.Width = borderWidth;
                borderRight.Height = -borderWidth * 2;
                borderRight.CustomCursor = Cursors.SizeWE;
                this.AddChild(borderRight);
            }


            if (tryCreateFormsObject)
            {
                FormsControlAsObject = new Gum.Forms.Window(this);
            }
        }

        public event Action Closed;

        public void Open()
        {
            IsEnabled = true;
            FormsControl.IsVisible = true;
            FormsControl.AddToRoot();
        }
        public void Close()
        {
            IsEnabled = false;
            FormsControl.IsVisible = false;
            FormsControl.RemoveFromRoot();
            Closed?.Invoke();
        }
        public Gum.Forms.Window FormsControl => FormsControlAsObject as Gum.Forms.Window;
        public ContainerRuntime InnerPanel;
    }
}
