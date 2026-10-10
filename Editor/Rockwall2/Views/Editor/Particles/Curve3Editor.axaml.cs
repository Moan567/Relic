using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Relic.Particles;
using System;

namespace Rockwall2;

public partial class Curve3Editor : UserControl
{
    // Drives all three slider Maximum values simultaneously.
    // Default 1.0 (alpha). Set to e.g. 4.0 for a size-multiplier curve.
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<Curve3Editor, double>(
            nameof(Maximum), defaultValue: 1.0);

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public event EventHandler<Curve3>? ValueChanged;

    // Suppresses the ValueChanged event + preview redraws while the
    // parent is populating the sliders via LoadCurve().
    private bool _loading;

    public Curve3Editor()
    {
        InitializeComponent();

        // Ensure slider maximums match the property default.
        startSlider.Maximum = Maximum;
        midSlider.Maximum = Maximum;
        endSlider.Maximum = Maximum;
    }

    // Called when Maximum changes (replaces WPF OnMaximumChanged callback).
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MaximumProperty)
        {
            double max = (double)change.NewValue!;
            startSlider.Maximum = max;
            midSlider.Maximum = max;
            endSlider.Maximum = max;
            RedrawPreview();
        }
    }

    /// <summary>
    /// Populates the sliders from a Curve3 without firing ValueChanged.
    /// Call this from UpdateSelectionInfo.
    /// </summary>
    public void LoadCurve(Curve3 curve)
    {
        _loading = true;
        startSlider.Value = Math.Clamp(curve.start, 0, Maximum);
        midSlider.Value = Math.Clamp(curve.mid, 0, Maximum);
        endSlider.Value = Math.Clamp(curve.end, 0, Maximum);
        _loading = false;
        RedrawPreview();
    }

    /// <summary>
    /// Reads the current slider positions as a Curve3.
    /// </summary>
    public Curve3 ReadCurve() => new Curve3
    {
        start = (float)startSlider.Value,
        mid = (float)midSlider.Value,
        end = (float)endSlider.Value
    };

    private void Slider_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        RedrawPreview();
        ValueChanged?.Invoke(this, ReadCurve());
    }

    private void RedrawPreview()
    {
        const int steps = 40;
        var curve = ReadCurve();

        double w = previewCanvas.Bounds.Width;
        double h = previewCanvas.Bounds.Height;

        // Fall back to the explicit Height while the canvas hasn't been
        // measured yet (Bounds is zero before first layout pass).
        if (w < 1) w = previewCanvas.Width;
        if (h < 1) h = previewCanvas.Height;

        if (w < 1 || h < 1 || Maximum <= 0) return;

        var points = new Points();
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float v = curve.Evaluate(t);
            double px = t * w;
            double py = h - Math.Clamp(v / Maximum, 0, 1) * h;
            points.Add(new Point(px, py));
        }

        curvePreview.Points = points;
    }

    // Replaces WPF's OnRenderSizeChanged.
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        RedrawPreview();
    }
}