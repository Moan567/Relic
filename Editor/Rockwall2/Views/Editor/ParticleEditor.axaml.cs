using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Relic.Particles;
using Relic.Utils;
using Rockwall2.Editor.Common;
using Rockwall;
using Rockwall2.Editor.Particles;
using Rockwall2.ViewModels.Editor;
using Rockwall2.Views;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Rockwall2;

public partial class ParticleEditor : UserControl
{
    public Control GameView => gameControl;

    public static ParticleEditor Instance { get; private set; }

    private readonly HashSet<int> mutedSubsystems = new();
    private int soloSubsystem = -1;

    private bool _updating;

    private static readonly string[] TimelineColors =
    {
        "#5B9BD5", "#ED7D31", "#A9D18E", "#FF6B6B",
        "#FFC000", "#9E480E", "#B4C6E7", "#C6EFCE",
        "#FFE699", "#FABF8F"
    };

    public ParticleEditor()
    {
        if (!Design.IsDesignMode)
            DataContext = new ParticleEditorViewModel();

        Instance = this;

        InitializeComponent();
        gameControl.Host = App.Host;
        GameView.PointerEntered += (a, b) => GameView.Focus();

        // Any change made through the generated field panel restarts the
        // preview, same as every individual handler used to do manually.
        particleFieldPanel.Changed += ResetSystem;

        UpdatePlayStateLabel();
    }

    private void OnPlayClick(object? sender, RoutedEventArgs e)
    {
        ParticleView.Instance.IsPaused = false;
        UpdatePlayStateLabel();
    }

    private void OnPauseClick(object? sender, RoutedEventArgs e)
    {
        ParticleView.Instance.IsPaused = true;
        UpdatePlayStateLabel();
    }

    private void OnRestartClick(object? sender, RoutedEventArgs? e = null)
    {
        ParticleView.Instance.Restart();
        UpdatePlayStateLabel();
    }

    private void UpdatePlayStateLabel()
    {
        lblPlayState.Text = ParticleView.Instance?.IsPaused == true ? "Paused" : "Playing";
    }

    public void Refresh()
    {
        systemViewer.Items.Clear();

        var spawner = ParticleView.Instance?.ActiveSpawner;

        subsystemPanel.IsEnabled = spawner?.system?.particleSubsystems?.Length > 0;

        if (spawner?.system != null)
        {
            _updating = true;
            try
            {
                lifetimeBox.Text = spawner.system.behavior.lifetime.ToString(CultureInfo.InvariantCulture);
                bounceBox.Text = spawner.system.behavior.bounce.ToString(CultureInfo.InvariantCulture);
                dampenBox.Text = spawner.system.behavior.velocityDampening.ToString(CultureInfo.InvariantCulture);
                collisionTick.IsChecked = spawner.system.behavior.collidesWithWorld;
            }
            finally { _updating = false; }
        }

        if (spawner?.system?.particleSubsystems == null) return;

        for (int i = 0; i < spawner.system.particleSubsystems.Length; i++)
        {
            var colorHex = TimelineColors[i % TimelineColors.Length];
            var avColor = Avalonia.Media.Color.Parse(colorHex);
            var idx = i;

            var label = new TextBlock
            {
                Text = $"Subsystem {i}: {spawner.system.particleSubsystems[i].behavior.material ?? "(no material)"}",
                FontSize = 12,
                Foreground = new SolidColorBrush(avColor),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };

            var soloButton = new ToggleButton
            {
                Content = "S",
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                FontSize = 10,
                IsChecked = soloSubsystem == idx,
            };

            var muteButton = new ToggleButton
            {
                Content = "M",
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                FontSize = 10,
                IsChecked = mutedSubsystems.Contains(idx),
            };

            soloButton.Click += (_, _) =>
            {
                soloSubsystem = soloSubsystem == idx ? -1 : idx;
                Refresh();
            };

            muteButton.Click += (_, _) =>
            {
                if (mutedSubsystems.Contains(idx))
                {
                    mutedSubsystems.Remove(idx);
                }
                else
                {
                    mutedSubsystems.Add(idx);
                }
            };

            var headerPanel = new DockPanel();
            DockPanel.SetDock(soloButton, Dock.Right);
            DockPanel.SetDock(muteButton, Dock.Right);
            headerPanel.Children.Add(soloButton);
            headerPanel.Children.Add(muteButton);
            headerPanel.Children.Add(label);

            var item = new TreeViewItem
            {
                Header = headerPanel,
                FontSize = 12,
            };

            item.Tapped += (_, _) =>
            {
                ParticleView.Instance.SelectedSubsystem = idx;
                UpdateSelectionInfo();
                RefreshTimeline();
            };
            systemViewer.Items.Add(item);
        }

        // Select the active subsystem in the tree.
        int sel = ParticleView.Instance.SelectedSubsystem;
        if (sel < systemViewer.Items.Count)
            ((TreeViewItem)systemViewer.Items[sel]!).IsSelected = true;

        UpdateSelectionInfo();
        RefreshTimeline();
    }

    public void UpdateSelectionInfo()
    {
        var b = GetCurrentSubsystemBehavior();

        // Material stays manual (it needs the picker dialog, not a reflectable
        // field), so it's still refreshed here explicitly.
        subMaterialName.Text = b?.material ?? "(none)";

        // Everything else
        particleFieldPanel.Bind(b);
    }

    private void RefreshTimeline()
    {
        timelineCanvas.Children.Clear();
        var spawner = ParticleView.Instance?.ActiveSpawner;
        if (spawner == null) return;

        double w = timelineCanvas.Bounds.Width;
        if (w < 2) return;

        float maxLife = spawner.system.behavior.lifetime;
        if (maxLife <= 0f) maxLife = 1f;

        const double barH = 20;
        const double barY = 14;
        int selected = ParticleView.Instance.SelectedSubsystem;

        for (int i = 0; i < spawner.system.particleSubsystems.Length; i++)
        {
            var b = spawner.system.particleSubsystems[i].behavior;
            double xPos = (b.startDelay / maxLife) * w;
            double yPos = barH * i + barY;
            double bw = Math.Max((b.lifetime / maxLife) * w, 4);
            bool sel = i == selected;

            var avColor = Avalonia.Media.Color.Parse(TimelineColors[i % TimelineColors.Length]);

            var rect = new Avalonia.Controls.Shapes.Rectangle
            {
                Width = bw,
                Height = barH,
                RadiusX = 3,
                RadiusY = 3,
                Fill = new SolidColorBrush(avColor) { Opacity = sel ? 1.0 : 0.5 },
                Cursor = new Cursor(StandardCursorType.Hand),
            };

            if (sel)
                rect.Stroke = new SolidColorBrush(Colors.White) { Opacity = 0.8 };

            Canvas.SetLeft(rect, xPos);
            Canvas.SetTop(rect, yPos);

            var captured = i;
            rect.PointerPressed += (_, _) =>
            {
                ParticleView.Instance.SelectedSubsystem = captured;
                if (captured < systemViewer.Items.Count)
                    ((TreeViewItem)systemViewer.Items[captured]!).IsSelected = true;
                UpdateSelectionInfo();
                RefreshTimeline();
            };

            timelineCanvas.Children.Add(rect);

            if (bw >= 16)
            {
                var lbl = new TextBlock
                {
                    Text = i.ToString(),
                    FontSize = 10,
                    Foreground = Brushes.White,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(lbl, xPos + 4);
                Canvas.SetTop(lbl, yPos + 3);
                timelineCanvas.Children.Add(lbl);
            }
        }

        // Time-ruler ticks (0 %, 25 %, 50 %, 75 %, 100 %)
        for (int t = 0; t <= 4; t++)
        {
            double tx = t * 0.25 * w;

            var tick = new Line
            {
                StartPoint = new Avalonia.Point(tx, 2),
                EndPoint = new Avalonia.Point(tx, barY - 1),
                Stroke = new SolidColorBrush(Avalonia.Media.Color.FromArgb(80, 255, 255, 255)),
                StrokeThickness = 1,
            };
            timelineCanvas.Children.Add(tick);

            var tickLbl = new TextBlock
            {
                Text = $"{t * 25}%",
                FontSize = 9,
                Foreground = new SolidColorBrush(Avalonia.Media.Color.FromArgb(100, 200, 200, 200)),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(tickLbl, tx + 2);
            Canvas.SetTop(tickLbl, 0);
            timelineCanvas.Children.Add(tickLbl);
        }
    }

    private void TimelineCanvas_SizeChanged(object? sender, SizeChangedEventArgs e) => RefreshTimeline();

    private void AddButton_Click(object? sender, RoutedEventArgs e)
    {
        var spawner = ParticleView.Instance?.ActiveSpawner;
        if (spawner?.system?.particleSubsystems == null) return;

        var newB = new ParticleSubsystemBehavior();
        if (GlobalMapData.LoadedMaterials?.Length > 0)
            newB.material = GlobalMapData.LoadedMaterials[0].Name;
        var newSub = new ParticleSubsystem(newB) { parent = spawner.system };

        spawner.system.particleSubsystems =
            spawner.system.particleSubsystems.Append(newSub).ToArray();
        ParticleView.Instance.SelectedSubsystem =
            spawner.system.particleSubsystems.Length - 1;

        Refresh();
        ResetSystem();
    }

    private void DuplicateButton_Click(object? sender, RoutedEventArgs e)
    {
        var sub = GetCurrentSubsystem();
        if (sub == null) return;
        var spawner = ParticleView.Instance.ActiveSpawner;

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(sub.behavior, ParticleJson.Settings);
        var newB = Newtonsoft.Json.JsonConvert.DeserializeObject<ParticleSubsystemBehavior>(json, ParticleJson.Settings)!;
        var newSub = new ParticleSubsystem(newB) { parent = spawner.system };

        spawner.system.particleSubsystems =
            spawner.system.particleSubsystems.Append(newSub).ToArray();
        ParticleView.Instance.SelectedSubsystem =
            spawner.system.particleSubsystems.Length - 1;

        Refresh();
        ResetSystem();
    }

    private void DeleteButton_Click(object? sender, RoutedEventArgs e)
    {
        var spawner = ParticleView.Instance?.ActiveSpawner;
        if (spawner?.system?.particleSubsystems == null ||
            spawner.system.particleSubsystems.Length == 0) return;

        var asList = spawner.system.particleSubsystems.ToList();
        asList.RemoveAt(ParticleView.Instance.SelectedSubsystem);
        spawner.system.particleSubsystems = asList.ToArray();
        ParticleView.Instance.SelectedSubsystem =
            Math.Clamp(ParticleView.Instance.SelectedSubsystem,
                       0, Math.Max(asList.Count - 1, 0));

        Refresh();
        ResetSystem();
    }

    private void LifetimeChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        var spawner = ParticleView.Instance?.ActiveSpawner;
        if (spawner == null) return;
        if (!TryParseF(lifetimeBox.Text, out var v)) return;
        spawner.system.behavior.lifetime = v;
        RefreshTimeline();
    }

    private void BounceChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        var spawner = ParticleView.Instance?.ActiveSpawner;
        if (spawner == null) return;
        if (!TryParseF(bounceBox.Text, out var v)) return;
        spawner.system.behavior.bounce = v;
    }

    private void DampenChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        var spawner = ParticleView.Instance?.ActiveSpawner;
        if (spawner == null) return;
        if (!TryParseF(dampenBox.Text, out var v)) return;
        spawner.system.behavior.velocityDampening = v;
    }

    private void CollisionTick_Click(object? sender, RoutedEventArgs e)
    {
        if (_updating) return;
        var spawner = ParticleView.Instance?.ActiveSpawner;
        if (spawner == null) return;
        spawner.system.behavior.collidesWithWorld = collisionTick.IsChecked == true;
    }

    private async void SubMaterial_Click(object? sender, RoutedEventArgs e)
    {
        var b = GetCurrentSubsystemBehavior(); if (b == null) return;
        int result = await MaterialPicker.PickAsync(MainWindow.Instance);
        if (result >= 0)
        {
            b.material = GlobalMapData.LoadedMaterials[result].Name;
            subMaterialName.Text = b.material;

            int idx = ParticleView.Instance.SelectedSubsystem;
            if (idx < systemViewer.Items.Count)
                ((TreeViewItem)systemViewer.Items[idx]!).Header =
                    $"Subsystem {idx}: {b.material}";
        }
        ResetSystem();
    }

    private ParticleSubsystem? GetCurrentSubsystem()
    {
        var s = ParticleView.Instance?.ActiveSpawner;
        if (s?.system?.particleSubsystems == null ||
            s.system.particleSubsystems.Length == 0) return null;
        int idx = ParticleView.Instance.SelectedSubsystem;
        if (idx >= s.system.particleSubsystems.Length) return null;
        return s.system.particleSubsystems[idx];
    }

    private ParticleSubsystemBehavior? GetCurrentSubsystemBehavior()
        => GetCurrentSubsystem()?.behavior;

    private void ResetSystem()
    {
        var viewer = ParticleView.Instance;
        if (viewer?.ActiveSpawner == null) return;
        viewer.Restart();
        UpdatePlayStateLabel();
    }

    public bool IsSubsystemActive(int index)
    {
        if (soloSubsystem >= 0)
        {
            return index == soloSubsystem;
        }

        return !mutedSubsystems.Contains(index);
    }

    private static bool TryParseF(string? s, out float result)
        => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
}