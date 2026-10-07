using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Scripting.ValueScript;
public class ScriptTokenizer
{
    string source;
    int pos;
    int line = 1;
    List<Token> tokens = new();

    public List<Token> Tokenize(string input)
    {
        source = input;
        pos = 0;

        while (!AtEnd())
        {
            ScanToken();
        }

        tokens.Add(new Token { Type = TokenType.Eof, Text = "", Line = line });
        return tokens;
    }
    void ScanToken()
    {
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
                tokens.Add(new Token { Type = TokenType.OpenBrace, Text = "{", Line = line });
                return;

            case '}':
                tokens.Add(new Token { Type = TokenType.CloseBrace, Text = "}", Line = line });
                return;

            case '(':
                tokens.Add(new Token { Type = TokenType.OpenParen, Text = "(", Line = line });
                return;

            case ')':
                tokens.Add(new Token { Type = TokenType.CloseParen, Text = ")", Line = line });
                return;

            case ',':
                tokens.Add(new Token { Type = TokenType.Comma, Text = ",", Line = line });
                return;

            case '=':
                tokens.Add(new Token { Type = TokenType.Equals, Text = "=", Line = line });
                return;

            case ':':
                tokens.Add(new Token { Type = TokenType.Colon, Text = ":", Line = line });
                return;
            case '+':
                tokens.Add(new Token { Type = TokenType.Plus, Text = "+", Line = line });
                return;
            case '-':
                tokens.Add(new Token { Type = TokenType.Minus, Text = "-", Line = line });
                return;
            case '*':
                tokens.Add(new Token { Type = TokenType.Star, Text = "*", Line = line });
                return;
            case '/':
                if (Peek() != '/')
                {
                    tokens.Add(new Token { Type = TokenType.Slash, Text = "*", Line = line });
                    return;
                }

                while (!AtEnd() && Peek() != '\n')
                {
                    Consume();
                }

                return;

            case '"':
                {
                    var sb = new StringBuilder();

                    while (Peek() != '"')
                    {
                        if (AtEnd())
                            throw new Exception($"Script parse: malformed string (line: {line}).");

                        sb.Append(Consume());
                    }

                    Consume();

                    tokens.Add(new Token { Type = TokenType.String, Text = sb.ToString(), Line = line });
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

                    tokens.Add(new Token { Type = TokenType.Number, Text = value.ToString(), Line = line });
                }
                return;
            case '!':
                {
                    var sb = new StringBuilder();

                    while (IsIdentifierPart(Peek()))
                    {
                        sb.Append(Consume());
                    }

                    if (sb.Length == 0)
                        throw new Exception($"Script parse: '!' not followed by a name (line: {line}).");

                    tokens.Add(new Token { Type = TokenType.Variable, Text = sb.ToString(), Line = line });
                }
                return;
            default:

                if (c == 't' || c == 'f')
                {
                    var sb = new StringBuilder();
                    sb.Append(c);
                    while (char.IsLetterOrDigit(Peek()))
                    {
                        sb.Append(Consume());
                    }
                    string text = sb.ToString();
                    if (text == "true" || text == "false")
                    {
                        tokens.Add(new Token { Type = TokenType.Boolean, Text = text, Line = line });
                    }
                    else
                    {
                        tokens.Add(new Token { Type = TokenType.Identifier, Text = text, Line = line });
                    }
                }
                else
                if (IsIdentifierStart(c))
                {
                    var sb = new StringBuilder();
                    sb.Append(c);

                    while (IsIdentifierPart(Peek()))
                    {
                        sb.Append(Consume());
                    }

                    tokens.Add(new Token { Type = TokenType.Identifier, Text = sb.ToString(), Line = line });
                }
                else
                {
                    throw new Exception($"Script parse: unexpected character '{c}' (line: {line}).");
                }

                return;
        }
    }

    bool AtEnd() => pos >= source.Length;
    char Consume() => source[pos++];
    char Peek() => AtEnd() ? '\0' : source[pos]; 
    bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_' || c == '$';
    bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '.';
}