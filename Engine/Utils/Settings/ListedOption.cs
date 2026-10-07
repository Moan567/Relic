using Engine.Input;
using Engine.UI;
using Gum.Wireframe;
using MonoGameGum.GueDeriving;
using System;

namespace Engine.Utils.Settings
{
    public abstract class ListedOption
    {
        public string OptionLabel { get; set; } = string.Empty;
        public string GameOption { get; set; }

        public abstract void Build(Gum.Forms.Controls.FrameworkElement tabContent, int yOffset);

        public abstract void Sync();

        protected static Gum.Forms.Controls.Label CreateRowLabel(string text, int x, int y)
        {
            var lbl = new Gum.Forms.Controls.Label();
            lbl.Text = text;
            lbl.Anchor(Anchor.TopLeft);
            lbl.X = x;
            lbl.Y = y;
            (lbl.Visual as TextRuntime).BitmapFont = Styling.Default24;
            lbl.UpdateState();
            return lbl;
        }
    }

    public class ComboBoxOption : ListedOption
    {
        public string[] Items { get; set; } = Array.Empty<string>();
        public Func<int> GetCurrentIndex { get; set; }
        public Action<int> OnSelectionChanged { get; set; }

        private Gum.Forms.Controls.ComboBox comboBox;
        private bool ignore;

        public override void Build(Gum.Forms.Controls.FrameworkElement tabContent, int yOffset)
        {
            tabContent.AddChild(CreateRowLabel(OptionLabel, 16, yOffset));

            comboBox = new CGComboBox().FormsControl;
            comboBox.Anchor(Anchor.TopRight);
            comboBox.Y = yOffset;
            comboBox.X = -16;

            foreach (var item in Items)
                comboBox.Items.Add(item);

            comboBox.SelectedIndex = GetCurrentIndex?.Invoke() ?? 0;

            var box = comboBox;
            box.SelectionChanged += (s, e) =>
            {
                if (ignore) return;
                OnSelectionChanged?.Invoke(box.SelectedIndex);
            };

            tabContent.AddChild(comboBox);
        }

        public override void Sync()
        {
            ignore = true;
            if (comboBox != null)
            {
                comboBox.SelectedIndex = GetCurrentIndex?.Invoke() ?? 0;
            }
            ignore = false;
        }
    }

    public class ToggleOption : ListedOption
    {
        public string OnLabel { get; set; } = "On";
        public string OffLabel { get; set; } = "Off";
        public Func<bool> GetCurrentValue { get; set; }
        public Action<bool> OnChanged { get; set; }

        private Gum.Forms.Controls.ComboBox comboBox;
        private bool ignore;

        public override void Build(Gum.Forms.Controls.FrameworkElement tabContent, int yOffset)
        {
            tabContent.AddChild(CreateRowLabel(OptionLabel, 16, yOffset));

            comboBox = new CGComboBox().FormsControl;
            comboBox.Anchor(Anchor.TopRight);
            comboBox.Y = yOffset;
            comboBox.X = -16;
            comboBox.Items.Add(OnLabel);
            comboBox.Items.Add(OffLabel);
            comboBox.SelectedIndex = GetCurrentValue?.Invoke() ?? false ? 0 : 1;

            var box = comboBox;
            box.SelectionChanged += (s, e) =>
            {
                if (ignore) return;
                OnChanged?.Invoke(box.SelectedIndex == 0);
            };

            tabContent.AddChild(comboBox);
        }

        public override void Sync()
        {
            ignore = true;
            if (comboBox != null)
                comboBox.SelectedIndex = GetCurrentValue?.Invoke() ?? false ? 0 : 1;
            ignore = false;
        }
    }

    public class SliderOption : ListedOption
    {
        public double Min { get; set; } = 0;
        public double Max { get; set; } = 100;
        public double SmallChange { get; set; } = 1;

        /// <summary>Returns the current value for Sync.</summary>
        public Func<double> GetCurrentValue { get; set; }

        /// <summary>Called with the new value when the slider moves.</summary>
        public Action<double> OnChanged { get; set; }

        /// <summary>Formats the value for the right-hand label. Defaults to integer display.</summary>
        public Func<double, string> FormatValue { get; set; } = v => ((int)v).ToString();

        private const int ValueLabelWidth = 52;

        private Gum.Forms.Controls.ScrollBar scrollBar;
        private Gum.Forms.Controls.Label valueLabel;
        private bool ignore;

        public override void Build(Gum.Forms.Controls.FrameworkElement tabContent, int yOffset)
        {
            tabContent.AddChild(CreateRowLabel(OptionLabel, 16, yOffset));

            valueLabel = new Gum.Forms.Controls.Label();
            valueLabel.Anchor(Anchor.TopRight);
            valueLabel.Y = yOffset;
            valueLabel.X = -16;
            valueLabel.Width = ValueLabelWidth;

            scrollBar = new CGScrollBar().FormsControl;

            scrollBar.Orientation = Gum.Forms.Controls.Orientation.Horizontal;
            scrollBar.Anchor(Anchor.TopRight);
            scrollBar.Y = yOffset;
            scrollBar.X = -(ValueLabelWidth + 24);
            scrollBar.Minimum = Min;
            scrollBar.Maximum = Max;
            scrollBar.SmallChange = SmallChange;
            scrollBar.Value = GetCurrentValue?.Invoke() ?? Min;

            valueLabel.Text = FormatValue(scrollBar.Value);

            var bar = scrollBar;
            var label = valueLabel;
            (label.Visual as TextRuntime).BitmapFont = Styling.Default24;
            label.UpdateState();
            bar.ValueChanged += (s, e) =>
            {
                if (ignore) return;

                OnChanged?.Invoke(bar.Value);
                label.Text = FormatValue(bar.Value);
            };

            tabContent.AddChild(scrollBar);
            tabContent.AddChild(valueLabel);
        }

        public override void Sync()
        {
            ignore = true;
            if (scrollBar == null || valueLabel == null) return;
            scrollBar.Value = GetCurrentValue?.Invoke() ?? Min;
            valueLabel.Text = FormatValue(scrollBar.Value);
            ignore = false;
        }
    }

    public class SectionHeaderOption : ListedOption
    {
        public SectionHeaderOption(string headerText)
        {
            OptionLabel = headerText;
        }

        public override void Build(Gum.Forms.Controls.FrameworkElement tabContent, int yOffset)
        {
            var header = new Gum.Forms.Controls.Label();
            header.Text = OptionLabel.ToUpper();
            header.Anchor(Anchor.TopLeft);
            header.X = 16;
            header.Y = yOffset;
            tabContent.AddChild(header);
        }

        /// <summary>Nothing to sync for a static header.</summary>
        public override void Sync() { }
    }
    public class KeybindOption : ListedOption
    {
        private readonly InputBinding binding;
        private Gum.Forms.Controls.Button primaryBtn;
        private Gum.Forms.Controls.Button secondaryBtn;

        public KeybindOption(InputBinding binding)
        {
            this.binding = binding;
            OptionLabel = binding.DisplayName;
        }

        public override void Build(Gum.Forms.Controls.FrameworkElement tabContent, int yOffset)
        {
            tabContent.AddChild(CreateRowLabel(binding.DisplayName, 16, yOffset));

            var primaryString = FormatBinding(binding.GetPrimary());

            primaryBtn = new CGButton().FormsControl;
            primaryBtn.Anchor(Anchor.TopRight);
            primaryBtn.Y = yOffset;
            primaryBtn.X = -164;
            primaryBtn.Width = 140;
            primaryBtn.Text = primaryString;

            if (primaryString == "N/A") 
                primaryBtn.IsEnabled = false;
            else
                primaryBtn.Click += (s, e) => BeginCapture(isPrimary: true);

            tabContent.AddChild(primaryBtn);

            var secondaryString = FormatBinding(binding.GetSecondary());

            secondaryBtn = new CGButton().FormsControl;
            secondaryBtn.Anchor(Anchor.TopRight);
            secondaryBtn.Y = yOffset;
            secondaryBtn.X = -16;
            secondaryBtn.Width = 140;
            secondaryBtn.Text = secondaryString;

            if (secondaryString == "N/A")
                secondaryBtn.IsEnabled = false;
            else
                secondaryBtn.Click += (s, e) => BeginCapture(isPrimary: false);

            tabContent.AddChild(secondaryBtn);
        }

        private void BeginCapture(bool isPrimary)
        {
            var targetBtn = isPrimary ? primaryBtn : secondaryBtn;
            string originalText = targetBtn.Text;
            targetBtn.Text = "[press key]";
            targetBtn.IsEnabled = false;

            InputCapture.StartCapture(
                onCaptured: newBinding =>
                {
                    binding.Rebind(newBinding, isPrimary);
                    InputRegistry.Save();
                    Sync();
                },
                onCancelled: () =>
                {
                    targetBtn.Text = originalText;
                    targetBtn.IsEnabled = true;
                });
        }

        public override void Sync()
        {
            if (primaryBtn != null)
            {
                primaryBtn.Text = FormatBinding(binding.GetPrimary());
                primaryBtn.IsEnabled = true;
            }
            if (secondaryBtn != null)
            {
                secondaryBtn.Text = FormatBinding(binding.GetSecondary());
                secondaryBtn.IsEnabled = true;
            }
        }

        private static string FormatBinding(SingleInputBinding b) => b switch
        {
            BoundKey k => k.Key.ToString(),
            BoundMouseButton m => m.MouseButton.ToString() + " Click",
            BoundGamepadButton g => $"GP: {g.GamepadButton.ToString()}",
            null => "---",
            _ => "N/A"
        };
    }
}