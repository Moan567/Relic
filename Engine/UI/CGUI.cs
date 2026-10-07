using Gum.Forms.Controls;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGameGum.GueDeriving;
using System;
using System.Collections.Generic;

namespace Engine.UI
{
    public class CGUIRow
    {
        internal readonly ContainerRuntime Root;
        private readonly ContainerRuntime content;
        private readonly int rowHeight;
        private int x;
        private readonly List<(TextRuntime rt, bool fill)> textFills = new();

        internal CGUIRow(int rowHeight)
        {
            this.rowHeight = rowHeight;
            Root = new ContainerRuntime
            {
                Width = 0,
                WidthUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent,
                Height = rowHeight,
                HeightUnits = Gum.DataTypes.DimensionUnitType.Absolute,
            };

            content = new ContainerRuntime
            {
                Width = 0,
                WidthUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent,
                Height = 0,
                HeightUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent,
                ChildrenLayout = Gum.Managers.ChildrenLayout.LeftToRightStack,
            };
            Root.Children.Add(content);
            x = 0;
        }

        public CGUIRow Thumbnail(Texture2D texture, int width = 96)
        {
            if (texture == null) { x += width; return this; }
            var sprite = new SpriteRuntime
            {
                Texture = texture,
                TextureAddress = Gum.Managers.TextureAddress.EntireTexture,
                Width = width,
                WidthUnits = Gum.DataTypes.DimensionUnitType.Absolute,
                Height = rowHeight,
                HeightUnits = Gum.DataTypes.DimensionUnitType.Absolute,
            };
            content.Children.Add(sprite);
            x += width;
            return this;
        }

        public CGUIRow Text(string primary, string sub = null, int width = 0, int padding = 6)
        {
            bool fill = width == 0;

            var col = new ContainerRuntime
            {
                Height = rowHeight,
                HeightUnits = Gum.DataTypes.DimensionUnitType.Absolute,
                Width = fill ? 1 : width - padding,
                WidthUnits = fill ? Gum.DataTypes.DimensionUnitType.Ratio
                                   : Gum.DataTypes.DimensionUnitType.Absolute,
            };

            col.Children.Add(new TextRuntime
            {
                Text = primary,
                Y = sub != null ? 4 : 0,
                YOrigin = sub != null ? RenderingLibrary.Graphics.VerticalAlignment.Top
                                             : RenderingLibrary.Graphics.VerticalAlignment.Center,
                YUnits = sub != null ? Gum.Converters.GeneralUnitType.PixelsFromSmall
                                             : Gum.Converters.GeneralUnitType.PixelsFromMiddle,
                Width = 0,
                WidthUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent,
                UseCustomFont = true,
                CustomFontFile = "Fonts/default.fnt",
            });

            if (sub != null)
            {
                col.Children.Add(new TextRuntime
                {
                    Text = sub,
                    YOrigin = RenderingLibrary.Graphics.VerticalAlignment.Bottom,
                    YUnits = Gum.Converters.GeneralUnitType.PixelsFromLarge,
                    Y = -4,
                    Width = 0,
                    WidthUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent,
                    UseCustomFont = true,
                    CustomFontFile = "Fonts/default.fnt",
                });
            }

            content.Children.Add(col);
            if (!fill) x += width;
            return this;
        }
    }
    public class CGUISelectableList<T>
    {
        public T Selected { get; private set; }
        public event Action<T> SelectionChanged;
        public event Action<T> DoubleClicked;

        internal void NotifySelected(T item)
        {
            Selected = item;
            SelectionChanged?.Invoke(item);
        }

        internal void NotifyDoubleClicked(T item) => DoubleClicked?.Invoke(item);
    }

    public class CGUI
    {
        private static readonly List<Action> deferred = new();

        public static void Flush()
        {
            if (deferred.Count == 0) return;
            var copy = new List<Action>(deferred);
            deferred.Clear();
            foreach (var action in copy) action();
        }

        private static void Defer(Action action) => deferred.Add(action);

        protected CGWindow window;
        protected GraphicalUiElement container;
        protected int y;
        protected readonly int padding;
        protected readonly int rowHeight;
        protected int innerWidth;
        private int windowInnerHeight;

        private const int DefaultPadding = 8;
        private const int DefaultRowHeight = 28;

        protected CGUI() { }

        private CGUI(CGWindow win, int padding, int rowHeight)
        {
            window = win;
            container = win.InnerPanel;
            this.padding = padding;
            this.rowHeight = rowHeight;
            innerWidth = (int)win.Width - padding * 2;
            // InnerPanel is Height = -48 relative to window (see CGWindow)
            windowInnerHeight = (int)win.Height - 48;
            y = padding;
        }

        public static CGUI Window(string title, int width = 400, int height = 300,
            int padding = DefaultPadding, int rowHeight = DefaultRowHeight)
        {
            var win = new CGWindow(title);
            win.Width = width;
            win.Height = height;
            win.MinWidth = width;
            win.MinHeight = height;
            win.Anchor(Anchor.Center);
            return new CGUI(win, padding, rowHeight);
        }

        public CGWindow Build()
        {
            window.Open();
            return window;
        }

        protected void Place(GraphicalUiElement element, int height, bool stretch = true)
        {
            element.Anchor(Anchor.TopLeft);
            element.X = padding;
            element.Y = y;
            if (stretch)
            {
                element.Width = innerWidth;
                element.WidthUnits = Gum.DataTypes.DimensionUnitType.Absolute;
            }
            element.Height = height;
            element.HeightUnits = Gum.DataTypes.DimensionUnitType.Absolute;
            container.Children.Add(element);
            y += height + padding;
        }

        protected void PlaceControl(FrameworkElement control, int height, bool stretch = true)
            => Place(control.Visual, height, stretch);

        public CGUI Label(string text, int height = 20)
        {
            var lbl = new Gum.Forms.Controls.Label();
            lbl.Text = text;
            (lbl.Visual as TextRuntime).BitmapFont = Styling.Default24;
            lbl.UpdateState();
            PlaceControl(lbl, height);
            return this;
        }

        public CGUI TextBox(out TextBox result, string placeholder = "", int height = 28)
        {
            var tb = new CGTextBoxRuntime();
            tb.FormsControl.Text = placeholder;
            PlaceControl(tb.FormsControl, height);
            result = tb.FormsControl;
            return this;
        }

        public CGUI List(IEnumerable<string> items, out ListBox result, int height = 120)
        {
            var lb = new CGListBox();
            foreach (var item in items)
                lb.FormsControl.Items.Add(item);
            PlaceControl(lb.FormsControl, height);
            result = lb.FormsControl;
            return this;
        }

        public CGUI List(out ListBox result, int height = 120)
            => List(Array.Empty<string>(), out result, height);

        public CGUI Dropdown(IEnumerable<string> items, out ComboBox result, int height = 28)
        {
            var cb = new CGComboBox();
            foreach (var item in items)
                cb.FormsControl.Items.Add(item);
            PlaceControl(cb.FormsControl, height);
            result = cb.FormsControl;
            return this;
        }

        public CGUI Separator(int thickness = 1)
        {
            var line = new ColoredRectangleRuntime
            {
                Color = new Color(80, 80, 80),
                Height = thickness,
                HeightUnits = Gum.DataTypes.DimensionUnitType.Absolute,
            };
            Place(line, thickness);
            return this;
        }

        public CGUI Space(int pixels = 8)
        {
            y += pixels;
            return this;
        }

        public CGUI Buttons(params (string label, Action<CGWindow> onClick)[] buttons)
        {
            if (buttons.Length == 0) return this;

            int gap = padding;
            int btnWidth = (innerWidth - gap * (buttons.Length - 1)) / buttons.Length;
            int x = padding;

            foreach (var (label, onClick) in buttons)
            {
                var btn = new CGButton();
                btn.FormsControl.Text = label;

                var visual = btn.FormsControl.Visual;
                visual.Anchor(Anchor.TopLeft);
                visual.X = x;
                visual.Y = y;
                visual.Width = btnWidth;
                visual.WidthUnits = Gum.DataTypes.DimensionUnitType.Absolute;
                visual.Height = rowHeight;
                visual.HeightUnits = Gum.DataTypes.DimensionUnitType.Absolute;

                var capturedAction = onClick;
                var capturedWindow = window;
                btn.FormsControl.Click += (s, e) =>
                {
                    if (capturedAction != null) Defer(()=> capturedAction(window));
                    else Defer(window.Close);
                };

                container.Children.Add(visual);
                x += btnWidth + gap;
            }

            y += rowHeight + padding;
            return this;
        }

        public CGUI Button(string label, Action<CGWindow> onClick) => Buttons((label, onClick));

        public CGUI Image(Texture2D texture, int? height = null)
        {
            int h = height ?? texture.Height;
            var sprite = new SpriteRuntime
            {
                Texture = texture,
                TextureAddress = Gum.Managers.TextureAddress.EntireTexture,
                WidthUnits = Gum.DataTypes.DimensionUnitType.Absolute,
                HeightUnits = Gum.DataTypes.DimensionUnitType.Absolute,
                Width = innerWidth,
                Height = h,
            };
            Place(sprite, h);
            return this;
        }

        public CGUI Panel(int height, Action<CGUI> build, int baseColor = 35)
        {
            var panel = new CGDarkPanel(baseColor);
            panel.WidthUnits = Gum.DataTypes.DimensionUnitType.Absolute;
            panel.HeightUnits = Gum.DataTypes.DimensionUnitType.Absolute;
            panel.Width = innerWidth;
            panel.Height = height;
            container.Children.Add(panel);

            var sub = new CGUI();
            sub.window = window;
            sub.container = panel;
            sub.innerWidth = innerWidth - padding * 2;
            sub.y = padding;
            build(sub);

            y += height + padding;
            return this;
        }

        public CGUI ScrollPanel(int height, Action<CGUI> build)
        {
            var viewer = new CGScrollViewer();
            PlaceControl(viewer.FormsControl, height);

            var sub = new CGUI();
            sub.window = window;
            sub.container = viewer.FormsControl.InnerPanel;
            sub.innerWidth = innerWidth - padding * 2;
            sub.y = 0;
            build(sub);

            return this;
        }

        /// <summary>
        /// A scrollable list of custom-templated rows that fills all available
        /// vertical space, automatically leaving room for a BottomBar if one
        /// follows. Each item is rendered using the provided rowBuilder.
        /// </summary>

        public CGUI FillList<T>(
            IEnumerable<T> items,
            Action<CGUIRow, T> rowBuilder,
            out CGUISelectableList<T> result,
            int itemHeight = 64,
            int bottomReserve = 0)
        {
            // Reserve = caller's bottomReserve, or default one BottomBar height
            int reserve = bottomReserve > 0
                ? bottomReserve
                : rowHeight + padding * 2 + padding;

            int topOffset = y;
            int availableH = windowInnerHeight - topOffset - reserve;

            var scroller = new CGScrollViewer().FormsControl;
            scroller.Visual.Anchor(Anchor.TopLeft);
            scroller.Visual.X = padding;
            scroller.Visual.Y = topOffset;
            scroller.Visual.Width = -(padding * 2);
            scroller.Visual.WidthUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent;
            scroller.Visual.Height = -(topOffset + reserve + padding);
            scroller.Visual.HeightUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent;
            scroller.InnerPanel.StackSpacing = 1;

            var bg = new Panel(new CGDarkPanel());
            bg.Visual.Anchor(Anchor.TopLeft);
            bg.Visual.X = padding;
            bg.Visual.Y = topOffset;
            bg.Visual.Width = -(padding * 2);
            bg.Visual.WidthUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent;
            bg.Visual.Height = -(topOffset + reserve + padding);
            bg.Visual.HeightUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent;

            container.Children.Add(bg.Visual);
            container.Children.Add(scroller.Visual);
            y += (int)(windowInnerHeight - topOffset - reserve) + padding;

            var handle = new CGUISelectableList<T>();
            result = handle;

            ContainerRuntime selectedRow = null;

            foreach (var item in items)
            {
                var row = new CGUIRow(itemHeight);
                rowBuilder(row, item);

                var rowRoot = row.Root;

                // Background highlight rect (hidden by default)
                var highlight = new ColoredRectangleRuntime
                {
                    Name = "RowHighlight",
                    Color = Styling.Colors.Primary,
                    Width = 0,
                    WidthUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent,
                    Height = 0,
                    HeightUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent,
                    Visible = false,
                };
                rowRoot.Children.Insert(0, highlight);

                var capturedItem = item;
                var capturedHandle = handle;
                double lastClick = 0;

                rowRoot.Click += (s, e) =>
                {
                    // Deselect previous
                    if (selectedRow != null)
                    {
                        var prev = selectedRow.GetChildByName("RowHighlight") as ColoredRectangleRuntime;
                        if (prev != null) prev.Visible = false;
                    }

                    selectedRow = rowRoot;
                    highlight.Visible = true;
                    capturedHandle.NotifySelected(capturedItem);

                    // Double-click detection (~300ms)
                    double now = Environment.TickCount64;
                    if (now - lastClick < 300)
                        capturedHandle.NotifyDoubleClicked(capturedItem);
                    lastClick = now;
                };

                scroller.InnerPanel.AddChild(rowRoot);
            }

            return this;
        }

        /// <summary>
        /// A row of buttons anchored to the bottom of the window, outside the
        /// normal stacker flow.
        /// </summary>
        public CGUI BottomBar(params (string label, Action<CGWindow> onClick)[] buttons)
        {
            if (buttons.Length == 0) return this;

            var bar = new ContainerRuntime();
            bar.Anchor(Anchor.BottomLeft);
            bar.Y = -padding;
            bar.Width = 0;
            bar.WidthUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent;
            bar.Height = rowHeight;
            bar.HeightUnits = Gum.DataTypes.DimensionUnitType.Absolute;
            bar.ChildrenLayout = Gum.Managers.ChildrenLayout.LeftToRightStack;
            bar.StackSpacing = padding;
            container.Children.Add(bar);

            foreach (var (label, onClick) in buttons)
            {
                var btn = new CGButton();
                btn.FormsControl.Text = label;

                var visual = btn.FormsControl.Visual;
                visual.Width = 1;
                visual.WidthUnits = Gum.DataTypes.DimensionUnitType.Ratio;
                visual.Height = rowHeight;
                visual.HeightUnits = Gum.DataTypes.DimensionUnitType.Absolute;

                var capturedAction = onClick;
                var capturedWindow = window;
                btn.FormsControl.Click += (s, e) =>
                {
                    if (capturedAction != null) Defer(()=>capturedAction(window));
                    else Defer(window.Close);
                };

                bar.Children.Add(visual);
            }

            return this;
        }
    }
}