using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Relic.EXScript;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Mapper.Utils;

public record EXCompletionItem(string Label, string Kind, string Doc);

public static class EXCompletionEngine
{
    private static readonly string[] Keywords =
    {
        "var", "const", "if", "else", "while", "for", "function",
        "return", "break", "continue", "wait", "is", "not",
        "true", "false", "null"
    };
    public static List<EXCompletionItem> GetCompletions(string fullText, int caretOffset)
    {
        var schema = EXHostSchemaLoader.Load();
        string prefixText = fullText.Substring(0, Math.Min(caretOffset, fullText.Length));

        List<EXToken> tokens;
        try { tokens = new EXScriptTokenizer().Tokenize(prefixText); }
        catch { return new List<EXCompletionItem>(); }

        if (tokens.Count > 0 && tokens[^1].Type == EXTokenType.Eof) tokens.RemoveAt(tokens.Count - 1);
        if (tokens.Count == 0) return GlobalCompletions(schema, "", tokens);

        var last = tokens[^1];
        string typedPrefix = "";
        int chainEnd;

        if (last.Type == EXTokenType.Identifier && tokens.Count >= 2 && tokens[^2].Type == EXTokenType.Dot)
        {
            typedPrefix = last.Text;
            chainEnd = tokens.Count - 2;
        }
        else if (last.Type == EXTokenType.Identifier)
        {
            if (tokens.Count >= 2 && tokens[^2].Type is EXTokenType.Var or EXTokenType.Const)
                return new List<EXCompletionItem>();
            return GlobalCompletions(schema, last.Text, tokens);
        }
        else if (last.Type == EXTokenType.Dot)
        {
            chainEnd = tokens.Count - 1;
        }
        else
        {
            return GlobalCompletions(schema, last.Text, tokens);
        }

        // walk backward collecting the alternating Identifier . Identifier . Identifier run
        var chain = new List<EXToken>();
        int i = chainEnd - 1;
        while (i >= 0 && tokens[i].Type == EXTokenType.Identifier)
        {
            chain.Insert(0, tokens[i]);
            if (i - 1 >= 0 && tokens[i - 1].Type == EXTokenType.Dot) i -= 2;
            else break;
        }

        return chain.Count == 0
            ? new List<EXCompletionItem>()
            : MemberCompletions(schema, chain, typedPrefix);
    }

    private static List<EXCompletionItem> MemberCompletions(EXHostSchema schema, List<EXToken> chain, string prefix)
    {
        var results = new List<EXCompletionItem>();
        string firstName = chain[0].Text;

        // first hop: reserved identifiers use their declared type; anything else assumed Entity, per design
        string currentType = schema.ReservedIdentifiers.TryGetValue(firstName, out var reserved)
            ? reserved.Type
            : "Entity";

        // subsequent hops: follow each member's declared valueType. EG. Entity.position -> Vector3D
        for (int idx = 1; idx < chain.Count; idx++)
        {
            if (!schema.Types.TryGetValue(currentType, out var typeDef)) return results;
            var member = typeDef.Members.FirstOrDefault(m => m.Name == chain[idx].Text);
            if (member == null || string.IsNullOrEmpty(member.ValueType)) return results;
            currentType = member.ValueType;
        }

        if (schema.Types.TryGetValue(currentType, out var finalType))
        {
            foreach (var m in finalType.Members.GroupBy(m => m.Name).Select(g => g.First()))
            {
                if (m.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string doc = m.ReadOnly ? $"{m.Doc} (read-only)".Trim() : m.Doc;
                    results.Add(new EXCompletionItem(m.Name, m.Kind, doc));
                }
            }
        }

        // real CallInput names only make sense at the first hop, "_self.position.SomeInput" isn't possible
        if (chain.Count == 1 && EXHostCompletionData.PlacedEntityNames().TryGetValue(firstName, out var classname)
            && EXHostCompletionData.InputsByClassname().TryGetValue(classname, out var inputs))
        {
            foreach (var input in inputs)
            {
                if (!schema.HiddenInputs.Contains(input) && input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new EXCompletionItem(input, "input", $"Forwards to {classname}.CallInput(\"{input}\", ...)"));
                }
            }
        }

        return results;
    }
    private static List<EXCompletionItem> GlobalCompletions(EXHostSchema schema, string prefix, List<EXToken> tokensUpToCaret)
    {
        var results = new List<EXCompletionItem>();

        foreach (var kw in Keywords)
            if (kw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                results.Add(new EXCompletionItem(kw, "keyword", null));

        foreach (var id in schema.ReservedIdentifiers)
            if (id.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                results.Add(new EXCompletionItem(id.Key, "identifier", id.Value.Doc));

        foreach (var fn in schema.GlobalFunctions.GroupBy(f => f.Name).Select(g => g.First()))
            if (fn.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                results.Add(new EXCompletionItem(fn.Name, "function", fn.Doc));

        foreach (var item in EXStdLibCompletionData.GetCompletionItems())
            if (item.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                results.Add(item);

        foreach (var kvp in EXHostCompletionData.PlacedEntityNames())
            if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                results.Add(new EXCompletionItem(kvp.Key, "entity", $"Placed {kvp.Value}"));

        var (locals, functionNames) = CollectVisibleLocals(tokensUpToCaret);

        foreach (var name in locals)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                results.Add(new EXCompletionItem(name, "local", null));

        foreach (var name in functionNames)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                results.Add(new EXCompletionItem(name, "function", "User-defined function"));

        return results;
    }

    private static (List<string> locals, List<string> functionNames) CollectVisibleLocals(List<EXToken> tokens)
    {
        var globalConsts = new List<string>();
        var functionNames = new List<string>();
        var topLevelScopeStack = new List<List<string>> { new() };
        var functionScopeStack = new List<List<string>>();
        bool inFunction = false;

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];

            if (t.Type == EXTokenType.Function && !inFunction)
            {
                int j = i + 1;
                if (j < tokens.Count && tokens[j].Type == EXTokenType.Identifier)
                    functionNames.Add(tokens[j++].Text);

                var paramNames = new List<string>();
                if (j < tokens.Count && tokens[j].Type == EXTokenType.OpenParen)
                {
                    j++;
                    while (j < tokens.Count && tokens[j].Type != EXTokenType.CloseParen)
                    {
                        if (tokens[j].Type == EXTokenType.Identifier) paramNames.Add(tokens[j].Text);
                        j++;
                    }
                }

                inFunction = true;
                functionScopeStack.Clear();
                functionScopeStack.Add(paramNames); 
                continue;
            }

            if (t.Type == EXTokenType.OpenBrace)
            {
                (inFunction ? functionScopeStack : topLevelScopeStack).Add(new List<string>());
            }
            else if (t.Type == EXTokenType.CloseBrace)
            {
                if (inFunction)
                {
                    if (functionScopeStack.Count > 1) functionScopeStack.RemoveAt(functionScopeStack.Count - 1);
                    else { inFunction = false; functionScopeStack.Clear(); }
                }
                else if (topLevelScopeStack.Count > 1)
                {
                    topLevelScopeStack.RemoveAt(topLevelScopeStack.Count - 1);
                }
            }
            else if (t.Type is EXTokenType.Var or EXTokenType.Const
                     && i + 1 < tokens.Count && tokens[i + 1].Type == EXTokenType.Identifier)
            {
                string name = tokens[i + 1].Text;

                if (!inFunction && t.Type == EXTokenType.Const && topLevelScopeStack.Count == 1)
                    globalConsts.Add(name);

                (inFunction ? functionScopeStack : topLevelScopeStack)[^1].Add(name);
            }
        }

        var locals = inFunction
            ? functionScopeStack.SelectMany(s => s).Concat(globalConsts).Distinct().ToList()
            : topLevelScopeStack.SelectMany(s => s).Distinct().ToList();

        return (locals, functionNames);
    }
}


public class EXScriptColorizer : DocumentColorizingTransformer
{
    private static readonly IBrush KeywordBrush = new SolidColorBrush(Color.Parse("#C586C0"));
    private static readonly IBrush StringBrush = new SolidColorBrush(Color.Parse("#CE9178"));
    private static readonly IBrush NumberBrush = new SolidColorBrush(Color.Parse("#B5CEA8"));
    private static readonly IBrush LiteralBrush = new SolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush CommentBrush = new SolidColorBrush(Color.Parse("#6A9955"));
    private static readonly IBrush CallBrush = new SolidColorBrush(Color.Parse("#DCDCAA"));

    private static readonly IBrush VariableBrush = new SolidColorBrush(Color.Parse("#9CDCFE"));
    private static readonly IBrush MemberBrush = new SolidColorBrush(Color.Parse("#4FC1FF"));

    private List<EXToken> tokens = new();
    private string lastText;
    protected override void ColorizeLine(DocumentLine line)
    {
        string fullText = CurrentContext.Document.Text;
        if (fullText != lastText)
        {
            try { tokens = new EXScriptTokenizer().Tokenize(fullText); }
            catch { return; }
            lastText = fullText;
        }

        int lineStart = line.Offset, lineEnd = line.EndOffset;
        for (int i = 0; i < tokens.Count; i++)
        {
            var tok = tokens[i];
            if (tok.Offset + tok.Length <= lineStart || tok.Offset >= lineEnd) continue;

            IBrush brush;
            if (tok.Type == EXTokenType.Identifier)
            {
                bool isCall = i + 1 < tokens.Count && tokens[i + 1].Type == EXTokenType.OpenParen;
                bool isMember = !isCall && i > 0 && tokens[i - 1].Type == EXTokenType.Dot;
                brush = isCall ? CallBrush : isMember ? MemberBrush : VariableBrush;
            }
            else
            {
                brush = tok.Type switch
                {
                    EXTokenType.If or EXTokenType.Else or EXTokenType.While or EXTokenType.For
                        or EXTokenType.Var or EXTokenType.Const or EXTokenType.Function
                        or EXTokenType.Return or EXTokenType.Break or EXTokenType.Continue
                        or EXTokenType.Is or EXTokenType.Not or EXTokenType.Wait => KeywordBrush,
                    EXTokenType.String or EXTokenType.InterpolatedString => StringBrush,
                    EXTokenType.Number => NumberBrush,
                    EXTokenType.Boolean or EXTokenType.Null => LiteralBrush,
                    EXTokenType.Comment => CommentBrush,
                    _ => null
                };
            }
            if (brush == null) continue;

            ChangeLinePart(Math.Max(tok.Offset, lineStart), Math.Min(tok.Offset + tok.Length, lineEnd),
                el => el.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}


public class EXCompletionData : ICompletionData
{
    private readonly EXCompletionItem item;
    private Control contentControl;

    public EXCompletionData(EXCompletionItem item) => this.item = item;

    public IImage Image => null;
    public string Text => item.Label; // still used internally for filter-matching as you type
    public object Content => contentControl ??= BuildContent();
    public object Description => item.Doc;
    public double Priority => KindPriority(item.Kind);

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        => textArea.Document.Replace(completionSegment, item.Label);

    private Control BuildContent()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };

        panel.Children.Add(new Border
        {
            Background = KindBrush(item.Kind),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = item.Kind,
                FontSize = 10,
                FontFamily = new FontFamily("Consolas,monospace"),
                Foreground = Brushes.Black
            }
        });

        panel.Children.Add(new TextBlock
        {
            Text = item.Label,
            FontSize = 13,
            FontFamily = new FontFamily("Consolas,monospace"),
            Foreground = new SolidColorBrush(Color.Parse("#DCDCDC"))
        });

        return panel;
    }

    private static IBrush KindBrush(string kind) => kind switch
    {
        "keyword" => new SolidColorBrush(Color.Parse("#C586C0")),
        "identifier" => new SolidColorBrush(Color.Parse("#569CD6")),
        "field" or "indexer" => new SolidColorBrush(Color.Parse("#4FC1FF")),
        "method" or "function" or "stdlib" => new SolidColorBrush(Color.Parse("#DCDCAA")),
        "input" or "entity" => new SolidColorBrush(Color.Parse("#4EC9B0")),
        "local" => new SolidColorBrush(Color.Parse("#9CDCFE")),
        _ => new SolidColorBrush(Color.Parse("#808080"))
    };

    private static double KindPriority(string kind) => kind switch
    {
        "local" => 3,
        "entity" or "input" => 2,
        _ => 0
    };
}