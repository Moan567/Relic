using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Scripting.ValueScript;
public class ScriptParser
{
    List<Token> tokens;
    int pos;

    public ScriptBlock Parse(List<Token> input)
    {
        tokens = input;
        pos = 0;

        var root = new ScriptBlock();
        while (Peek().Type != TokenType.Eof)
        {
            root.Entries.Add(ParseEntry());
        }
        return root;
    }

    ScriptEntry ParseEntry()
    {
        var keyToken = Expect(TokenType.Identifier, "as entry key");
        int entryLine = keyToken.Line;

        ScriptEntry entry = new ScriptEntry();
        entry.Line = entryLine;
        entry.Key = keyToken.Text;

        switch(Peek().Type)
        {
            case TokenType.Equals: // Setting an identifier

                Consume();
                entry.Value = ParsePrimary();

                break;
            case TokenType.Colon: // Inheritance

                Consume();
                var baseToken = Expect(TokenType.Identifier,"as base reference after ':'");
                var block = ParseBlock();
                block.BaseRef = baseToken.Text;
                entry.Value = new ScriptValue { Kind = ScriptValueKind.Block, Block = block };

                break;
            case TokenType.OpenBrace: // Block

                var plainBlock = ParseBlock();
                entry.Value = new ScriptValue { Kind = ScriptValueKind.Block, Block = plainBlock };

                break;
            default:
                throw new Exception($"Script parse: nothing provided after {keyToken.Text} (line: {keyToken.Line})");
        }

        return entry;
    }
    ScriptValue ParseValue()
    {
        if (Check(TokenType.String))
        {
            var token = Consume();
            return new ScriptValue { Kind = ScriptValueKind.String, StringValue = token.Text };
        }

        if (Check(TokenType.Boolean))
        {
            var token = Consume();
            return new ScriptValue { Kind = ScriptValueKind.Boolean, BooleanValue = bool.Parse(token.Text) };
        }

        if (Check(TokenType.Number))
        {
            var token = Consume();
            return new ScriptValue { Kind = ScriptValueKind.Number, NumberValue = double.Parse(token.Text, CultureInfo.InvariantCulture) };
        }

        if (Check(TokenType.Variable))
        {
            var token = Consume();
            return new ScriptValue { Kind = ScriptValueKind.Variable, StringValue = token.Text };
        }

        if (Check(TokenType.OpenBrace))
        {
            var block = ParseBlock();
            return new ScriptValue { Kind = ScriptValueKind.Block, Block = block };
        }

        if (Check(TokenType.Identifier))
        {
            if (PeekAhead(1).Type == TokenType.OpenParen)
            {
                var call = ParseFunctionCall();
                return new ScriptValue { Kind = ScriptValueKind.FunctionCall, FunctionCall = call };
            }

            var token = Consume();
            return new ScriptValue { Kind = ScriptValueKind.Identifier, StringValue = token.Text };
        }

        throw new Exception($"Script parse: unexpected token {Peek().Type} while parsing value (line: {Peek().Line}).");
    }

    ScriptBlock ParseBlock()
    {
        Expect(TokenType.OpenBrace, "to begin block");

        var block = new ScriptBlock();

        while(!Check(TokenType.CloseBrace))
        {
            if(Check(TokenType.Identifier) && IsEntryStart(PeekAhead(1).Type))
            {
                block.Entries.Add(ParseEntry());
            }
            else
            {
                var value = ParsePrimary();
                block.Entries.Add(new ScriptEntry { Key = null, Value = value, Line = Peek().Line });

                if (!Check(TokenType.CloseBrace))
                    Expect(TokenType.Comma,"between array values");
            }
        }
        Expect(TokenType.CloseBrace, "to close block");

        return block;
    }

    ScriptFunctionCall ParseFunctionCall()
    {
        var nameToken = Expect(TokenType.Identifier, "as function name");
        Expect(TokenType.OpenParen, "to begin function");

        var call = new ScriptFunctionCall();
        call.Name = nameToken.Text;

        if (!Check(TokenType.CloseParen))
        {
            call.Arguments.Add(ParsePrimary());

            while(Check(TokenType.Comma))
            {
                Consume();
                call.Arguments.Add(ParsePrimary());
            }
        }

        Expect(TokenType.CloseParen, "to end function");

        if(Check(TokenType.OpenBrace))
        {
            call.TrailingBlock = ParseBlock();
        }

        return call;
    }
    ScriptValue ParseExpression() => ParseAdditive();

    ScriptValue ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (Check(TokenType.Plus) || Check(TokenType.Minus))
        {
            var op = Consume();
            var right = ParseMultiplicative();
            left = new ScriptValue { Kind = ScriptValueKind.BinaryOp, Operator = op.Text, Left = left, Right = right };
        }
        return left;
    }

    ScriptValue ParseMultiplicative()
    {
        var left = ParsePrimary();
        while (Check(TokenType.Star) || Check(TokenType.Slash))
        {
            var op = Consume();
            var right = ParsePrimary();
            left = new ScriptValue { Kind = ScriptValueKind.BinaryOp, Operator = op.Text, Left = left, Right = right };
        }
        return left;
    }

    ScriptValue ParsePrimary()
    {
        if (Check(TokenType.OpenParen))
        {
            Consume();
            var inner = ParseExpression();
            Expect(TokenType.CloseParen, "to close grouped expression");
            return inner;
        }

        return ParseValue();
    }

    bool IsEntryStart(TokenType next) =>
        next == TokenType.Equals || next == TokenType.Colon || next == TokenType.OpenBrace;

    Token Expect(TokenType type, string context)
    {
        if (!Check(type))
            throw new Exception($"Script parse: expected {type} {context} (line: {Peek().Line}).");
        return Consume();
    }
    Token Peek() => tokens[pos];
    Token PeekAhead(int offset) => tokens[pos + offset];
    Token Consume() => tokens[pos++];
    bool Check(TokenType type) => Peek().Type == type;
}