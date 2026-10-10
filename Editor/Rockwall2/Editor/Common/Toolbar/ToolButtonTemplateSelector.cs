using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Common.Toolbar;

public sealed class ToolButtonTemplateSelector : IDataTemplate
{
    private static readonly IValueConverter IsOff = new FuncValueConverter<bool?, bool>(c => c != true);
    private static readonly IValueConverter IsOn = new FuncValueConverter<bool?, bool>(c => c == true);

    public bool Match(object? data) => data is ToolButtonDef;

    public Control Build(object? param)
    {
        var def = (ToolButtonDef)param!;
        return def.Kind == ToolButtonKind.Toggle ? BuildToggle(def) : BuildAction(def);
    }
    private static Button BuildAction(ToolButtonDef def)
    {
        var btn = new Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Command = def.Command
        };
        btn.Classes.Add("icon-btn");
        ToolTip.SetTip(btn, def.ToolTip);
        btn.Content = new Image { Source = def.IconSource, Stretch = Stretch.Fill };
        return btn;
    }
    private static ToggleButton BuildToggle(ToolButtonDef def)
    {
        var toggle = new ToggleButton
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            IsChecked = def.InitialChecked,
            Command = def.Command
        };
        toggle.Classes.Add("icon-toggle");
        ToolTip.SetTip(toggle, def.ToolTip);

        var off = new Image { Source = def.IconSource, Stretch = Stretch.Fill };
        var on = new Image { Source = def.IconCheckedSource, Stretch = Stretch.Fill };
        off.Bind(Visual.IsVisibleProperty, new Binding(nameof(ToggleButton.IsChecked)) { Source = toggle, Converter = IsOff });
        on.Bind(Visual.IsVisibleProperty, new Binding(nameof(ToggleButton.IsChecked)) { Source = toggle, Converter = IsOn });

        toggle.Content = new Panel { Children = { off, on } };
        return toggle;
    }
}