using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Rockwall2.Views;
using System.Collections.Generic;
using System.Linq;

namespace Rockwall2;

public partial class CollectionPicker : Window
{
    private const string EverythingLabel = "(Import everything, no filter)";

    public string? SelectedCollection { get; private set; }

    private bool isWindowDragInEffect = false;
    private Point cursorDragStart = new(0, 0);

    public CollectionPicker() : this(Enumerable.Empty<string>()) { }

    public CollectionPicker(IEnumerable<string> collectionNames)
    {
        InitializeComponent();

        collectionList.Items.Clear();
        collectionList.Items.Add(EverythingLabel);
        foreach (var name in collectionNames)
            collectionList.Items.Add(name);

        collectionList.SelectedIndex = 0;
    }

    private void TitleBar_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!isWindowDragInEffect) return;
        Point currentCursorPosition = e.GetPosition(this);
        Point delta = currentCursorPosition - cursorDragStart;
        Position = this.PointToScreen(delta);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.Maximized || WindowState == WindowState.FullScreen) return;
        isWindowDragInEffect = true;
        cursorDragStart = e.GetPosition(this);
    }

    private void TitleBar_PointerReleased(object? sender, PointerReleasedEventArgs e)
        => isWindowDragInEffect = false;

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        var selected = collectionList.SelectedItem as string;
        SelectedCollection = (selected == null || selected == EverythingLabel) ? null : selected;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}