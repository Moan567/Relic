using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Relic.Models;
using System.Collections.Generic;
using System.Linq;

namespace Rockwall2;

public partial class BoneMaskEditor : Window
{
    private bool isWindowDragInEffect = false;
    private Point cursorDragStart = new(0, 0);

    private readonly List<CheckBox> checkboxes = new();
    private readonly List<string> originalMask;

    public List<string> ResultMask { get; private set; }

    public BoneMaskEditor(List<string> currentMask = null, List<CBone> bones = null)
    {
        InitializeComponent();

        if (currentMask == null || bones == null) return;

        originalMask = new List<string>(currentMask);

        foreach (var bone in bones)
        {
            var cb = new CheckBox
            {
                Content = bone.Name,
                Foreground = Avalonia.Media.Brushes.White,
                Margin = new Thickness(2),
                IsChecked = currentMask.Contains(bone.Name)
            };
            checkboxes.Add(cb);
            boneCheckList.Items.Add(cb);
        }
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

    private void All_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        foreach (var cb in checkboxes) cb.IsChecked = true;
    }
    private void None_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        foreach (var cb in checkboxes) cb.IsChecked = false;
    }
    private void Ok_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ResultMask = checkboxes
            .Where(cb => cb.IsChecked == true)
            .Select(cb => cb.Content as string)
            .ToList();

        Close(true);
    }
    private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }
}