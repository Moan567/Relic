using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Relic.Models;
using Relic.Particles;
using Relic.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using AvColor = Avalonia.Media.Color;
using AvBrush = Avalonia.Media.SolidColorBrush;
using AvFontWeight = Avalonia.Media.FontWeight;
using Color = Microsoft.Xna.Framework.Color;
using Vector3 = Microsoft.Xna.Framework.Vector3;

namespace Rockwall2.Editor.Common;

public class ParticleFieldPanel : StackPanel
{
    public event Action Changed;

    private object target;
    private readonly Dictionary<string, Control> valueControlsByMember = new();

    public void Bind(object instance)
    {
        target = instance;
        Children.Clear();
        valueControlsByMember.Clear();

        if (target == null) return;

        var members = CollectMembers(target.GetType());
        var groups = members
            .GroupBy(m => m.Attribute.Group)
            .OrderBy(g => g.Key == "General" ? 0 : 1)
            .ThenBy(g => g.Key);

        foreach (var group in groups)
        {
            Children.Add(BuildGroup(group.Key, group.OrderBy(m => m.Attribute.Order).ToList()));
        }

        Refresh();
    }

    public void Refresh()
    {
        if (target == null) return;
        foreach (var member in CollectMembers(target.GetType()))
        {
            if (valueControlsByMember.TryGetValue(member.Name, out var control))
            {
                PushToControl(member, control);
            }
        }
    }

    private static List<MemberAccessor> CollectMembers(Type type)
    {
        var result = new List<MemberAccessor>();

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = field.GetCustomAttribute<EditorFieldAttribute>();
            if (attr != null)
            {
                result.Add(new MemberAccessor(field, attr));
            }
        }

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = prop.GetCustomAttribute<EditorFieldAttribute>();
            if (attr != null)
            {
                result.Add(new MemberAccessor(prop, attr));
            }
        }

        return result;
    }

    private Border BuildGroup(string groupName, List<MemberAccessor> members)
    {
        var stack = new StackPanel();
        var body = new StackPanel { Margin = new Thickness(3), Spacing = 1 };

        foreach (var member in members)
        {
            var row = BuildRow(member);
            if (row != null)
            {
                body.Children.Add(row);
            }
        }

        stack.Children.Add(new TextBlock
        {
            Text = groupName,
            FontSize = 10,
            FontWeight = AvFontWeight.SemiBold,
            Background = new AvBrush(AvColor.FromRgb(0x25, 0x25, 0x25)),
            Padding = new Thickness(4, 2)
        });
        stack.Children.Add(body);

        return new Border
        {
            BorderBrush = new AvBrush(AvColor.FromRgb(0x30, 0x30, 0x30)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 3, 0, 0),
            Child = stack
        };
    }

    private Control BuildRow(MemberAccessor member)
    {
        Control valueControl = BuildValueControl(member);
        if (valueControl == null) return null;

        valueControlsByMember[member.Name] = valueControl;

        if (valueControl is StackPanel { Tag: "stacked" })
        {
            var block = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            block.Children.Add(new Label
            {
                Content = (member.Attribute.Label ?? member.Name) + ":",
                Padding = new Thickness(0)
            });
            block.Children.Add(valueControl);
            return block;
        }

        var row = new Grid();
        bool isColorField = valueControl.Classes.Contains("colorField");
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.ColumnDefinitions.Add(new ColumnDefinition(isColorField ? GridLength.Auto : GridLength.Star));

        var label = new Label
        {
            Content = (member.Attribute.Label ?? member.Name) + ":",
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);
        Grid.SetColumn(valueControl, 1);

        row.Children.Add(label);
        row.Children.Add(valueControl);
        return row;
    }

    private Control BuildValueControl(MemberAccessor member)
    {
        Type t = member.MemberType;

        if (t == typeof(bool))
        {
            var box = new CheckBox { HorizontalAlignment = HorizontalAlignment.Right };
            box.IsCheckedChanged += (s, e) =>
            {
                if (box.Tag as string == "loading") return;
                member.Set(target, box.IsChecked == true);
                Changed?.Invoke();
            };
            return box;
        }

        if (t.IsEnum)
        {
            var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var name in Enum.GetNames(t))
            {
                box.Items.Add(name);
            }
            box.SelectionChanged += (s, e) =>
            {
                if (box.Tag as string == "loading") return;
                if (box.SelectedItem is string name)
                {
                    member.Set(target, Enum.Parse(t, name));
                    Changed?.Invoke();
                }
            };
            return box;
        }

        if (t == typeof(float) || t == typeof(int) || t == typeof(ushort) || t == typeof(string))
        {
            var text = new TextBox();
            text.TextChanged += (s, e) =>
            {
                if (text.Tag as string == "loading") return;
                object parsed = ParseScalar(t, text.Text);
                if (parsed != null)
                {
                    member.Set(target, parsed);
                    Changed?.Invoke();
                }
            };
            return text;
        }

        if (t == typeof(Vector3))
        {
            return BuildVector3Control(v => member.Set(target, v));
        }

        if (t == typeof(Color))
        {
            return BuildColorControl(c => member.Set(target, c));
        }

        if (t == typeof(ValueRange<float>))
        {
            return BuildFloatRangeControl(
                min => member.Set(target, new ValueRange<float> { min = min, max = ((ValueRange<float>)member.Get(target)).max }),
                max => member.Set(target, new ValueRange<float> { min = ((ValueRange<float>)member.Get(target)).min, max = max }));
        }

        if (t == typeof(ValueRange<Vector3>))
        {
            return BuildVector3RangeControl(
                min => member.Set(target, new ValueRange<Vector3> { min = min, max = ((ValueRange<Vector3>)member.Get(target)).max }),
                max => member.Set(target, new ValueRange<Vector3> { min = ((ValueRange<Vector3>)member.Get(target)).min, max = max }));
        }

        if (t == typeof(ValueRange<Color>))
        {
            return BuildColorRangeControl(
                min => member.Set(target, new ValueRange<Color> { min = min, max = ((ValueRange<Color>)member.Get(target)).max }),
                max => member.Set(target, new ValueRange<Color> { min = ((ValueRange<Color>)member.Get(target)).min, max = max }));
        }

        if (t == typeof(Curve3))
        {
            var editor = new Curve3Editor { Maximum = ResolveMax(member.Attribute) };
            editor.ValueChanged += (s, curve) =>
            {
                member.Set(target, curve);
                Changed?.Invoke();
            };
            return editor;
        }

        return null;
    }

    private static double ResolveMax(EditorFieldAttribute attr)
        => float.IsNaN(attr.Max) ? 1.0 : attr.Max;

    private static object ParseScalar(Type t, string text)
    {
        if (t == typeof(string)) return text ?? "";
        if (t == typeof(float)) return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : (object)null;
        if (t == typeof(int)) return int.TryParse(text, out int i) ? i : (object)null;
        if (t == typeof(ushort)) return ushort.TryParse(text, out ushort u) ? u : (object)null;
        return null;
    }

    private Control BuildVector3Control(Action<Vector3> onChanged)
    {
        var text = new TextBox();
        text.TextChanged += (s, e) =>
        {
            if (text.Tag as string == "loading") return;
            if (TryParseVec3(text.Text, out var v))
            {
                onChanged(v);
                Changed?.Invoke();
            }
        };
        return text;
    }

    private Grid BuildColorControl(Action<Color> onChanged)
    {
        var grid = new Grid() { HorizontalAlignment = HorizontalAlignment.Right };
        grid.Classes.Add("colorField");
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var picker = new ColorPicker
        {
            Width = 26,
            Height = 20,
            Margin = new Thickness(0, 0, 4, 0)
        };

        var triple = new TextBox { Watermark = "r, g, b", Width = 80 };

        picker.ColorChanged += (s, e) =>
        {
            if (picker.Tag as string == "loading") return;
            var c = ToXna(e.NewColor);
            triple.Tag = "loading";
            triple.Text = ColorToTriple(c);
            triple.Tag = null;
            onChanged(c);
            Changed?.Invoke();
        };

        triple.TextChanged += (s, e) =>
        {
            if (triple.Tag as string == "loading") return;
            if (TryParseColorTriple(triple.Text, out var c))
            {
                picker.Tag = "loading";
                picker.Color = ToAvalonia(c);
                picker.Tag = null;
                onChanged(c);
                Changed?.Invoke();
            }
        };

        Grid.SetColumn(picker, 0);
        Grid.SetColumn(triple, 1);
        grid.Children.Add(picker);
        grid.Children.Add(triple);
        return grid;
    }

    private Control BuildFloatRangeControl(Action<float> onMin, Action<float> onMax)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        var minBox = new TextBox { Watermark = "min", Margin = new Thickness(0, 0, 2, 0) };
        var maxBox = new TextBox { Watermark = "max", Margin = new Thickness(2, 0, 0, 0) };

        minBox.TextChanged += (s, e) =>
        {
            if (minBox.Tag as string == "loading") return;
            if (float.TryParse(minBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            {
                onMin(v);
                Changed?.Invoke();
            }
        };
        maxBox.TextChanged += (s, e) =>
        {
            if (maxBox.Tag as string == "loading") return;
            if (float.TryParse(maxBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            {
                onMax(v);
                Changed?.Invoke();
            }
        };

        Grid.SetColumn(minBox, 0);
        Grid.SetColumn(maxBox, 1);
        grid.Children.Add(minBox);
        grid.Children.Add(maxBox);
        return grid;
    }

    private Control BuildVector3RangeControl(Action<Vector3> onMin, Action<Vector3> onMax)
    {
        var stack = new StackPanel { Spacing = 2 };
        var minBox = new TextBox { Watermark = "min x, y, z" };
        var maxBox = new TextBox { Watermark = "max x, y, z" };

        minBox.TextChanged += (s, e) =>
        {
            if (minBox.Tag as string == "loading") return;
            if (TryParseVec3(minBox.Text, out var v))
            {
                onMin(v);
                Changed?.Invoke();
            }
        };
        maxBox.TextChanged += (s, e) =>
        {
            if (maxBox.Tag as string == "loading") return;
            if (TryParseVec3(maxBox.Text, out var v))
            {
                onMax(v);
                Changed?.Invoke();
            }
        };

        stack.Children.Add(minBox);
        stack.Children.Add(maxBox);
        return stack;
    }

    private StackPanel BuildColorRangeControl(Action<Color> onMin, Action<Color> onMax)
    {
        var stack = new StackPanel { Spacing = 2, Tag = "stacked" };

        var minRow = new StackPanel { Spacing = 1 };
        minRow.Children.Add(new TextBlock { Text = "Min", FontSize = 9, Foreground = new AvBrush(AvColor.FromRgb(0x88, 0x88, 0x88)) });
        minRow.Children.Add(BuildColorControl(onMin));

        var maxRow = new StackPanel { Spacing = 1 };
        maxRow.Children.Add(new TextBlock { Text = "Max", FontSize = 9, Foreground = new AvBrush(AvColor.FromRgb(0x88, 0x88, 0x88)) });
        maxRow.Children.Add(BuildColorControl(onMax));

        stack.Children.Add(minRow);
        stack.Children.Add(maxRow);
        return stack;
    }

    private void PushToControl(MemberAccessor member, Control control)
    {
        control.Tag = "loading";
        object value = member.Get(target);

        switch (control)
        {
            case CheckBox cb:
                cb.IsChecked = value is bool b && b;
                break;
            case ComboBox combo when member.MemberType.IsEnum:
                combo.SelectedItem = value?.ToString();
                break;
            case TextBox tb when member.MemberType == typeof(Vector3):
                tb.Text = Vec3ToString((Vector3)value);
                break;
            case TextBox tb:
                tb.Text = value?.ToString() ?? "";
                break;
            case Curve3Editor ce:
                ce.LoadCurve((Curve3)value);
                break;
            case Grid g when g.Classes.Contains("colorField"):
                PushColorGrid(g, (Color)value);
                break;
            case Grid g when member.MemberType == typeof(ValueRange<float>):
                var fr = (ValueRange<float>)value;
                ((TextBox)g.Children[0]).Text = fr.min.ToString(CultureInfo.InvariantCulture);
                ((TextBox)g.Children[1]).Text = fr.max.ToString(CultureInfo.InvariantCulture);
                break;
            case StackPanel sp when member.MemberType == typeof(ValueRange<Vector3>):
                var vr = (ValueRange<Vector3>)value;
                ((TextBox)sp.Children[0]).Text = Vec3ToString(vr.min);
                ((TextBox)sp.Children[1]).Text = Vec3ToString(vr.max);
                break;
            case StackPanel sp2 when member.MemberType == typeof(ValueRange<Color>):
                var cr = (ValueRange<Color>)value;
                PushColorGrid((Grid)((StackPanel)sp2.Children[0]).Children[1], cr.min);
                PushColorGrid((Grid)((StackPanel)sp2.Children[1]).Children[1], cr.max);
                break;
        }

        control.Tag = null;
    }

    private static void PushColorGrid(Grid g, Color c)
    {
        var picker = (ColorPicker)g.Children[0];
        var triple = (TextBox)g.Children[1];

        picker.Tag = "loading";
        picker.Color = ToAvalonia(c);
        picker.Tag = null;

        triple.Tag = "loading";
        triple.Text = ColorToTriple(c);
        triple.Tag = null;
    }

    private static AvColor ToAvalonia(Color c) => AvColor.FromArgb(c.A, c.R, c.G, c.B);
    private static Color ToXna(AvColor c) => new Color(c.R, c.G, c.B, c.A);
    private static string ColorToTriple(Color c) => $"{c.R}, {c.G}, {c.B}";

    private static bool TryParseColorTriple(string s, out Color result)
    {
        result = Color.White;
        if (s == null) return false;
        var parts = s.Split(',');
        if (parts.Length < 3) return false;
        if (!byte.TryParse(parts[0].Trim(), out byte r)) return false;
        if (!byte.TryParse(parts[1].Trim(), out byte g)) return false;
        if (!byte.TryParse(parts[2].Trim(), out byte b)) return false;
        byte a = 255;
        if (parts.Length >= 4 && byte.TryParse(parts[3].Trim(), out byte parsedA)) a = parsedA;
        result = new Color(r, g, b, a);
        return true;
    }

    private static string Vec3ToString(Vector3 v)
        => $"{v.X.ToString(CultureInfo.InvariantCulture)}, {v.Y.ToString(CultureInfo.InvariantCulture)}, {v.Z.ToString(CultureInfo.InvariantCulture)}";

    private static bool TryParseVec3(string s, out Vector3 result)
    {
        result = Vector3.Zero;
        if (s == null) return false;
        var parts = s.Split(',');
        if (parts.Length < 3) return false;
        if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return false;
        if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return false;
        if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return false;
        result = new Vector3(x, y, z);
        return true;
    }
}