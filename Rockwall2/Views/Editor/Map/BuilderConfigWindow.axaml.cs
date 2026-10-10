using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Rockwall2.Editor.Mapper.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Rockwall2;

public partial class BuilderConfigWindow : Window
{
    private bool isWindowDragInEffect = false;
    private Point cursorDragStart = new(0, 0);

    BrushBuilder builder;
    List<(FieldInfo field, Control control)> bindings = new();

    public BuilderConfigWindow()
    {
        InitializeComponent();
    }

    public BuilderConfigWindow(BrushBuilder builder)
    {
        InitializeComponent();

        this.builder = builder;
        Title = builder.GetType().Name;
        Width = 320;

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };

        var fields = builder.GetType()
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(f => (field: f, attr: f.GetCustomAttribute<BuilderParamAttribute>()))
            .Where(x => x.attr != null);

        foreach (var (field, attr) in fields)
        {
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock { Text = attr.Label, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Width = 100, Foreground = Brushes.Gray });

            var value = field.GetValue(builder);

            if (field.FieldType == typeof(int) || field.FieldType == typeof(float))
            {
                var nud = new NumericUpDown
                {
                    Value = Convert.ToDecimal(value),
                    Minimum = (decimal)attr.Min,
                    Maximum = (decimal)attr.Max,
                    Increment = (decimal)attr.Increment,
                    FormatString = attr.FormatString,
                    Width = 160
                };
                nud.ValueChanged += (s, e) =>
                {
                    object converted = nud.Value.GetValueOrDefault();
                    field.SetValue(builder, Convert.ChangeType(converted, field.FieldType));
                    builder.RegenerateGeometry();
                };
                row.Children.Add(nud);
                bindings.Add((field, nud));
            }
            else if (field.FieldType == typeof(bool))
            {
                var toggle = new ToggleButton { IsChecked = (bool)value };
                toggle.IsCheckedChanged += (s, e) =>
                {
                    field.SetValue(builder, toggle.IsChecked == true);
                    builder.RegenerateGeometry();
                };
                row.Children.Add(toggle);
                bindings.Add((field, toggle));
            }

            panel.Children.Add(row);
        }

        var commitBtn = new Button { Content = "Commit", Margin = new Thickness(0, 12, 0, 0) };
        commitBtn.Click += (s, e) => { builder.Commit(); Close(); };
        panel.Children.Add(commitBtn);

        var cancelBtn = new Button { Content = "Cancel" };
        cancelBtn.Click += (s, e) => { builder.Close(); Close(); };
        panel.Children.Add(cancelBtn);

        mainArea.Children.Add(new ScrollViewer { Content = panel });

        Closing += (s, e) =>
        {
            builder.Close();
        };
    }

    private void TitleBar_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (isWindowDragInEffect)
        {
            Point currentCursorPosition = e.GetPosition(this);
            Point delta = currentCursorPosition - cursorDragStart;
            Position = this.PointToScreen(delta);
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.Maximized || WindowState == WindowState.FullScreen) return;
        isWindowDragInEffect = true;
        cursorDragStart = e.GetPosition(this);
    }

    private void TitleBar_PointerReleased(object? sender, PointerReleasedEventArgs e)
        => isWindowDragInEffect = false;

}