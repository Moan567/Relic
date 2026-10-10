using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Relic.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Rockwall2.Editor.Common;

public class PropertyEditorPanel : StackPanel
{
    private object target;
    private readonly Dictionary<string, Control> controlsByProperty = new();
    private readonly Dictionary<string, Control> rowsByProperty = new();
    private readonly Dictionary<string, Label> labelsByProperty = new();
    private bool updating;

    public event Action Changed;

    public Control GetRow(string propertyName) => rowsByProperty.GetValueOrDefault(propertyName);
    public Control GetControl(string propertyName) => controlsByProperty.GetValueOrDefault(propertyName);
    public Label GetLabel(string propertyName) => labelsByProperty.GetValueOrDefault(propertyName);

    public void Bind(object instance)
    {
        target = instance;
        Children.Clear();
        controlsByProperty.Clear();
        rowsByProperty.Clear();
        labelsByProperty.Clear();

        if (target == null) return;

        var properties = target.GetType().GetProperties()
            .Where(p => p.GetCustomAttribute<EditorFieldAttribute>() != null);

        foreach (var prop in properties)
        {
            var attr = prop.GetCustomAttribute<EditorFieldAttribute>();
            var row = BuildRow(prop, attr);
            rowsByProperty[prop.Name] = row;
            Children.Add(row);
        }

        Refresh();
    }

    public void Refresh()
    {
        if (target == null) return;
        updating = true;

        foreach (var (name, control) in controlsByProperty)
        {
            var prop = target.GetType().GetProperty(name);
            SetControlValue(control, prop.PropertyType, prop.GetValue(target));
        }

        updating = false;
    }

    private Grid BuildRow(PropertyInfo prop, EditorFieldAttribute attr)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 8, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        var label = new Label { Content = (attr.Label ?? prop.Name) + ":", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 0);
        row.Children.Add(label);
        labelsByProperty[prop.Name] = label;

        Control field = BuildControl(prop);
        Grid.SetColumn(field, 1);
        row.Children.Add(field);
        controlsByProperty[prop.Name] = field;

        return row;
    }

    private Control BuildControl(PropertyInfo prop)
    {
        Type t = prop.PropertyType;

        if (t == typeof(bool))
        {
            var box = new CheckBox { Margin = new Thickness(4, 0, 0, 0) };
            box.IsCheckedChanged += (s, e) => PushValue(prop, box.IsChecked == true);
            return box;
        }

        if (t.IsEnum)
        {
            var box = new ComboBox { Width = 140 };
            foreach (var name in Enum.GetNames(t)) box.Items.Add(name);
            box.SelectionChanged += (s, e) =>
            {
                if (!updating && box.SelectedItem is string name) PushValue(prop, Enum.Parse(t, name));
            };
            return box;
        }

        var text = new TextBox { Width = 100 };
        text.TextChanged += (s, e) =>
        {
            if (updating) return;
            object parsed = ParseValue(t, text.Text);
            if (parsed != null) PushValue(prop, parsed);
        };
        return text;
    }

    private void PushValue(PropertyInfo prop, object value)
    {
        if (updating || target == null) return;
        prop.SetValue(target, value);
        Changed?.Invoke();
    }

    private static object ParseValue(Type t, string text)
    {
        if (t == typeof(string)) return text ?? "";
        if (t == typeof(float)) return float.TryParse(text, out float f) ? f : (object)null;
        if (t == typeof(int)) return int.TryParse(text, out int i) ? i : (object)null;
        return null;
    }

    private void SetControlValue(Control control, Type type, object value)
    {
        switch (control)
        {
            case CheckBox cb:
                cb.IsChecked = value is bool b && b;
                break;
            case ComboBox combo when type.IsEnum:
                combo.SelectedItem = value?.ToString();
                break;
            case TextBox tb:
                tb.Text = value?.ToString() ?? "";
                break;
        }
    }
}