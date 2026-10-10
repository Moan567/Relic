using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Rockwall2.Editor.Common.Toolbar;

public enum ToolButtonKind { Action, Toggle }

public sealed class ToolButtonDef
{
    public required string Id { get; init; }
    public required string ToolTip { get; init; }
    public required ToolButtonKind Kind { get; init; }
    public required IImage IconSource { get; init; }
    public IImage? IconCheckedSource { get; init; }
    public ICommand? Command { get; init; }
    public bool InitialChecked { get; init; }

    public static ToolButtonDef Action(string id, string toolTip, IImage icon, ICommand command) => new()
    {
        Id = id,
        ToolTip = toolTip,
        Kind = ToolButtonKind.Action,
        IconSource = icon,
        Command = command
    };

    public static ToolButtonDef Toggle(string id, string toolTip, IImage iconOff, IImage iconOn, ICommand command, bool initialChecked = false) => new()
    {
        Id = id,
        ToolTip = toolTip,
        Kind = ToolButtonKind.Toggle,
        IconSource = iconOff,
        IconCheckedSource = iconOn,
        Command = command,
        InitialChecked = initialChecked
    };
}

public sealed class ToolCategory
{
    public string? Title { get; init; }
    public required IReadOnlyList<ToolButtonDef> Buttons { get; init; }

    public static ToolCategory Of(params ToolButtonDef[] buttons) => new() { Buttons = buttons };
    public static ToolCategory Of(string title, params ToolButtonDef[] buttons) => new() { Title = title, Buttons = buttons };
}