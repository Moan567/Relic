using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System.Collections.Generic;
using System.Linq;

namespace Rockwall2;

public partial class AnimationPicker : Window
{
    private bool isWindowDragInEffect = false;
    private Point cursorDragStart = new(0, 0);

    private readonly List<(CheckBox checkbox, string animName, bool alreadyExists)> _items = new();
    public List<string> SelectedAnimations { get; private set; }

    public AnimationPicker(List<string> animationsInFBX = null, HashSet<string> animationsInModel = null)
    {
        InitializeComponent();

        if (animationsInFBX == null || animationsInModel == null) return;

        foreach (var name in animationsInFBX)
        {
            bool exists = animationsInModel.Contains(name);

            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };

            var cb = new CheckBox
            {
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };

            var label = new TextBlock
            {
                Text = name,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };

            var tag = new TextBlock
            {
                Text = exists ? " (replace)" : " (new)",
                Foreground = exists
                    ? new SolidColorBrush(Color.FromRgb(255, 180, 60))
                    : new SolidColorBrush(Color.FromRgb(100, 220, 100)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };

            panel.Children.Add(cb);
            panel.Children.Add(label);
            panel.Children.Add(tag);
            animList.Children.Add(panel);

            _items.Add((cb, name, exists));
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
        foreach (var item in _items) item.checkbox.IsChecked = true;
    }
    private void None_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        foreach (var item in _items) item.checkbox.IsChecked = false;
    }
    private void Ok_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SelectedAnimations = _items
            .Where(i => i.checkbox.IsChecked == true)
            .Select(i => i.animName)
            .ToList();
        Close(true);
    }
    private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }
}