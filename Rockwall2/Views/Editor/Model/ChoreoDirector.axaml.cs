using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Reactive;
using Avalonia.Threading;
using Relic;
using Relic.Models.Morph;
using Rockwall2.Editor;
using Rockwall2.Editor.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Rockwall2;

public partial class ChoreoDirector : UserControl
{
    public event Action? CloseRequested;

    private readonly SceneTimelineRenderer timeline = new();
    private readonly EditorAudioPlayer audioPlayer = new();
    private DispatcherTimer playTimer;
    private float currentTime = 0f;
    private bool frameInputUpdating = false;
    private bool isPlaying = false;
    private bool staticDirty = true;

    private Line timelinePlayheadLine;
    private Rectangle timelineFrameHighlight;

    private ChoreoScene currentScene = new();
    private int selectedTrackIndex = -1;
    private int selectedEventIndex = -1;
    private int dragEventTrack = -1;
    private int dragEventIdx = -1;
    private bool propUpdating = false;
    private bool timelineScrubbing = false;
    private bool rulerScrubbing = false;

    const double LabelW = 140;
    const double RowH = 40;

    public ChoreoDirector()
    {
        InitializeComponent();

        timelineFrameHighlight = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)) };
        timelinePlayheadLine = new Line { Stroke = Brushes.White, StrokeThickness = 1.5 };
        viewTimelinePlayhead.Children.Add(timelineFrameHighlight);
        viewTimelinePlayhead.Children.Add(timelinePlayheadLine);

        viewSequenceRuler.GetObservable(Canvas.BoundsProperty)
            .Subscribe(new AnonymousObserver<Rect>(_ => MarkDirty()));
        viewSequenceTimeline.GetObservable(Canvas.BoundsProperty)
            .Subscribe(new AnonymousObserver<Rect>(_ => MarkDirty()));

        viewSequenceRuler.PointerPressed += (s, e) =>
        {
            rulerScrubbing = true;
            ScrubTo(CanvasXToTime(e.GetPosition(viewSequenceRuler).X, viewSequenceRuler.Bounds.Width));
            e.Pointer.Capture(viewSequenceRuler);
        };
        viewSequenceRuler.PointerMoved += (s, e) =>
        {
            if (!rulerScrubbing) return;
            ScrubTo(CanvasXToTime(e.GetPosition(viewSequenceRuler).X, viewSequenceRuler.Bounds.Width));
        };
        viewSequenceRuler.PointerReleased += (s, e) =>
        {
            rulerScrubbing = false;
            e.Pointer.Capture(null);
        };

        viewSequenceTimeline.PointerPressed += OnTimelinePointerPressed;
        viewSequenceTimeline.PointerMoved += OnTimelinePointerMoved;
        viewSequenceTimeline.PointerReleased += OnTimelinePointerReleased;

        MarkDirty();
        RefreshProperties();
    }

    public void NotifyShown()
    {
        MarkDirty();
        RefreshProperties();
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        StopPlay();
        CloseRequested?.Invoke();
    }

    float SnapToFrame(float t) => MathF.Round(t * SceneTimelineRenderer.FPS) / SceneTimelineRenderer.FPS;
    double TimeToCanvasX(float t, double w) => LabelW + timeline.TimeToX(t, w - LabelW);
    float CanvasXToTime(double x, double w) => timeline.XToTime(Math.Max(0, x - LabelW), w - LabelW);

    static Color EventColor(ChoreoEventType type) => type switch
    {
        ChoreoEventType.LookAt => Color.FromRgb(80, 160, 240),
        ChoreoEventType.PlayMorph => Color.FromRgb(80, 200, 100),
        ChoreoEventType.PlayGesture => Color.FromRgb(180, 100, 220),
        ChoreoEventType.EntityIO => Color.FromRgb(240, 160, 50),
        _ => Color.FromRgb(120, 120, 120),
    };

    static float GetEventDisplayDuration(ChoreoEvent evt)
    {
        if (evt.EventType == ChoreoEventType.PlayMorph && !string.IsNullOrEmpty(evt.Target))
        {
            try
            {
                string fullPath = System.IO.Path.Combine(GlobalEditorData.WorkingDirectory, evt.Target);
                var data = CMorphAnimData.LoadFromFile(fullPath);
                return (data.EndFrame - data.StartFrame) / (float)SceneTimelineRenderer.FPS;
            }
            catch { }
        }
        return 1.0f;
    }

    private void MarkDirty()
    {
        staticDirty = true;
        Redraw();
    }

    private void Redraw()
    {
        if (staticDirty)
        {
            timeline.DrawRuler(viewSequenceRuler, LabelW);
            RedrawTrackCanvas();
            staticDirty = false;
        }
        UpdatePlayheads();
        lblSeqFrame.Text = $"{timeline.TimeToFrame(currentTime)} / {timeline.TotalFrames}";
    }

    private void UpdatePlayheads()
    {
        double tw = viewTimelinePlayhead.Bounds.Width;
        double th = viewTimelinePlayhead.Bounds.Height;
        double tx = LabelW + timeline.TimeToX(currentTime, tw - LabelW);
        double fpw = Math.Max((tw - LabelW) / Math.Max(timeline.TotalFrames, 1), 2);

        Canvas.SetLeft(timelineFrameHighlight, tx - fpw / 2);
        timelineFrameHighlight.Width = fpw;
        timelineFrameHighlight.Height = th;

        timelinePlayheadLine.StartPoint = new Point(tx, 0);
        timelinePlayheadLine.EndPoint = new Point(tx, th);
    }

    private void RedrawTrackCanvas()
    {
        viewSequenceTimeline.Children.Clear();

        viewSequenceTimeline.Height = Math.Max(
            timelineScroll.Bounds.Height,
            currentScene.Tracks.Count * RowH);

        double w = viewSequenceTimeline.Bounds.Width;
        double h = viewSequenceTimeline.Bounds.Height;
        if (w <= 0) return;

        int totalFrames = timeline.TotalFrames;
        double framePixelW = (w - LabelW) / Math.Max(totalFrames, 1);

        if (framePixelW >= 4)
        {
            var fBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
            for (int f = 0; f <= totalFrames; f++)
            {
                double gx = LabelW + f / (double)totalFrames * (w - LabelW);
                viewSequenceTimeline.Children.Add(new Line
                {
                    StartPoint = new Point(gx, 0),
                    EndPoint = new Point(gx, h),
                    Stroke = fBrush,
                    StrokeThickness = 1,
                });
            }
        }

        var secBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255));
        for (int f = 0; f <= totalFrames; f += SceneTimelineRenderer.FPS)
        {
            double gx = LabelW + f / (double)totalFrames * (w - LabelW);
            viewSequenceTimeline.Children.Add(new Line
            {
                StartPoint = new Point(gx, 0),
                EndPoint = new Point(gx, h),
                Stroke = secBrush,
                StrokeThickness = 1,
            });
        }

        viewSequenceTimeline.Children.Add(new Line
        {
            StartPoint = new Point(LabelW, 0),
            EndPoint = new Point(LabelW, h),
            Stroke = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
            StrokeThickness = 1,
        });

        for (int ti = 0; ti < currentScene.Tracks.Count; ti++)
        {
            var track = currentScene.Tracks[ti];
            double y = ti * RowH;
            bool trackSel = ti == selectedTrackIndex;

            viewSequenceTimeline.Children.Add(new Rectangle
            {
                Width = w,
                Height = RowH - 1,
                Fill = new SolidColorBrush(trackSel ? Color.FromRgb(35, 35, 42) : Color.FromRgb(28, 28, 28)),
                [Canvas.LeftProperty] = 0d,
                [Canvas.TopProperty] = y,
            });
            viewSequenceTimeline.Children.Add(new Rectangle
            {
                Width = LabelW,
                Height = RowH - 1,
                Fill = new SolidColorBrush(trackSel ? Color.FromRgb(40, 40, 52) : Color.FromRgb(22, 22, 22)),
                [Canvas.LeftProperty] = 0d,
                [Canvas.TopProperty] = y,
            });
            viewSequenceTimeline.Children.Add(new TextBlock
            {
                Text = track.ActorName ?? "(unnamed)",
                FontSize = 10,
                Foreground = new SolidColorBrush(trackSel ? Color.FromRgb(220, 220, 255) : Color.FromRgb(160, 160, 160)),
                [Canvas.LeftProperty] = 6d,
                [Canvas.TopProperty] = y + RowH / 2 - 7,
            });
            viewSequenceTimeline.Children.Add(new Line
            {
                StartPoint = new Point(0, y + RowH - 1),
                EndPoint = new Point(w, y + RowH - 1),
                Stroke = new SolidColorBrush(Color.FromRgb(38, 38, 38)),
                StrokeThickness = 1,
            });

            for (int ei = 0; ei < track.ChoreoEvents.Count; ei++)
            {
                var evt = track.ChoreoEvents[ei];
                bool evtSel = trackSel && ei == selectedEventIndex;
                double ex = TimeToCanvasX(evt.Time, w);
                var color = EventColor(evt.EventType);
                var brush = new SolidColorBrush(color);

                int capturedTi = ti;
                int capturedEi = ei;

                if (evt.EventType is ChoreoEventType.PlayMorph or ChoreoEventType.PlayGesture)
                {
                    float dur = GetEventDisplayDuration(evt);
                    double blockW = Math.Max((w - LabelW) * (dur / Math.Max(timeline.Duration, 0.001f)), 8);
                    double ey = y + 5, eh = RowH - 10;

                    var rect = new Rectangle
                    {
                        Width = blockW,
                        Height = eh,
                        Fill = new SolidColorBrush(Color.FromArgb((byte)(evtSel ? 210 : 150), color.R, color.G, color.B)),
                        Stroke = evtSel ? Brushes.White : brush,
                        StrokeThickness = evtSel ? 1.5 : 1,
                        RadiusX = 3,
                        RadiusY = 3,
                        [Canvas.LeftProperty] = ex,
                        [Canvas.TopProperty] = ey,
                    };
                    rect.PointerPressed += (s, e) =>
                    {
                        selectedTrackIndex = capturedTi; selectedEventIndex = capturedEi;
                        dragEventTrack = capturedTi; dragEventIdx = capturedEi;
                        e.Pointer.Capture(rect); e.Handled = true;
                        MarkDirty(); RefreshProperties();
                    };
                    viewSequenceTimeline.Children.Add(rect);
                    viewSequenceTimeline.Children.Add(new TextBlock
                    {
                        Text = System.IO.Path.GetFileNameWithoutExtension(evt.Target ?? ""),
                        FontSize = 8,
                        Foreground = Brushes.White,
                        [Canvas.LeftProperty] = ex + 3,
                        [Canvas.TopProperty] = ey + 2,
                    });
                }
                else
                {
                    double mx = ex, my = y + RowH / 2, d = evtSel ? 7 : 5;
                    var diamond = new Polygon
                    {
                        Fill = brush,
                        Stroke = evtSel ? Brushes.White : null,
                        StrokeThickness = evtSel ? 1.5 : 0,
                        Points = new Points
                        {
                            new Point(mx, my - d), new Point(mx + d, my),
                            new Point(mx, my + d), new Point(mx - d, my),
                        }
                    };
                    diamond.PointerPressed += (s, e) =>
                    {
                        selectedTrackIndex = capturedTi; selectedEventIndex = capturedEi;
                        dragEventTrack = capturedTi; dragEventIdx = capturedEi;
                        e.Pointer.Capture(diamond); e.Handled = true;
                        MarkDirty(); RefreshProperties();
                    };
                    viewSequenceTimeline.Children.Add(diamond);
                }
            }
        }
    }

    private void OnTimelinePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pt = e.GetPosition(viewSequenceTimeline);
        int ti = (int)(pt.Y / RowH);
        bool valid = ti >= 0 && ti < currentScene.Tracks.Count;

        if (e.GetCurrentPoint(viewSequenceTimeline).Properties.IsRightButtonPressed)
        {
            if (!valid || pt.X <= LabelW) return;
            var newEvent = new ChoreoEvent
            {
                EventType = ChoreoEventType.EntityIO,
                Target = "",
                Data1 = "",
                Data2 = "",
                Time = SnapToFrame(CanvasXToTime(pt.X, viewSequenceTimeline.Bounds.Width))
            };
            currentScene.Tracks[ti].ChoreoEvents.Add(newEvent);
            selectedTrackIndex = ti;
            selectedEventIndex = currentScene.Tracks[ti].ChoreoEvents.Count - 1;
            MarkDirty(); RefreshProperties();
            return;
        }

        if (pt.X <= LabelW)
        {
            if (!valid) return;
            selectedTrackIndex = ti; selectedEventIndex = -1; dragEventTrack = -1;
            MarkDirty(); RefreshProperties();
        }
        else
        {
            timelineScrubbing = true;
            ScrubTo(CanvasXToTime(pt.X, viewSequenceTimeline.Bounds.Width));
            e.Pointer.Capture(viewSequenceTimeline);
        }
    }

    private void OnTimelinePointerMoved(object? sender, PointerEventArgs e)
    {
        if (dragEventTrack >= 0)
        {
            float newTime = SnapToFrame(CanvasXToTime(e.GetPosition(viewSequenceTimeline).X, viewSequenceTimeline.Bounds.Width));
            var track = currentScene.Tracks[dragEventTrack];
            var evt = track.ChoreoEvents[dragEventIdx];
            evt.Time = newTime;
            track.ChoreoEvents[dragEventIdx] = evt;
            if (dragEventTrack == selectedTrackIndex && dragEventIdx == selectedEventIndex)
            {
                propUpdating = true; txtEvtTime.Text = newTime.ToString("0.###"); propUpdating = false;
            }
            MarkDirty();
            return;
        }
        if (timelineScrubbing)
            ScrubTo(CanvasXToTime(e.GetPosition(viewSequenceTimeline).X, viewSequenceTimeline.Bounds.Width));
    }

    private void OnTimelinePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (dragEventTrack >= 0)
        {
            var track = currentScene.Tracks[dragEventTrack];
            var evt = track.ChoreoEvents[dragEventIdx];
            track.ChoreoEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
            if (dragEventTrack == selectedTrackIndex)
                selectedEventIndex = track.ChoreoEvents.FindIndex(ev =>
                    ev.Time == evt.Time && ev.Target == evt.Target && ev.Data1 == evt.Data1);
            dragEventTrack = -1;
            e.Pointer.Capture(null);
            MarkDirty();
            return;
        }
        if (timelineScrubbing)
        {
            timelineScrubbing = false;
            e.Pointer.Capture(null);
        }
    }

    private void RefreshProperties()
    {
        bool hasTrack = selectedTrackIndex >= 0 && selectedTrackIndex < currentScene.Tracks.Count;
        bool hasEvent = hasTrack && selectedEventIndex >= 0
            && selectedEventIndex < currentScene.Tracks[selectedTrackIndex].ChoreoEvents.Count;

        lblNoSelection.IsVisible = !hasTrack;
        panelTrackProps.IsVisible = hasTrack;
        sepProps.IsVisible = hasTrack && hasEvent;
        panelEventProps.IsVisible = hasEvent;

        if (!hasTrack) return;

        propUpdating = true;
        txtActorName.Text = currentScene.Tracks[selectedTrackIndex].ActorName ?? "";

        if (hasEvent)
        {
            var evt = currentScene.Tracks[selectedTrackIndex].ChoreoEvents[selectedEventIndex];
            cmbEventType.SelectedIndex = (int)evt.EventType;
            txtTarget.Text = evt.Target ?? "";
            txtData1.Text = evt.Data1 ?? "";
            txtData2.Text = evt.Data2 ?? "";
            txtEvtTime.Text = evt.Time.ToString("0.###");
            UpdateEventFieldLabels(evt.EventType);
        }
        propUpdating = false;
    }

    private void UpdateEventFieldLabels(ChoreoEventType type)
    {
        switch (type)
        {
            case ChoreoEventType.LookAt:
                lblTarget.Content = "Entity:";
                rowData1.IsVisible = false;
                rowData2.IsVisible = false;
                break;
            case ChoreoEventType.PlayMorph:
                lblTarget.Content = "Morph File:";
                rowData1.IsVisible = false;
                rowData2.IsVisible = false;
                break;
            case ChoreoEventType.PlayGesture:
                lblTarget.Content = "Sequence:";
                rowData1.IsVisible = false;
                rowData2.IsVisible = false;
                break;
            case ChoreoEventType.EntityIO:
                lblTarget.Content = "Entity:";
                lblData1.Content = "Input:";
                lblData2.Content = "Parameter:";
                rowData1.IsVisible = true;
                rowData2.IsVisible = true;
                break;
        }
    }

    private void SetEvt(Func<ChoreoEvent, ChoreoEvent> fn)
    {
        if (selectedTrackIndex < 0 || selectedEventIndex < 0) return;
        var track = currentScene.Tracks[selectedTrackIndex];
        if (selectedEventIndex >= track.ChoreoEvents.Count) return;
        track.ChoreoEvents[selectedEventIndex] = fn(track.ChoreoEvents[selectedEventIndex]);
    }

    private void EventType_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (propUpdating) return;
        var newType = (ChoreoEventType)cmbEventType.SelectedIndex;
        SetEvt(ev => { var n = ev; n.EventType = newType; return n; });
        UpdateEventFieldLabels(newType);
        MarkDirty();
    }

    private void ActorName_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (propUpdating || selectedTrackIndex < 0) return;
        currentScene.Tracks[selectedTrackIndex].ActorName = txtActorName.Text ?? "";
        MarkDirty();
    }

    private void Target_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (propUpdating) return;
        SetEvt(ev => { var n = ev; n.Target = txtTarget.Text ?? ""; return n; });
        MarkDirty();
    }

    private void Data1_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (propUpdating) return;
        SetEvt(ev => { var n = ev; n.Data1 = txtData1.Text ?? ""; return n; });
    }

    private void Data2_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (propUpdating) return;
        SetEvt(ev => { var n = ev; n.Data2 = txtData2.Text ?? ""; return n; });
    }

    private void EvtTime_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (propUpdating) return;
        if (!float.TryParse(txtEvtTime.Text, out float t)) return;
        SetEvt(ev => { var n = ev; n.Time = SnapToFrame(t); return n; });
        MarkDirty();
    }

    private void AddTrack_Click(object? sender, RoutedEventArgs e)
    {
        currentScene.Tracks.Add(new ChoreoTrack { ActorName = "actor" });
        selectedTrackIndex = currentScene.Tracks.Count - 1;
        selectedEventIndex = -1;
        MarkDirty(); RefreshProperties();
    }

    private void DeleteTrack_Click(object? sender, RoutedEventArgs e)
    {
        if (selectedTrackIndex < 0 || selectedTrackIndex >= currentScene.Tracks.Count) return;
        currentScene.Tracks.RemoveAt(selectedTrackIndex);
        selectedTrackIndex = Math.Min(selectedTrackIndex, currentScene.Tracks.Count - 1);
        selectedEventIndex = -1;
        MarkDirty(); RefreshProperties();
    }

    private void DeleteEvent_Click(object? sender, RoutedEventArgs e)
    {
        if (selectedTrackIndex < 0 || selectedEventIndex < 0) return;
        var track = currentScene.Tracks[selectedTrackIndex];
        if (selectedEventIndex >= track.ChoreoEvents.Count) return;
        track.ChoreoEvents.RemoveAt(selectedEventIndex);
        selectedEventIndex = -1;
        MarkDirty(); RefreshProperties();
    }

    private void ScrubTo(float time)
    {
        currentTime = SnapToFrame(Math.Clamp(time, 0f, timeline.Duration));
        if (isPlaying) StopPlay();
        MarkDirty();
    }

    private Stopwatch playStopwatch;
    private void BtnPlay_Click(object? sender, RoutedEventArgs e)
    {
        if (isPlaying) return;
        isPlaying = true;

        var playbackStartTime = currentTime;
        playStopwatch = Stopwatch.StartNew();

        HashSet<(int track, int evt)> played = new HashSet<(int track, int evt)>();
        for (int ti = 0; ti < currentScene.Tracks.Count; ti++)
        {
            var track = currentScene.Tracks[ti];
            for (int i = 0; i < track.ChoreoEvents.Count; i++)
            {
                var evt = track.ChoreoEvents[i];
                if (evt.EventType != ChoreoEventType.PlayMorph) continue;
                if (playbackStartTime > evt.Time + GetEventDisplayDuration(evt))
                    played.Add((ti, i));
            }
        }

        playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        playTimer.Tick += (s, e) =>
        {
            currentTime = playbackStartTime + (float)playStopwatch.Elapsed.TotalSeconds;

            for (int ti = 0; ti < currentScene.Tracks.Count; ti++)
            {
                var track = currentScene.Tracks[ti];
                for (int i = 0; i < track.ChoreoEvents.Count; i++)
                {
                    var evt = track.ChoreoEvents[i];

                    if (evt.EventType != ChoreoEventType.PlayMorph) continue;
                    if (string.IsNullOrEmpty(evt.Target)) continue;

                    if (played.Contains((ti, i))) continue;

                    if (currentTime < evt.Time) continue;

                    played.Add((ti, i));

                    try
                    {
                        string fullPath = System.IO.Path.Combine(GlobalEditorData.WorkingDirectory, evt.Target);
                        if (!File.Exists(fullPath)) continue;
                        var morphData = CMorphAnimData.LoadFromFile(fullPath);
                        if (string.IsNullOrEmpty(morphData.AudioPath)) continue;

                        float dur = (morphData.EndFrame - morphData.StartFrame) / (float)SceneTimelineRenderer.FPS;
                        float offset = Math.Max(0, currentTime - evt.Time);

                        string audioPath = System.IO.Path.Combine(fullPath, morphData.AudioPath);
                        if (!File.Exists(audioPath)) continue;
                        audioPlayer.PlaySceneAudio(audioPath, offset);
                    }
                    catch { }
                }
            }

            if (currentTime >= timeline.Duration) { StopPlay(); return; }
            Redraw();
        };
        playTimer.Start();
    }

    private void BtnStop_Click(object? sender, RoutedEventArgs e) => StopPlay();

    private void StopPlay()
    {
        isPlaying = false;
        playTimer?.Stop();
        playTimer = null;
        audioPlayer.StopSceneAudio();
        Redraw();
    }

    private void BoxFrameStart_TextChanged(object? sender, TextChangedEventArgs e) => ApplyFrameRange();
    private void BoxFrameEnd_TextChanged(object? sender, TextChangedEventArgs e) => ApplyFrameRange();

    private void ApplyFrameRange()
    {
        if (frameInputUpdating) return;
        if (!int.TryParse(boxFrameStart.Text, out int start)) return;
        if (!int.TryParse(boxFrameEnd.Text, out int end)) return;
        if (end <= start) return;
        timeline.SetDuration(start, end);
        currentScene.StartFrame = start;
        currentScene.EndFrame = end;
        currentTime = Math.Clamp(currentTime, 0f, timeline.Duration);
        MarkDirty();
    }

    private void NewChoreo(object? sender, RoutedEventArgs e)
    {
        try
        {
            currentScene = new ChoreoScene();
            currentScene.StartFrame = 0;
            currentScene.EndFrame = 100;
            selectedTrackIndex = -1;
            selectedEventIndex = -1;

            frameInputUpdating = true;
            boxFrameStart.Text = currentScene.StartFrame.ToString();
            boxFrameEnd.Text = currentScene.EndFrame.ToString();
            frameInputUpdating = false;

            timeline.SetDuration(currentScene.StartFrame, currentScene.EndFrame);
            currentTime = 0f;
            MarkDirty();
            RefreshProperties();
        }
        catch (Exception ex) { Console.WriteLine($"[Choreo] Load failed: {ex.Message}"); }
    }

    private async void OpenChoreo(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var files = await topLevel!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Choreo Scene",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Choreo Scene") { Patterns = new[] { "*.choreo" } } }
        });
        if (files == null || files.Count == 0) return;
        try
        {
            currentScene = await ChoreoScene.LoadFromFile(files[0].Path.LocalPath);
            selectedTrackIndex = -1;
            selectedEventIndex = -1;

            frameInputUpdating = true;
            boxFrameStart.Text = currentScene.StartFrame.ToString();
            boxFrameEnd.Text = currentScene.EndFrame.ToString();
            frameInputUpdating = false;

            timeline.SetDuration(currentScene.StartFrame, currentScene.EndFrame);
            currentTime = 0f;
            MarkDirty();
            RefreshProperties();
        }
        catch (Exception ex) { Console.WriteLine($"[Choreo] Load failed: {ex.Message}"); }
    }

    private async void SaveChoreo(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var file = await topLevel!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Choreo Scene",
            DefaultExtension = "choreo",
            FileTypeChoices = new[] { new FilePickerFileType("Choreo Scene") { Patterns = new[] { "*.choreo" } } }
        });
        if (file == null) return;
        try { ChoreoScene.WriteToFile(currentScene, file.Path.LocalPath); }
        catch (Exception ex) { Console.WriteLine($"[Choreo] Save failed: {ex.Message}"); }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopPlay();
        audioPlayer.Dispose();
        base.OnDetachedFromVisualTree(e);
    }
}