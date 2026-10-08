using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Chisel.EXScript;
public class EXScriptTokenizer
{
    string source;
    int pos;
    int line = 1;
    List<EXToken> tokens = new();

    public List<EXToken> Tokenize(string input)
    {
        source = input;
        pos = 0;

        while (!AtEnd())
        {
            ScanToken();
        }

        tokens.Add(new EXToken { Type = EXTokenType.Eof, Text = "", Line = line });
        return tokens;
    }
    void ScanToken()
    {
        int startPos = pos;
        char c = Consume();

        switch (c)
        {
            case ' ':
            case '\t':
            case '\r':
                return;

            case '\n':
                line++;
                return;

            case '{':
                tokens.Add(new EXToken { Type = EXTokenType.OpenBrace, Text = "{", Line = line, Offset = startPos, Length = pos - startPos });
                return;

            case '}':
                tokens.Add(new EXToken { Type = EXTokenType.CloseBrace, Text = "}", Line = line, Offset = startPos, Length = pos - startPos });
                return;

            case '(':
                tokens.Add(new EXToken { Type = EXTokenType.OpenParen, Text = "(", Line = line, Offset = startPos, Length = pos - startPos });
                return;

            case ')':
                tokens.Add(new EXToken { Type = EXTokenType.CloseParen, Text = ")", Line = line, Offset = startPos, Length = pos - startPos });
                return;

            case ',':
                tokens.Add(new EXToken { Type = EXTokenType.Comma, Text = ",", Line = line, Offset = startPos, Length = pos - startPos });
                return;

            case ';':
                tokens.Add(new EXToken { Type = EXTokenType.Semicolon, Text = ";", Line = line, Offset = startPos, Length = pos - startPos });
                return;

            case '.':
                tokens.Add(new EXToken { Type = EXTokenType.Dot, Text = ".", Line = line, Offset = startPos, Length = pos - startPos });
                return;

            case '[':
                tokens.Add(new EXToken { Type = EXTokenType.OpenSquare, Text = "[", Line = line, Offset = startPos, Length = pos - startPos });
                return;

            case ']':
                tokens.Add(new EXToken { Type = EXTokenType.CloseSquare, Text = "]", Line = line, Offset = startPos, Length = pos - startPos });
                return;

            case '<':
                if (Peek() == '=')
                {
                    Consume();
                    tokens.Add(new EXToken { Type = EXTokenType.LessEquals, Text = "<=", Line = line, Offset = startPos, Length = pos - startPos });
                }
                else
                {
                    tokens.Add(new EXToken { Type = EXTokenType.Less, Text = "<", Line = line, Offset = startPos, Length = pos - startPos });
                }
                return;

            case '>':
                if (Peek() == '=')
                {
                    Consume();
                    tokens.Add(new EXToken { Type = EXTokenType.GreaterEquals, Text = ">=", Line = line, Offset = startPos, Length = pos - startPos });
                }
                else
                {
                    tokens.Add(new EXToken { Type = EXTokenType.Greater, Text = ">", Line = line, Offset = startPos, Length = pos - startPos });
                }
                return;

            case '&':
                if (Peek() == '&')
                {
                    Consume();
                    tokens.Add(new EXToken { Type = EXTokenType.AmpAmp, Text = "&&", Line = line, Offset = startPos, Length = pos - startPos });
                }
                else
                {
                    throw new Exception($"EXScript parse: unexpected character '&' (line: {line}). Did you mean '&&'?");
                }
                return;

            case '|':
                if (Peek() == '|')
                {
                    Consume();
                    tokens.Add(new EXToken { Type = EXTokenType.PipePipe, Text = "||", Line = line, Offset = startPos, Length = pos - startPos });
                }
                else
                {
                    throw new Exception($"EXScript parse: unexpected character '|' (line: {line}). Did you mean '||'?");
                }
                return;

            case '=':
                if (Peek() == '=')
                {
                    Consume();
                    tokens.Add(new EXToken { Type = EXTokenType.EqualsEquals, Text = "==", Line = line, Offset = startPos, Length = pos - startPos });
                }
                else
                {
                    tokens.Add(new EXToken { Type = EXTokenType.Equals, Text = "=", Line = line, Offset = startPos, Length = pos - startPos });
                }
                return;

            case '!':
                if (Peek() == '=')
                {
                    Consume();
                    tokens.Add(new EXToken { Type = EXTokenType.NotEquals, Text = "!=", Line = line, Offset = startPos, Length = pos - startPos });
                }
                else
                {
                    tokens.Add(new EXToken { Type = EXTokenType.Bang, Text = "!", Line = line, Offset = startPos, Length = pos - startPos });
                }
                return;
            case '+':
                if (Peek() == '=')
                {
                    Consume();
                    tokens.Add(new EXToken { Type = EXTokenType.PlusEquals, Text = "+=", Line = line, Offset = startPos, Length = pos - startPos });
                }
                else
                if (Peek() == '+')
                {
                    Consume();
                    tokens.Add(new EXToken { Type = EXTokenType.PlusPlus, Text = "++", Line = line, Offset = startPos, Length = pos - startPos });
                }
                else
                {
                    tokens.Add(new EXToken { Type = EXTokenType.Plus, Text = "+", Line = line, Offset = startPos, Length = pos - startPos });
                }
                return;
            case '-':
                if (Peek() == '=')
                {
                    Consume();
                    tokens.Add(new EXToken { Type = EXTokenType.MinusEquals, Text = "-=", Line = line, Offset = startPos, Length = pos - startPos });
                }
                else
                if (Peek() == '-')
                {
                    Consume();
                    tokens.Add(new EXToken { Type = EXTokenType.MinusMinus, Text = "--", Line = line, Offset = startPos, Length = pos - startPos });
                }
                else
                {
                    tokens.Add(new EXToken { Type = EXTokenType.Minus, Text = "-", Line = line, Offset = startPos, Length = pos - startPos });
                }
                return;
            case '*':
                if (Peek() == '=')
                {
                    Consume();
                    tokens.Add(new EXToken { Type = EXTokenType.StarEquals, Text = "*=", Line = line, Offset = startPos, Length = pos - startPos });
                }
                else
                {
                    tokens.Add(new EXToken { Type = EXTokenType.Star, Text = "*", Line = line, Offset = startPos, Length = pos - startPos });
                }
                return;
            case '/':
                if (Peek() == '/')
                {
                    while (!AtEnd() && Peek() != '\n')
                    {
                        Consume();
                    }
                    tokens.Add(new EXToken { Type = EXTokenType.Comment, Text = "", Line = line, Offset = startPos, Length = pos - startPos });
                    return;
                }
                if (Peek() == '=')
                {
                    Consume();
                    tokens.Add(new EXToken { Type = EXTokenType.SlashEquals, Text = "/=", Line = line, Offset = startPos, Length = pos - startPos });
                }
                else
                {
                    tokens.Add(new EXToken { Type = EXTokenType.Slash, Text = "/", Line = line, Offset = startPos, Length = pos - startPos });
                }
                return;
            case '%':
                if (Peek() == '=')
                {
                    Consume();
                    tokens.Add(new EXToken { Type = EXTokenType.PercentEquals, Text = "%=", Line = line, Offset = startPos, Length = pos - startPos });
                }
                else
                {
                    tokens.Add(new EXToken { Type = EXTokenType.Percent, Text = "%", Line = line, Offset = startPos, Length = pos - startPos });
                }
                return;
            case '"':
                {
                    var sb = new StringBuilder();

                    while (Peek() != '"')
                    {
                        if (AtEnd())
                            throw new Exception($"EXScript parse: malformed string (line: {line}).");

                        sb.Append(Consume());
                    }

                    Consume();

                    tokens.Add(new EXToken { Type = EXTokenType.String, Text = sb.ToString(), Line = line, Offset = startPos, Length = pos - startPos });
                }
                return;
            case '$':
                {
                    if (Peek() != '"')
                        throw new Exception($"EXScript parse: malformed interpolated string (line: {line}).");
                    Consume(); // consume the opening quote

                    var sb = new StringBuilder();

                    while (Peek() != '"')
                    {
                        if (AtEnd())
                            throw new Exception($"EXScript parse: malformed string (line: {line}).");

                        sb.Append(Consume());
                    }

                    Consume();

                    tokens.Add(new EXToken { Type = EXTokenType.InterpolatedString, Text = sb.ToString(), Line = line, Offset = startPos, Length = pos - startPos });
                }
                return;

            case '0':
            case '1':
            case '2':
            case '3':
            case '4':
            case '5':
            case '6':
            case '7':
            case '8':
            case '9':
                {
                    var sb = new StringBuilder();
                    sb.Append(c);

                    while (char.IsDigit(Peek()) || Peek() == '.')
                    {
                        sb.Append(Consume());
                    }

                    double value = double.Parse(sb.ToString(), CultureInfo.InvariantCulture);

                    tokens.Add(new EXToken { Type = EXTokenType.Number, Text = value.ToString(), Line = line, Offset = startPos, Length = pos - startPos });
                }
                return;
            default:

                if (IsIdentifierStart(c))
                {
                    var sb = new StringBuilder();
                    sb.Append(c);

                    while (IsIdentifierPart(Peek()))
                    {
                        sb.Append(Consume());
                    }
                    var str = sb.ToString();

                    tokens.Add(new EXToken
                    {
                        Type = str switch
                        {
                            "var" => EXTokenType.Var,
                            "if" => EXTokenType.If,
                            "else" => EXTokenType.Else,
                            "while" => EXTokenType.While,
                            "is" => EXTokenType.Is,
                            "not" => EXTokenType.Not,
                            "null" => EXTokenType.Null,
                            "for" => EXTokenType.For,
                            "wait" => EXTokenType.Wait,
                            "const" => EXTokenType.Const,
                            "break" => EXTokenType.Break,
                            "continue" => EXTokenType.Continue,
                            "return" => EXTokenType.Return,
                            "function" => EXTokenType.Function,
                            "true" => EXTokenType.Boolean,
                            "false" => EXTokenType.Boolean,
                            _ => EXTokenType.Identifier
                        },
                        Text = str,
                        Line = line,
                        Offset = startPos,
                        Length = pos - startPos
                    });
                }
                else
                {
                    throw new Exception($"EXScript parse: unexpected character '{c}' (line: {line}).");
                }

                return;
        }
    }

    bool AtEnd() => pos >= source.Length;
    char Consume() => source[pos++];
    char Peek() => AtEnd() ? '\0' : source[pos];
    bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
    bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';
}