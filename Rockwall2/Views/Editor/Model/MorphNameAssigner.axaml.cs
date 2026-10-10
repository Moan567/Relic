using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Relic.Models.Morph;
using Rockwall2.Editor.Model.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Rockwall2;

public partial class MorphNameAssigner : Window
{
    private static readonly (string Code, string Label)[] Phonemes =
    {
        ("A", "P / B / M  —  closed lips  (viseme_PP)"),
        ("B", "K / S / T / EE  —  clenched teeth  (viseme_SS)"),
        ("C", "EH / AE  —  open mouth  (viseme_E)"),
        ("D", "AA  —  wide open  (viseme_aa)"),
        ("E", "AO / ER  —  slightly rounded  (viseme_O)"),
        ("F", "UW / OW / W  —  puckered  (viseme_U)"),
        ("G", "F / V  —  teeth on lip  (viseme_FF)"),
        ("H", "L  —  tongue up  (viseme_TH)"),
    };
    public Dictionary<string, string> Result { get; private set; }

    private readonly string sidecarPath;
    private readonly Dictionary<string, ComboBox> pickers = new();

    private bool windowDragging = false;
    private Point dragStart = new(0, 0);

    private void TitleBar_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!windowDragging) return;
        var delta = e.GetPosition(this) - dragStart;
        Position = this.PointToScreen(delta);
    }
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (WindowState is WindowState.Maximized or WindowState.FullScreen) return;
        windowDragging = true;
        dragStart = e.GetPosition(this);
    }
    private void TitleBar_PointerReleased(object? sender, PointerReleasedEventArgs e)
        => windowDragging = false;

    public MorphNameAssigner(string modelPath)
    {
        InitializeComponent();

        sidecarPath = Path.ChangeExtension(modelPath, ".phonememap");

        var morphNames = ModelEditorData.ActiveModel?.Bodygroups
            .Where(bg => bg.MorphTargets?.Count > 0)
            .SelectMany(bg => bg.MorphTargets.Select(m => m.Name))
            .Distinct()
            .OrderBy(n => n)
            .ToList() ?? new();

        // Load existing map if present
        var existing = LoadMap();

        foreach (var (code, label) in Phonemes)
        {
            var row = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            row.ColumnDefinitions.Add(new ColumnDefinition(180, GridUnitType.Pixel));

            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 8, 0),
                TextWrapping = TextWrapping.NoWrap,
            };

            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.Items.Add("— None —");
            foreach (var name in morphNames)
                combo.Items.Add(name);

            combo.SelectedIndex = 0;
            if (existing.TryGetValue(code, out string mapped))
            {
                int idx = morphNames.IndexOf(mapped);
                if (idx >= 0) combo.SelectedIndex = idx + 1;
            }

            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(combo, 1);
            row.Children.Add(lbl);
            row.Children.Add(combo);

            pickers[code] = combo;
            phonemeStack.Children.Add(row);
        }
    }

    private Dictionary<string, string> LoadMap()
    {
        if (!File.Exists(sidecarPath)) return new();
        try
        {
            var json = File.ReadAllText(sidecarPath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch { return new(); }
    }

    public Dictionary<string, string> GetMap()
    {
        var map = new Dictionary<string, string>();
        foreach (var (code, combo) in pickers)
        {
            if (combo.SelectedIndex <= 0) continue;
            map[code] = combo.SelectedItem as string;
        }
        return map;
    }

    private void BtnAccept_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            Result = GetMap();
            var json = JsonSerializer.Serialize(Result, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(sidecarPath, json);
            Close(true);
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"Error: {ex.Message}";
        }
    }

    private void BtnCancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close(false);
}