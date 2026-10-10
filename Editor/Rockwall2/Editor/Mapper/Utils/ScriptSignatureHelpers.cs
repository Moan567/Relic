using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using Relic.EXScript;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Rockwall2.Editor.Mapper.Utils;
public static class EXSignatureHelp
{
    public record Signature(string Header, string Content);

    public static List<Signature> GetSignatures(string fullText, int caretOffset)
    {
        var schema = EXHostSchemaLoader.Load();
        string prefixText = fullText.Substring(0, Math.Min(caretOffset, fullText.Length));

        List<EXToken> tokens;
        try { tokens = new EXScriptTokenizer().Tokenize(prefixText); }
        catch { return new List<Signature>(); }
        if (tokens.Count > 0 && tokens[^1].Type == EXTokenType.Eof) tokens.RemoveAt(tokens.Count - 1);

        // walk forward tracking paren depth; each open paren remembers the identifier
        // token right before it (a "call" paren), or -1 for plain grouping like (1 + 2)
        var parenStack = new Stack<int>();
        for (int idx = 0; idx < tokens.Count; idx++)
        {
            if (tokens[idx].Type == EXTokenType.OpenParen)
            {
                int callNameIndex = (idx > 0 && tokens[idx - 1].Type == EXTokenType.Identifier) ? idx - 1 : -1;
                parenStack.Push(callNameIndex);
            }
            else if (tokens[idx].Type == EXTokenType.CloseParen)
            {
                if (parenStack.Count > 0) parenStack.Pop();
            }
        }

        if (parenStack.Count == 0) return new List<Signature>();
        int nameIdx = parenStack.Peek();
        if (nameIdx < 0) return new List<Signature>(); // innermost open paren is grouping, not a call

        string callName = tokens[nameIdx].Text;
        bool hasReceiver = nameIdx >= 1 && tokens[nameIdx - 1].Type == EXTokenType.Dot;

        if (!hasReceiver)
        {
            var results = new List<Signature>();
            foreach (var fn in EXStdLib.GetRegisteredFunctions().Where(f => f.Name == callName).OrderBy(f => f.Arity))
                results.Add(new Signature($"{callName}({FormatStdLibParams(fn)})", fn.Doc ?? ""));
            foreach (var gf in schema.GlobalFunctions.Where(f => f.Name == callName))
                results.Add(new Signature($"{callName}({FormatSchemaParams(gf.Parameters, gf.Variadic, gf.VariadicElementType)})", gf.Doc ?? ""));
            return results;
        }

        // walk the receiver chain backward from just before the dot preceding callName
        var chain = new List<EXToken>();
        int i = nameIdx - 2;
        while (i >= 0 && tokens[i].Type == EXTokenType.Identifier)
        {
            chain.Insert(0, tokens[i]);
            if (i - 1 >= 0 && tokens[i - 1].Type == EXTokenType.Dot) i -= 2;
            else break;
        }
        if (chain.Count == 0) return new List<Signature>();

        string currentType = schema.ReservedIdentifiers.TryGetValue(chain[0].Text, out var reserved)
            ? reserved.Type : "Entity";
        for (int idx = 1; idx < chain.Count; idx++)
        {
            if (!schema.Types.TryGetValue(currentType, out var t)) return new List<Signature>();
            var m = t.Members.FirstOrDefault(mm => mm.Name == chain[idx].Text);
            if (m == null || string.IsNullOrEmpty(m.ValueType)) return new List<Signature>();
            currentType = m.ValueType;
        }

        var memberResults = new List<Signature>();
        if (schema.Types.TryGetValue(currentType, out var finalType))
            foreach (var m in finalType.Members.Where(m => m.Name == callName && m.Kind == "method"))
                memberResults.Add(new Signature($"{callName}({FormatSchemaParams(m.Parameters, m.Variadic, m.VariadicElementType)})", m.Doc ?? ""));

        if (chain.Count == 1 && EXHostCompletionData.PlacedEntityNames().TryGetValue(chain[0].Text, out var classname)
            && EXHostCompletionData.InputsByClassname().TryGetValue(classname, out var inputs)
            && inputs.Contains(callName))
        {
            memberResults.Add(new Signature($"{callName}(...)", $"Forwards to {classname}.CallInput(\"{callName}\", ...)"));
        }

        return memberResults;
    }

    private static string FormatSchemaParams(List<EXParamDef> parameters, bool variadic, string variadicElementType)
    {
        if (variadic) return $"...: {(string.IsNullOrEmpty(variadicElementType) ? "any" : variadicElementType)}";
        if (parameters == null || parameters.Count == 0) return "";
        return string.Join(", ", parameters.Select(p =>
            $"{p.Name}{(p.Optional ? "?" : "")}: {(string.IsNullOrEmpty(p.Type) ? "any" : p.Type)}"));
    }

    private static string FormatStdLibParams(EXStdLib.EXStdLibFunctionInfo fn)
    {
        if (fn.Arity < 0) return $"...: {(string.IsNullOrEmpty(fn.VariadicElementType) ? "any" : fn.VariadicElementType)}";
        if (fn.Parameters == null || fn.Parameters.Length == 0) return "";
        return string.Join(", ", fn.Parameters.Select(p => $"{p.Name}: {(string.IsNullOrEmpty(p.Type) ? "any" : p.Type)}"));
    }
}

public class EXOverloadProvider : IOverloadProvider, INotifyPropertyChanged
{
    private readonly List<EXSignatureHelp.Signature> signatures;
    private int selectedIndex;

    public EXOverloadProvider(List<EXSignatureHelp.Signature> signatures) => this.signatures = signatures;

    public int Count => signatures.Count;

    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            selectedIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentIndexText));
            OnPropertyChanged(nameof(CurrentHeader));
            OnPropertyChanged(nameof(CurrentContent));
        }
    }

    public string CurrentIndexText => $"{SelectedIndex + 1}/{Count}";
    public object CurrentHeader => BuildWrapped(signatures[SelectedIndex].Header, bold: true);
    public object CurrentContent => BuildWrapped(signatures[SelectedIndex].Content, bold: false);

    private static Control BuildWrapped(string text, bool bold) => new TextBlock
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 420,
        FontFamily = new FontFamily("Consolas,monospace"),
        FontSize = 13,
        FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
        Foreground = new SolidColorBrush(Color.Parse("#DCDCDC"))
    };

    public event PropertyChangedEventHandler PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}