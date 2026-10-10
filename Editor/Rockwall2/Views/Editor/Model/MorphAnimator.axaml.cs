using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Reactive;
using Avalonia.Threading;
using Relic.Models.Morph;
using Rockwall2.Editor;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Utils;
using Rockwall2.Editor.Model.Utils;
using Rockwall2.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Rockwall2;

public partial class MorphAnimator : Window
{
    private static readonly Lazy<Bitmap> AddKeyIcon = new(() =>
        new Bitmap(AssetLoader.Open(new Uri("avares://Rockwall2/Assets/Icons/addkey.png"))));
    private static readonly Lazy<Bitmap> RemKeyIcon = new(() =>
        new Bitmap(AssetLoader.Open(new Uri("avares://Rockwall2/Assets/Icons/remkey.png"))));

    private bool windowDragging = false;
    private Point dragStart = new(0, 0);

    private float? rulerDragOriginalTime = null;
    private CMorphKeyframe draggedKeyframe = null;
    private CMorphTrack draggedTrack = null;

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

    private readonly SceneTimelineRenderer timeline = new();
    private readonly EditorAudioPlayer audioPlayer = new();
    private DispatcherTimer playTimer;
    private DispatcherTimer scrubTimer;
    private float currentTime = 0f;
    private bool frameInputUpdating = false;
    private bool isPlaying = false;
    private bool staticDirty = true;

    private RenderTargetBitmap waveformBitmap;
    private double cachedWaveformW;
    private double cachedWaveformH;

    private Line waveformPlayheadLine;
    private Line timelinePlayheadLine;
    private Rectangle timelineFrameHighlight;

    private CMorphAnimData currentAnimData;

    private Dictionary<string, Slider> morphSliders = new Dictionary<string, Slider>();

    private bool ignoreSliders = false;

    public MorphAnimator()
    {
        InitializeComponent();

        waveformPlayheadLine = new Line { Stroke = Brushes.White, StrokeThickness = 1.5 };
        viewWaveformPlayhead.Children.Add(waveformPlayheadLine);

        timelineFrameHighlight = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)) };
        timelinePlayheadLine = new Line { Stroke = Brushes.White, StrokeThickness = 1.5 };
        viewTimelinePlayhead.Children.Add(timelineFrameHighlight);
        viewTimelinePlayhead.Children.Add(timelinePlayheadLine);

        viewSequenceRuler.GetObservable(Canvas.BoundsProperty)
            .Subscribe(new AnonymousObserver<Rect>(_ => MarkDirty()));

        timeline.AttachInput(viewWaveform, () => currentTime, ScrubTo, MarkDirty);
        timeline.AttachInput(viewSequenceTimeline, () => currentTime, ScrubTo, MarkDirty);

        currentAnimData = new CMorphAnimData();

        viewSequenceRuler.PointerMoved += (s, e) =>
        {
            if (rulerDragOriginalTime == null) return;
            double w = viewSequenceRuler.Bounds.Width;
            float newTime = SnapToFrame(timeline.XToTime(e.GetPosition(viewSequenceRuler).X, w));
            foreach (var track in currentAnimData.Tracks)
            {
                foreach (var kf in track.Keyframes)
                {
                    if (MathF.Abs(kf.Time - rulerDragOriginalTime.Value) < 0.001f)
                    {
                        kf.Time = newTime;
                    }
                }
            }
            rulerDragOriginalTime = newTime;
            MarkDirty();
        };

        viewSequenceRuler.PointerReleased += (s, e) =>
        {
            if (rulerDragOriginalTime == null) return;
            foreach (var track in currentAnimData.Tracks)
            {
                track.Keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));
                MergeCloseKeyframes(track);
            }
            rulerDragOriginalTime = null;
            e.Pointer.Capture(null);
            MarkDirty();
        };

        viewSequenceTimeline.PointerMoved += (s, e) =>
        {
            if (draggedKeyframe == null) return;
            double w = viewSequenceTimeline.Bounds.Width;
            draggedKeyframe.Time = SnapToFrame(timeline.XToTime(e.GetPosition(viewSequenceTimeline).X, w));
            MarkDirty();
        };

        viewSequenceTimeline.PointerReleased += (s, e) =>
        {
            if (draggedKeyframe == null) return;
            draggedTrack.Keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));
            MergeCloseKeyframes(draggedTrack);
            draggedKeyframe = null;
            draggedTrack = null;
            e.Pointer.Capture(null);
            MarkDirty();
        }; 

        AddHandler(InputElement.KeyDownEvent, PreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(InputElement.KeyUpEvent, PreviewKeyUp, RoutingStrategies.Tunnel);

        SetupMorphSliders();
        MarkDirty();
    }

    float SnapToFrame(float time) => MathF.Round(time * SceneTimelineRenderer.FPS) / SceneTimelineRenderer.FPS;

    private void MarkDirty()
    {
        staticDirty = true;
        Redraw();
    }

    private void Redraw()
    {
        if (staticDirty)
        {
            timeline.DrawRuler(viewSequenceRuler);
            DrawRulerMarkers();
            RedrawWaveformStatic();
            RedrawTrackCanvas();
            staticDirty = false;
        }
        UpdatePlayheads();
        lblSeqFrame.Text = $"{timeline.TimeToFrame(currentTime)} / {timeline.TotalFrames}";
    }

    private void UpdatePlayheads()
    {
        double wx = timeline.TimeToX(currentTime, viewWaveformPlayhead.Bounds.Width);
        waveformPlayheadLine.StartPoint = new Point(wx, 0);
        waveformPlayheadLine.EndPoint = new Point(wx, viewWaveformPlayhead.Bounds.Height);

        double tw = viewTimelinePlayhead.Bounds.Width;
        double th = viewTimelinePlayhead.Bounds.Height;
        double tx = timeline.TimeToX(currentTime, tw);
        double fpw = Math.Max(tw / Math.Max(timeline.TotalFrames, 1), 2);

        Canvas.SetLeft(timelineFrameHighlight, tx - fpw / 2);
        timelineFrameHighlight.Width = fpw;
        timelineFrameHighlight.Height = th;

        timelinePlayheadLine.StartPoint = new Point(tx, 0);
        timelinePlayheadLine.EndPoint = new Point(tx, th);
    }

    private void RedrawWaveformStatic()
    {
        viewWaveform.Children.Clear();
        double w = viewWaveform.Bounds.Width;
        double h = viewWaveform.Bounds.Height;
        if (w <= 0 || h <= 0) return;

        if (waveformBitmap == null || cachedWaveformW != w || cachedWaveformH != h)
        {
            waveformBitmap?.Dispose();
            waveformBitmap = timeline.RenderWaveformBitmap(w, h);
            cachedWaveformW = w;
            cachedWaveformH = h;
        }

        viewWaveform.Children.Add(new Image
        {
            Source = waveformBitmap,
            Width = w,
            Height = h,
            [Canvas.LeftProperty] = 0d,
            [Canvas.TopProperty] = 0d,
        });
    }

    void DrawRulerMarkers()
    {
        if (currentAnimData == null) return;

        double w = viewSequenceRuler.Bounds.Width;
        var uniqueTimes = currentAnimData.Tracks
            .SelectMany(t => t.Keyframes.Select(k => k.Time))
            .Distinct();

        foreach (var t in uniqueTimes)
        {
            double x = timeline.TimeToX(t, w);
            var dot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = new SolidColorBrush(Color.FromRgb(255, 200, 60)),
                Cursor = new Cursor(StandardCursorType.SizeWestEast),
            };
            Canvas.SetLeft(dot, x - 3.5);
            Canvas.SetTop(dot, 3.5);

            float capturedTime = t;
            dot.PointerPressed += (s, e) =>
            {
                rulerDragOriginalTime = capturedTime;
                e.Pointer.Capture(dot);
                e.Handled = true;
            };
            viewSequenceRuler.Children.Add(dot);
        }
    }

    private void RedrawTrackCanvas()
    {
        if (currentAnimData == null) return;

        viewSequenceTimeline.Children.Clear();

        const double RowH = 30;

        viewSequenceTimeline.Height = Math.Max(timelineScroll.Bounds.Height, currentAnimData.Tracks.Count * RowH);

        double w = viewSequenceTimeline.Bounds.Width;
        double h = viewSequenceTimeline.Bounds.Height;

        int totalFrames = timeline.TotalFrames;
        double framePixelWidth = w / Math.Max(totalFrames, 1);

        if (framePixelWidth >= 4)
        {
            var frameBrush = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255));
            for (int f = 0; f <= totalFrames; f++)
            {
                double gx = f / (double)totalFrames * w;
                viewSequenceTimeline.Children.Add(new Line
                {
                    StartPoint = new Point(gx, 0),
                    EndPoint = new Point(gx, h),
                    Stroke = frameBrush,
                    StrokeThickness = 1,
                });
            }
        }

        var secBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255));
        for (int f = 0; f <= totalFrames; f += SceneTimelineRenderer.FPS)
        {
            double gx = f / (double)totalFrames * w;
            viewSequenceTimeline.Children.Add(new Line
            {
                StartPoint = new Point(gx, 0),
                EndPoint = new Point(gx, h),
                Stroke = secBrush,
                StrokeThickness = 1,
            });
        }

        if (currentAnimData.Tracks.Count > 0 && h > 0)
        {
            double rowH = h / currentAnimData.Tracks.Count;

            for (int i = 0; i < currentAnimData.Tracks.Count; i++)
            {
                var track = currentAnimData.Tracks[i];
                double y = i * rowH;

                viewSequenceTimeline.Children.Add(new Rectangle
                {
                    Width = w,
                    Height = rowH - 1,
                    Fill = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
                    [Canvas.LeftProperty] = 0d,
                    [Canvas.TopProperty] = y,
                });

                viewSequenceTimeline.Children.Add(new Line
                {
                    StartPoint = new Point(0, y + RowH - 1),
                    EndPoint = new Point(w, y + RowH - 1),
                    Stroke = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                    StrokeThickness = 1,
                });

                viewSequenceTimeline.Children.Add(new TextBlock
                {
                    Text = track.Name,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                    [Canvas.LeftProperty] = 4d,
                    [Canvas.TopProperty] = y + 2d,
                });

                for (int j = 0; j < track.Keyframes.Count - 1; j++)
                {
                    var a = track.Keyframes[j];
                    var b = track.Keyframes[j + 1];
                    viewSequenceTimeline.Children.Add(new Line
                    {
                        StartPoint = new Point(timeline.TimeToX(a.Time, w), y + RowH * (1.0 - a.Weight)),
                        EndPoint = new Point(timeline.TimeToX(b.Time, w), y + RowH * (1.0 - b.Weight)),
                        Stroke = new SolidColorBrush(Color.FromArgb(80, 255, 200, 60)),
                        StrokeThickness = 1,
                    });
                }

                foreach (var kf in track.Keyframes)
                {
                    double kx = timeline.TimeToX(kf.Time, w);
                    double ky = y + rowH * (1.0 - kf.Weight);
                    double d = 5;

                    var diamond = new Polygon
                    {
                        Fill = new SolidColorBrush(Color.FromRgb(255, 200, 60)),
                        Points = new Points
                        {
                            new Point(kx,     ky - d),
                            new Point(kx + d, ky),
                            new Point(kx,     ky + d),
                            new Point(kx - d, ky),
                        }
                    };

                    var capturedKf = kf;
                    var capturedTrack = track;
                    diamond.PointerPressed += (s, e) =>
                    {
                        draggedKeyframe = capturedKf;
                        draggedTrack = capturedTrack;
                        e.Pointer.Capture(diamond);
                        e.Handled = true;
                    };
                    viewSequenceTimeline.Children.Add(diamond);
                }
            }
        }
    }
    private void PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return;

        switch (e.Key)
        {
            case Key.Space:
                if (isPlaying) StopPlay(); else BtnPlay_Click(null, null);
                e.Handled = true;
                break;

            case Key.Left:
                ScrubTo(currentTime - 1f / SceneTimelineRenderer.FPS);
                e.Handled = true;
                break;

            case Key.Right:
                ScrubTo(currentTime + 1f / SceneTimelineRenderer.FPS);
                e.Handled = true;
                break;
        }
    }

    private void PreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return;

        e.Handled = true;
    }
    private void ScrubTo(float time)
    {
        currentTime = SnapToFrame(Math.Clamp(time, 0f, timeline.Duration));

        if (isPlaying)
        {
            audioPlayer.Seek(currentTime);
        }
        else
        {
            scrubTimer?.Stop();
            audioPlayer.ScrubPlay(currentTime);

            scrubTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            scrubTimer.Tick += (s, e) =>
            {
                scrubTimer.Stop();
                audioPlayer.StopScrub();
            };
            scrubTimer.Start();
        }

        ignoreSliders = true;
        if (currentAnimData != null)
        {
            foreach (var track in currentAnimData.Tracks)
            {
                ModelEditorData.MorphState.SetWeight(track.Name, track.Evaluate(currentTime));

                morphSliders[track.Name].Value = track.Evaluate(currentTime);
            }
        }
        ignoreSliders = false;

        MarkDirty();
    }

    private void KeyframeAdd(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        foreach (var track in currentAnimData.Tracks)
        {
            float weight = ModelEditorData.MorphState.GetWeight(track.Name);
            var existing = track.Keyframes.FirstOrDefault(k => MathF.Abs(k.Time - SnapToFrame(currentTime)) < 0.001f);
            if (existing != null)
            {
                existing.Weight = weight;
            }
            else
            {
                track.Keyframes.Add(new CMorphKeyframe { Time = SnapToFrame(currentTime), Weight = weight });
                track.Keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));
            }
        }
        MarkDirty();
    }

    private void KeyframeRemove(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        foreach (var track in currentAnimData.Tracks)
        {
            if (track.Keyframes.Count == 0) continue;
            var nearest = track.Keyframes.MinBy(k => MathF.Abs(k.Time - currentTime));
            track.Keyframes.Remove(nearest);
        }
        MarkDirty();
    }

    private async void OpenMorph(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(MainWindow.Instance);
        var storageProvider = topLevel.StorageProvider;
        var startPart = await storageProvider.TryGetFolderFromPathAsync(System.IO.Path.Combine(GlobalEditorData.WorkingDirectory, "Scenes"));

        var customType = new FilePickerFileType("Morph Files")
        {
            Patterns = new[] { "*.morph" },
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File",
            FileTypeFilter = new[] { customType },
            AllowMultiple = false,
            SuggestedStartLocation = startPart,
        });

        if (files.Count >= 1)
        {
            var morphFile = files[0].Path.LocalPath;
            currentAnimData = CMorphAnimData.LoadFromFile(morphFile);

            var audio = CMorphAnimData.ResolveAudioPath(morphFile, currentAnimData.AudioPath);
            LoadAudio(audio);

            // Just to make sure....
            // It's ok these files are teeny tiny

            // EDIT from however long later... wtf am I making sure of? I mean obviously there was a reason so i guess ill leave it,
            // ...but huh? Frame start/end times maybe?
            currentAnimData = CMorphAnimData.LoadFromFile(morphFile);
            currentAnimData.AudioPath = audio;
            currentAnimData.Tracks = currentAnimData.Tracks.Where(t => morphSliders.ContainsKey(t.Name)).ToList();

            MarkDirty();
            Redraw();
        }
    }

    private async void SaveMorph(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(MainWindow.Instance);
        var storageProvider = topLevel.StorageProvider;
        var startPart = await storageProvider.TryGetFolderFromPathAsync(System.IO.Path.Combine(GlobalEditorData.WorkingDirectory,"Scenes"));

        var customType = new FilePickerFileType("Morph Files")
        {
            Patterns = new[] { "*.morph" },
        };

        var files = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save File",
            DefaultExtension = ".morph",
            FileTypeChoices = new[] { customType },
            SuggestedStartLocation = startPart
        });

        if (files != null)
        {
            var dir = System.IO.Path.GetDirectoryName(files.Path.LocalPath);
            if (System.IO.Path.IsPathFullyQualified(currentAnimData.AudioPath))
                currentAnimData.AudioPath = System.IO.Path.GetRelativePath(dir, currentAnimData.AudioPath).Replace('\\', '/');

            CMorphAnimData.WriteToFile(currentAnimData, files.Path.LocalPath);
        }
    }

    private async void ExtractMorph(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var phonemes = await LoadPhonemes();

        var topLevel = TopLevel.GetTopLevel(this);
        var files = await topLevel!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Audio File",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("OGG Audio") { Patterns = new[] { "*.ogg" } } }
        });

        if (files == null || files.Count == 0) return;

        string path = files[0].Path.LocalPath;
        LoadAudio(path);

        var allPhonemeMorphs = phonemes.Values.ToHashSet();

        var cues = await RhubarbRunner.Detect(path);
        if (cues == null) return;

        foreach (var (start, end, code) in cues)
        {
            phonemes.TryGetValue(code, out string activeMorph);
            foreach (var track in currentAnimData.Tracks)
            {
                if (!allPhonemeMorphs.Contains(track.Name)) continue;

                float weight = (!string.IsNullOrEmpty(activeMorph) && track.Name == activeMorph) ? 1f : 0f;
                track.Keyframes.Add(new CMorphKeyframe { Time = SnapToFrame(start), Weight = weight });
            }
        }

        if (cues.Count > 0)
        {
            float endTime = SnapToFrame(cues[^1].end);

            foreach (var track in currentAnimData.Tracks)
            {
                track.Keyframes.Add(new CMorphKeyframe { Time = endTime, Weight = 0f });
            }
        }

        foreach (var track in currentAnimData.Tracks)
        {
            track.Keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        MarkDirty();
    }

    async Task<Dictionary<string, string>> LoadPhonemes()
    {
        var path = System.IO.Path.ChangeExtension(ModelEditorData.ActivePath, ".phonememap");
        if (!File.Exists(path))
        {
            return await OpenPhonemeEditor();
        }
        try
        {
            var json = File.ReadAllText(path);
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch { return new(); }
    }

    async Task<Dictionary<string, string>> OpenPhonemeEditor()
    {
        if (ModelEditorData.ActivePath == null) FileHandler.SaveModelAs();
        var editor = new MorphNameAssigner(ModelEditorData.ActivePath);
        await editor.ShowDialog(this);
        return editor.Result;
    }

    void SyncTracks(List<string> morphNames)
    {
        var existing = currentAnimData.Tracks.ToDictionary(t => t.Name);
        foreach (var name in morphNames)
        {
            if (!existing.ContainsKey(name))
            {
                currentAnimData.Tracks.Add(new CMorphTrack { Name = name });
            }
        }
    }

    void MergeCloseKeyframes(CMorphTrack track)
    {
        const float threshold = 0.0001f;
    restart:
        for (int i = 0; i < track.Keyframes.Count - 1; i++)
        {
            if (MathF.Abs(track.Keyframes[i].Time - track.Keyframes[i + 1].Time) < threshold)
            {
                if (track.Keyframes[i + 1].Weight > track.Keyframes[i].Weight)
                {
                    track.Keyframes[i].Weight = track.Keyframes[i + 1].Weight;
                }
                track.Keyframes.RemoveAt(i + 1);
                goto restart;
            }
        }
    }

    private void BtnPlay_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (isPlaying) return;
        isPlaying = true;

        audioPlayer.Play(currentTime);

        playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        playTimer.Tick += (s, e) =>
        {
            currentTime = audioPlayer.IsPlaying ? audioPlayer.CurrentTime : currentTime + 0.016f;

            if (currentTime >= timeline.Duration)
            {
                StopPlay();
                return;
            }

            if (currentAnimData != null)
            {
                foreach (var track in currentAnimData.Tracks)
                {
                    ModelEditorData.MorphState.SetWeight(track.Name, track.Evaluate(currentTime));
                }
            }

            Redraw();
        };
        playTimer.Start();
    }

    private void BtnStop_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => StopPlay();

    private void StopPlay()
    {
        isPlaying = false;
        playTimer?.Stop();
        playTimer = null;
        audioPlayer.Stop();
        Redraw();
    }

    private void BoxFrameStart_TextChanged(object? sender, TextChangedEventArgs e)
        => ApplyFrameRange();

    private void BoxFrameEnd_TextChanged(object? sender, TextChangedEventArgs e)
        => ApplyFrameRange();

    private void ApplyFrameRange()
    {
        if (frameInputUpdating) return;
        if (!int.TryParse(boxFrameStart.Text, out int start)) return;
        if (!int.TryParse(boxFrameEnd.Text, out int end)) return;
        if (end <= start) return;

        timeline.SetDuration(start, end);
        currentTime = Math.Clamp(currentTime, 0f, timeline.Duration);
        currentAnimData.StartFrame = start;
        currentAnimData.EndFrame = end;
        MarkDirty();
    }

    private async void BtnBrowseAudio_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var files = await topLevel!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Audio File",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("OGG Audio") { Patterns = new[] { "*.ogg" } } }
        });

        if (files == null || files.Count == 0) return;
        LoadAudio(files[0].Path.LocalPath);
    }

    void LoadAudio(string path)
    {
        lblAudioPath.Text = System.IO.Path.GetFileName(path);
        StopPlay();
        timeline.LoadAudio(path);
        audioPlayer.Load(path);
        waveformBitmap?.Dispose();
        waveformBitmap = null;

        frameInputUpdating = true;
        boxFrameStart.Text = "0";
        boxFrameEnd.Text = timeline.TotalFrames.ToString();
        frameInputUpdating = false;

        currentAnimData.AudioPath = path;

        currentTime = 0f;
        MarkDirty();
    }

    private void SetupMorphSliders()
    {
        morphStack.Children.Clear();

        if (ModelEditorData.ActiveModel?.Bodygroups == null) return;

        var morphNames = ModelEditorData.ActiveModel.Bodygroups
            .Where(bg => bg.MorphTargets?.Count > 0)
            .SelectMany(bg => bg.MorphTargets.Select(m => m.Name))
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        if (morphNames.Count == 0)
        {
            morphStack.Children.Add(new TextBlock
            {
                Text = "No morph targets.",
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                FontStyle = FontStyle.Italic,
                FontSize = 11,
                Margin = new Thickness(6, 4),
            });
            return;
        }

        ModelEditorData.MorphState ??= new CMorphState();

        foreach (var name in morphNames)
        {
            var capturedName = name;
            float initialWeight = ModelEditorData.MorphState.GetWeight(name);

            var row = new Grid { Margin = new Thickness(2, 1), HorizontalAlignment = HorizontalAlignment.Stretch };
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            row.ColumnDefinitions.Add(new ColumnDefinition(22, GridUnitType.Pixel));
            row.ColumnDefinitions.Add(new ColumnDefinition(22, GridUnitType.Pixel));
            row.ColumnDefinitions.Add(new ColumnDefinition(22, GridUnitType.Pixel));

            var label = new TextBlock
            {
                Text = name,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                Margin = new Thickness(2, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var valueLabel = new TextBlock
            {
                Text = initialWeight.ToString("0.00"),
                FontSize = 10,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0),
            };

            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 1,
                Value = initialWeight,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0),
            };

            slider.ValueChanged += (s, e) =>
            {
                if (ignoreSliders) return;

                float w = (float)slider.Value;
                valueLabel.Text = w.ToString("0.00");
                ModelEditorData.MorphState?.SetWeight(capturedName, w);

                var track = currentAnimData?.Tracks.FirstOrDefault(t => t.Name == capturedName);
                if (track == null) return;

                var existing = track.Keyframes.FirstOrDefault(k => MathF.Abs(k.Time - SnapToFrame(currentTime)) < 0.001f);
                if (existing != null)
                {
                    existing.Weight = w;
                }
                else
                {
                    track.Keyframes.Add(new CMorphKeyframe { Time = SnapToFrame(currentTime), Weight = w });
                    track.Keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));
                }
                MarkDirty();
            };
            morphSliders.Add(name,slider);

            var resetBtn = new Button
            {
                Content = "↶",
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var addKeyBtn = new Button
            {
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Content = new Image { Source = AddKeyIcon.Value },
            };
            addKeyBtn.Click += (s, e) =>
            {
                var track = currentAnimData?.Tracks.FirstOrDefault(t => t.Name == capturedName);
                if (track == null) return;
                float w = (float)slider.Value;
                var existing = track.Keyframes.FirstOrDefault(k => MathF.Abs(k.Time - SnapToFrame(currentTime)) < 0.001f);
                if (existing != null)
                    existing.Weight = w;
                else
                {
                    track.Keyframes.Add(new CMorphKeyframe { Time = SnapToFrame(currentTime), Weight = w });
                    track.Keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));
                }
                MarkDirty();
            };

            var remKeyBtn = new Button
            {
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Content = new Image { Source = RemKeyIcon.Value },
            };
            remKeyBtn.Click += (s, e) =>
            {
                var track = currentAnimData?.Tracks.FirstOrDefault(t => t.Name == capturedName);
                if (track == null || track.Keyframes.Count == 0) return;
                var nearest = track.Keyframes.MinBy(k => MathF.Abs(k.Time - currentTime));
                track.Keyframes.Remove(nearest);
                MarkDirty();
            };
            ToolTip.SetTip(resetBtn, "Reset to 0");
            resetBtn.Click += (s, e) => slider.Value = 0;

            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            topRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(label, 0);
            Grid.SetColumn(valueLabel, 1);
            topRow.Children.Add(label);
            topRow.Children.Add(valueLabel);

            var inner = new StackPanel { Spacing = 0 };
            inner.Children.Add(topRow);
            inner.Children.Add(slider);

            Grid.SetColumn(inner, 0);
            Grid.SetColumn(resetBtn, 1);
            Grid.SetColumn(addKeyBtn, 2);
            Grid.SetColumn(remKeyBtn, 3);
            row.Children.Add(inner);
            row.Children.Add(resetBtn);
            row.Children.Add(addKeyBtn);
            row.Children.Add(remKeyBtn);

            morphStack.Children.Add(row);
        }
        SyncTracks(morphNames);
    }

    protected override void OnClosed(EventArgs e)
    {
        StopPlay();
        audioPlayer.Dispose();
        waveformBitmap?.Dispose();
        base.OnClosed(e);
    }
}