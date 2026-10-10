using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using Rockwall2.Editor.Mapper.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Rockwall2;

public partial class ScriptEditorWindow : Window
{
    public int OutputIndex => outputIndex;

    private readonly EntityInspectorWindow owner;
    private int outputIndex;

    private bool isWindowDragInEffect = false;
    private Point cursorDragStart = new(0, 0);
    private CompletionWindow completionWindow;

    private OverloadInsightWindow currentInsight;
    private List<string> currentInsightHeaders; // used to detect "still the same call" and avoid resetting overload selection every keystroke

    public ScriptEditorWindow() : this(null!, -1) { }

    public ScriptEditorWindow(EntityInspectorWindow owner, int outputIndex)
    {
        InitializeComponent();
        this.owner = owner;
        this.outputIndex = outputIndex;

        if (Design.IsDesignMode) return;

        titleBar.PointerPressed += TitleBar_PointerPressed;
        titleBar.PointerMoved += TitleBar_PointerMoved;
        titleBar.PointerReleased += TitleBar_PointerReleased;
        scriptText.TextChanged += ScriptText_TextChanged;
        scriptText.TextArea.TextEntered += TextArea_TextEntered;
        scriptText.TextArea.TextEntering += TextArea_TextEntering;
        scriptText.TextArea.Caret.PositionChanged += Caret_PositionChanged;
        scriptText.TextArea.TextView.LineTransformers.Add(new EXScriptColorizer());

        scriptText.WordWrap = true;

        scriptText.Text = owner.GetScriptSource(outputIndex);
        Title = $"Script - Output {outputIndex + 1}";
    }

    protected override void OnClosed(EventArgs e)
    {
        titleBar.PointerPressed -= TitleBar_PointerPressed;
        titleBar.PointerMoved -= TitleBar_PointerMoved;
        titleBar.PointerReleased -= TitleBar_PointerReleased;
        scriptText.TextChanged -= ScriptText_TextChanged;
        scriptText.TextArea.TextEntered -= TextArea_TextEntered;
        scriptText.TextArea.TextEntering -= TextArea_TextEntering;
        scriptText.TextArea.Caret.PositionChanged -= Caret_PositionChanged;
        currentInsight?.Close();
        base.OnClosed(e);
    }

    public void Retarget(int newOutputIndex)
    {
        outputIndex = newOutputIndex;
        scriptText.Text = owner.GetScriptSource(outputIndex);
        Title = $"Script - Output {outputIndex + 1}";
    }

    private void ScriptText_TextChanged(object? sender, EventArgs e)
        => owner.SetScriptSource(outputIndex, scriptText.Text ?? string.Empty);

    private void TextArea_TextEntered(object sender, TextInputEventArgs e)
    {
        if (e.Text == ".")
        {
            ShowCompletion(scriptText.CaretOffset);
            return;
        }

        if (completionWindow != null) return;

        if (e.Text.Length == 1 && (char.IsLetter(e.Text[0]) || e.Text[0] == '_'))
            ShowCompletion(FindIdentifierStart());
    }

    private int FindIdentifierStart()
    {
        var text = scriptText.Text ?? "";
        int i = scriptText.CaretOffset;
        while (i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '_'))
            i--;
        return i;
    }

    private void TextArea_TextEntering(object sender, TextInputEventArgs e)
    {
        if (e.Text.Length > 0 && completionWindow != null && !char.IsLetterOrDigit(e.Text[0]) && e.Text[0] != '_')
        {
            completionWindow.CompletionList.RequestInsertion(e);
        }
    }

    private void Caret_PositionChanged(object? sender, EventArgs e) => RefreshSignatureHelp();

    private void RefreshSignatureHelp()
    {
        var signatures = EXSignatureHelp.GetSignatures(scriptText.Text ?? "", scriptText.CaretOffset);

        if (signatures.Count == 0)
        {
            currentInsight?.Close();
            currentInsight = null;
            currentInsightHeaders = null;
            return;
        }

        var headers = signatures.Select(s => s.Header).ToList();

        if (currentInsight != null && currentInsightHeaders != null && headers.SequenceEqual(currentInsightHeaders))
            return;

        currentInsight?.Close();

        var insight = new OverloadInsightWindow(scriptText.TextArea) { Provider = new EXOverloadProvider(signatures) };
        insight.Closed += (_, _) => { if (currentInsight == insight) { currentInsight = null; currentInsightHeaders = null; } };
        currentInsight = insight;
        currentInsightHeaders = headers;
        insight.Show();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.Maximized || WindowState == WindowState.FullScreen) return;
        isWindowDragInEffect = true;
        cursorDragStart = e.GetPosition(this);
    }

    private void TitleBar_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!isWindowDragInEffect) return;
        Point current = e.GetPosition(this);
        Point delta = current - cursorDragStart;
        Position = this.PointToScreen(delta);
    }

    private void TitleBar_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        isWindowDragInEffect = false;
        owner.OnScriptWindowDragEnded(this);
    }

    private void ShowCompletion(int startOffset)
    {
        completionWindow?.Close();

        var items = EXCompletionEngine.GetCompletions(scriptText.Text ?? "", scriptText.CaretOffset);
        if (items.Count == 0) return;

        var newWindow = new CompletionWindow(scriptText.TextArea) { StartOffset = startOffset };
        newWindow.CompletionList.Background = new SolidColorBrush(Color.Parse("#1E1E1E"));
        newWindow.CompletionList.BorderBrush = new SolidColorBrush(Color.Parse("#444444"));
        newWindow.CompletionList.FontFamily = new FontFamily("Consolas,monospace");
        newWindow.CompletionList.FontSize = 13;

        foreach (var item in items)
            newWindow.CompletionList.CompletionData.Add(new EXCompletionData(item));

        newWindow.Closed += (_, _) => { if (completionWindow == newWindow) completionWindow = null; };
        completionWindow = newWindow;
        newWindow.Show();
    }
}