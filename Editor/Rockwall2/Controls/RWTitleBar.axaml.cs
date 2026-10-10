using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;

namespace Rockwall2;

public partial class RWTitleBar : UserControl
{
    public RWTitleBar()
    {
        // Apparently this doesnt work on non-windows platforms
        if (!OperatingSystem.IsWindows()) return;

        InitializeComponent();

        MinimizeButton.Click += (_, _) =>
            ((Window)VisualRoot!).WindowState = WindowState.Minimized;

        MaximizeButton.Click += (_, _) =>
        {
            var w = (Window)VisualRoot!;
            w.WindowState = w.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        };

        CloseButton.Click += (_, _) =>
            ((Window)VisualRoot!).Close();
    }
}