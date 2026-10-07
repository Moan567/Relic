using Gum.DataTypes.Variables;
using Gum.Forms;
using Gum.Forms.Controls;
using Gum.Forms.DefaultVisuals;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGameGum.GueDeriving;
using RenderingLibrary.Graphics;

namespace Engine.UI
{
    public static class Styling
    {
        public static BitmapFont Default48 => new BitmapFont($"{MainEngine.FullPath}/Gum/Fonts/default48.fnt");
        public static BitmapFont Default32 => new BitmapFont($"{MainEngine.FullPath}/Gum/Fonts/default32.fnt");
        public static BitmapFont Default24 => new BitmapFont($"{MainEngine.FullPath}/Gum/Fonts/default24.fnt");
        public static BitmapFont Default18 => new BitmapFont($"{MainEngine.FullPath}/Gum/Fonts/default18.fnt");
        public static class Colors
        {
            public static Color Primary { get; set; } = new Color(80, 80, 80);
            public static Color PrimaryLight { get; set; } = new Color(100, 100, 100);
            public static Color PrimaryDark { get; set; } = new Color(40, 40, 40);
            public static Color PrimaryVeryDark { get; set; } = new Color(24, 24, 24);
            public static Color DarkGray { get; set; } = new Color(70, 70, 80);
            public static Color Gray { get; set; } = new Color(130, 130, 130);
            public static Color White { get; set; } = new Color(255, 255, 255);
            public static Color Accent { get; set; } = new Color(140, 48, 138);
        }
    }

    public class CGButton
    {
        public Button FormsControl { get; }

        public CGButton()
        {
            FormsControl = new Button();
            ApplyBevelButton((ButtonVisual)FormsControl.Visual, Styling.Colors.Primary);
            FormsControl.UpdateState();
        }

        internal static void ApplyBevelButton(ButtonVisual visual, Color faceColor)
        {
            var texUp = MainEngine.Instance.Content.Load<Texture2D>("UI/cguibutton_up");
            var texDown = MainEngine.Instance.Content.Load<Texture2D>("UI/cguibutton_down");

            visual.Background.Texture = texUp;
            visual.Background.TextureWidth = 32;
            visual.Background.TextureHeight = 32;
            visual.Background.TextureAddress = Gum.Managers.TextureAddress.EntireTexture;
            visual.TextInstance.BitmapFont = Styling.Default24;

            visual.States.Enabled.Clear();
            visual.States.Enabled.Apply = () =>
            {
                visual.Background.Texture = texUp;
                visual.Background.TextureWidth = 128;
                visual.Background.TextureHeight = 128;
                visual.Background.Color = faceColor;
                visual.TextInstance.Color = Styling.Colors.White;
                visual.FocusedIndicator.Visible = false;
            };
            visual.States.Focused.Clear();
            visual.States.Focused.Apply = () =>
            {
                visual.Background.Texture = texUp;
                visual.Background.TextureWidth = 128;
                visual.Background.TextureHeight = 128;
                visual.Background.Color = faceColor;
                visual.TextInstance.Color = Styling.Colors.White;
                visual.FocusedIndicator.Visible = true;
            };
            visual.States.Highlighted.Clear();
            visual.States.Highlighted.Apply = () =>
            {
                visual.Background.Texture = texUp;
                visual.Background.TextureWidth = 128;
                visual.Background.TextureHeight = 128;
                visual.Background.Color = Styling.Colors.PrimaryLight;
                visual.TextInstance.Color = Styling.Colors.White;
                visual.FocusedIndicator.Visible = false;
            };
            visual.States.HighlightedFocused.Clear();
            visual.States.HighlightedFocused.Apply = () =>
            {
                visual.Background.Texture = texUp;
                visual.Background.TextureWidth = 128;
                visual.Background.TextureHeight = 128;
                visual.Background.Color = Styling.Colors.PrimaryLight;
                visual.TextInstance.Color = Styling.Colors.White;
                visual.FocusedIndicator.Visible = true;
            };
            visual.States.Pushed.Clear();
            visual.States.Pushed.Apply = () =>
            {
                visual.Background.Texture = texDown;
                visual.Background.TextureWidth = 128;
                visual.Background.TextureHeight = 128;
                visual.Background.Color = faceColor;
                visual.TextInstance.Color = Styling.Colors.White;
                visual.FocusedIndicator.Visible = false;
            };
            visual.States.Disabled.Clear();
            visual.States.Disabled.Apply = () =>
            {
                visual.Background.Texture = texUp;
                visual.Background.TextureWidth = 128;
                visual.Background.TextureHeight = 128;
                visual.Background.Color = Styling.Colors.DarkGray;
                visual.TextInstance.Color = Styling.Colors.Gray;
                visual.FocusedIndicator.Visible = false;
            };
            visual.States.DisabledFocused.Clear();
            visual.States.DisabledFocused.Apply = () =>
            {
                visual.Background.Texture = texUp;
                visual.Background.TextureWidth = 128;
                visual.Background.TextureHeight = 128;
                visual.Background.Color = Styling.Colors.DarkGray;
                visual.TextInstance.Color = Styling.Colors.Gray;
                visual.FocusedIndicator.Visible = true;
            };
        }
    }

    public class CGScrollBar
    {
        public ScrollBar FormsControl { get; }

        public CGScrollBar()
        {
            FormsControl = new ScrollBar();
            ApplyStyling(FormsControl);
        }

        public static void ApplyStyling(ScrollBar bar)
        {
            var visual = (ScrollBarVisual)bar.Visual;
            var buttonColor = new Color(45, 45, 45);

            CGButton.ApplyBevelButton((ButtonVisual)visual.UpButtonInstance, buttonColor);
            CGButton.ApplyBevelButton((ButtonVisual)visual.DownButtonInstance, buttonColor);
            CGButton.ApplyBevelButton((ButtonVisual)visual.ThumbInstance, buttonColor);

            visual.TrackInstance.Color = new Color(30, 30, 30);

            bar.UpButton.UpdateState();
            bar.DownButton.UpdateState();

            visual.ThumbInstance.FormsControl.UpdateState();
        }
    }

    public class CGScrollViewer
    {
        public ScrollViewer FormsControl { get; }

        public CGScrollViewer()
        {
            FormsControl = new ScrollViewer();
            var visual = (ScrollViewerVisual)FormsControl.Visual;
            visual.Background.Visible = false;
            CGScrollBar.ApplyStyling(FormsControl.VerticalScrollBar);
        }
    }

    public class CGComboBox
    {
        public ComboBox FormsControl { get; }

        public CGComboBox()
        {
            FormsControl = new ComboBox();
            var visual = (ComboBoxVisual)FormsControl.Visual;

            var tex = MainEngine.Instance.Content.Load<Texture2D>("UI/cguiinput");

            visual.Background.Texture = tex;
            visual.Background.TextureWidth = 32;
            visual.Background.TextureHeight = 32;
            visual.Background.TextureAddress = Gum.Managers.TextureAddress.EntireTexture;

            visual.Background.Color = Styling.Colors.Primary;
            visual.Background.WidthUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent;
            visual.Background.HeightUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent;

            visual.TextInstance.BitmapFont = Styling.Default24;

            visual.DropdownIndicator.Color = Styling.Colors.White;

            visual.States.Disabled.Clear();
            visual.States.Disabled.Apply = () =>
            {
                visual.Background.Color = Styling.Colors.Primary;
                visual.FocusedIndicator.Visible = false;
            };
            visual.States.DisabledFocused.Clear();
            visual.States.DisabledFocused.Apply = () =>
            {
                visual.Background.Color = Styling.Colors.Primary;
                visual.FocusedIndicator.Visible = true;
            };
            visual.States.Enabled.Clear();
            visual.States.Enabled.Apply = () =>
            {
                visual.Background.Color = Styling.Colors.Primary;
                visual.FocusedIndicator.Visible = false;
            };
            visual.States.Focused.Clear();
            visual.States.Focused.Apply = () =>
            {
                visual.Background.Color = Styling.Colors.Primary;
                visual.FocusedIndicator.Visible = true;
            };
            visual.States.Highlighted.Clear();
            visual.States.Highlighted.Apply = () =>
            {
                visual.Background.Color = Styling.Colors.PrimaryLight;
                visual.FocusedIndicator.Visible = false;
            };
            visual.States.HighlightedFocused.Clear();
            visual.States.HighlightedFocused.Apply = () =>
            {
                visual.Background.Color = Styling.Colors.PrimaryLight;
                visual.FocusedIndicator.Visible = true;
            };
            visual.States.Pushed.Clear();
            visual.States.Pushed.Apply = () =>
            {
                visual.Background.Color = Styling.Colors.PrimaryDark;
                visual.FocusedIndicator.Visible = false;
            };

            CGListBox.ApplyStyling(FormsControl.ListBox);

            FormsControl.UpdateState();
        }
    }

    public class CGListBox
    {
        public ListBox FormsControl { get; }

        public CGListBox()
        {
            FormsControl = new ListBox();
            ApplyStyling(FormsControl);
            FormsControl.UpdateState();
        }

        public static void ApplyStyling(ListBox listBox)
        {
            var visual = (ListBoxVisual)listBox.Visual;

            visual.Background.Texture = MainEngine.Instance.Content.Load<Texture2D>("UI/cguiinput");
            visual.Background.TextureAddress = Gum.Managers.TextureAddress.EntireTexture;
            visual.Background.Color = Styling.Colors.PrimaryVeryDark;

            CGScrollBar.ApplyStyling(listBox.VerticalScrollBar);

            listBox.VisualTemplate = new VisualTemplate(() =>
            {
                var item = new ListBoxItemVisual();

                item.Background.Texture = MainEngine.Instance.Content.Load<Texture2D>("UI/cguibutton_up");
                item.Background.TextureAddress = Gum.Managers.TextureAddress.EntireTexture;
                item.TextInstance.BitmapFont = Styling.Default24;

                void AddVariable(StateSave state, string name, object value)
                {
                    state.Variables.Add(new VariableSave
                    {
                        Name = name,
                        Value = value
                    });
                }

                void AddState(StateSave state, bool isBackgroundVisible, bool isFocusedVisible,
                    Color textColor, Color? backgroundColor = null)
                {
                    item.ListBoxItemCategory.States.Add(state);
                    AddVariable(state, "Background.Visible", isBackgroundVisible);
                    AddVariable(state, "FocusedIndicator.Visible", isFocusedVisible);
                    AddVariable(state, "TextInstance.Color", textColor);
                    if (backgroundColor != null)
                    {
                        AddVariable(state, "Background.Color", backgroundColor);
                    }
                }

                AddState(item.States.Enabled, false, false, Styling.Colors.White);
                AddState(item.States.Highlighted, true, false, Styling.Colors.White, Styling.Colors.Primary);
                AddState(item.States.Selected, true, false, Styling.Colors.White, Styling.Colors.Primary);
                AddState(item.States.Focused, false, true, Styling.Colors.White);
                AddState(item.States.Disabled, false, false, Styling.Colors.Gray);

                return item;
            });
        }
    }
    public class CGTextBoxRuntime
    {
        public TextBox FormsControl { get; }

        public CGTextBoxRuntime()
        {
            FormsControl = new TextBox();
            var visual = (TextBoxVisual)FormsControl.Visual;

            visual.PlaceholderTextInstance.Text = "...";

            visual.Background.Texture = MainEngine.Instance.Content.Load<Texture2D>("UI/cguiinput");
            visual.Background.TextureAddress = Gum.Managers.TextureAddress.EntireTexture;

            visual.TextInstance.BitmapFont = Styling.Default24;

            visual.SelectionInstance.Color = Styling.Colors.Accent;
            visual.CaretInstance.Color = new Color(188, 188, 188);

            visual.States.Enabled.Clear();
            visual.States.Enabled.Apply = () =>
            {
                visual.Background.Color = new Color(25, 25, 25);
                visual.TextInstance.Color = Styling.Colors.White;
            };
            visual.States.Highlighted.Clear();
            visual.States.Highlighted.Apply = () =>
            {
                visual.Background.Color = new Color(30, 30, 30);
                visual.TextInstance.Color = Styling.Colors.White;
            };
            visual.States.Focused.Clear();
            visual.States.Focused.Apply = () =>
            {
                visual.Background.Color = new Color(25, 25, 25);
                visual.TextInstance.Color = Styling.Colors.White;
            };
            visual.States.Disabled.Clear();
            visual.States.Disabled.Apply = () =>
            {
                visual.Background.Color = new Color(25, 25, 25);
                visual.TextInstance.Color = Styling.Colors.Gray;
            };

            FormsControl.UpdateState();
        }
    }
}