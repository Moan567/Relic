using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Chisel.EXScript;
internal class EXParser
{
    List<EXToken> tokens;
    int pos;
    public EXExpr ParseSingleExpression(List<EXToken> tokens)
    {
        this.tokens = tokens; pos = 0;
        return ParseExpression();
    }

    public EXScript Parse(List<EXToken> tokens)
    {
        this.tokens = tokens;
        pos = 0;
        var script = new EXScript();
        while(!Check(EXTokenType.Eof))
        {
            if (Match(EXTokenType.Comment)) continue;

            if (ParseFunction(out var func))
            {
                script.Functions.Add(func.Name, func);
            }
            else if (Match(EXTokenType.Const))
            {
                var decl = (VarDecl)ParseVarDecl(isConst: true);
                script.GlobalConsts.Add(decl);
            }
            else
            {
                script.Statements.Add(ParseStatement());
            }
        }
        return script;
    }

    bool ParseFunction(out FunctionDecl val)
    {
        val = null;

        if(Match(EXTokenType.Function))
        {
            var funcName = Expect(EXTokenType.Identifier, "for function name");

            Expect(EXTokenType.OpenParen, "to begin function args");
            var args = new List<string>();
            if (!Check(EXTokenType.CloseParen))
            {
                do { args.Add(Expect(EXTokenType.Identifier, "for argument").Text); }
                while (Match(EXTokenType.Comma));
            }
            Expect(EXTokenType.CloseParen, "to end function args");

            var funcBody = ParseBlock();

            val = new FunctionDecl { Body = funcBody as Block, Name = funcName.Text, Parameters = args };
            return true;
        }

        return false;
    }

    EXStatement ParseStatement()
    {
        var startLine = Peek().Line;
        EXStatement stmt = DispatchStatement();
        stmt.Line = startLine;
        return stmt;
    }

    EXStatement DispatchStatement()
    {
        if (Match(EXTokenType.Var)) return ParseVarDecl(isConst: false);
        if (Match(EXTokenType.Const)) return ParseVarDecl(isConst: true);
        if (Match(EXTokenType.If)) return ParseIf();
        if (Match(EXTokenType.While)) return ParseWhile();
        if (Check(EXTokenType.OpenBrace)) return ParseBlock();
        if (Match(EXTokenType.For)) return ParseFor();
        if (Match(EXTokenType.Wait)) return ParseWait();
        if (Match(EXTokenType.Break))
        {
            Expect(EXTokenType.Semicolon, "to end break statement");
            return new BreakStatement();
        }
        if (Match(EXTokenType.Continue))
        {
            Expect(EXTokenType.Semicolon, "to end continue statement");
            return new ContinueStatement();
        }
        if (Match(EXTokenType.Return)) return ParseReturn();

        return ParseExprStatement();
    }

    EXStatement ParseReturn()
    {
        if (Match(EXTokenType.Semicolon))
            return new ReturnStatement();

        var returnValue = ParseExpression();
        Expect(EXTokenType.Semicolon, "to close return");
        return new ReturnStatement { Value = returnValue };
    }

    EXStatement ParseVarDecl(bool isConst)
    {
        var name = Expect(EXTokenType.Identifier, "after 'var'/'const'").Text;
        Expect(EXTokenType.Equals, "after variable name in declaration");
        var init = ParseExpression();
        Expect(EXTokenType.Semicolon, "to end declaration");

        return new VarDecl { Name = name, Value = init, IsConst = isConst };
    }

    EXStatement ParseWhile()
    {
        Expect(EXTokenType.OpenParen, "to begin while condition");
        var cond = ParseExpression();
        Expect(EXTokenType.CloseParen, "to end while condition");
        var body = ParseStatement();
        return new WhileStatement { Condition = cond, Body = body };
    }

    EXStatement ParseBlock()
    {
        Expect(EXTokenType.OpenBrace, "to begin block");
        var block = new Block();
        while (!Check(EXTokenType.CloseBrace) && !Check(EXTokenType.Eof))
            block.Statements.Add(ParseStatement());
        Expect(EXTokenType.CloseBrace, "to end block");
        return block;
    }

    EXStatement ParseFor()
    {
        Expect(EXTokenType.OpenParen, "to begin clause");

        EXStatement initializer = null;
        if (Check(EXTokenType.Semicolon)) Consume(); // no init
        else initializer = Match(EXTokenType.Var) ? ParseVarDecl(false) : ParseExprStatement();

        EXExpr cond = Check(EXTokenType.Semicolon) ? null : ParseExpression();
        Expect(EXTokenType.Semicolon, "after for-loop condition");

        EXExpr increment = Check(EXTokenType.CloseParen) ? null : ParseExpression();
        Expect(EXTokenType.CloseParen, "to end for-loop clauses");

        var body = ParseStatement();

        return new ForStatement { Init = initializer, Condition = cond, Body = body, Increment = increment };
    }

    EXStatement ParseExprStatement()
    {
        var expr = ParseExpression();
        Expect(EXTokenType.Semicolon, "to end statement");
        return new ExprStatement { Expression = expr };
    }

    EXStatement ParseWait()
    {
        var expr = ParseExpression();
        Expect(EXTokenType.Semicolon, "to end statement");
        return new WaitStatement { Duration = expr };
    }

    EXStatement ParseIf()
    {
        Expect(EXTokenType.OpenParen, "to begin condition statement");
        var cond = ParseExpression();
        Expect(EXTokenType.CloseParen, "to end condition statement");
        var thenBranch = ParseStatement();
        EXStatement elseBranch = null;
        if (Match(EXTokenType.Else)) elseBranch = ParseStatement();
        return new IfStatement { Condition = cond, Then = thenBranch, Else = elseBranch };
    }

    static readonly (EXTokenType type, string op)[] CompoundAssignOps =
    {
        (EXTokenType.PlusEquals, "+"),
        (EXTokenType.MinusEquals, "-"),
        (EXTokenType.StarEquals, "*"),
        (EXTokenType.SlashEquals, "/"),
        (EXTokenType.PercentEquals, "%"),
    };

    EXExpr ParseExpression() => ParseAssignment();
    EXExpr ParseAssignment()
    {
        var left = ParseLogicOr();

        if (Match(EXTokenType.Equals))
        {
            var value = ParseAssignment();
            if (left is not (Identifier or MemberAccess or IndexAccess))
                throw new Exception($"EXScript parse: Invalid assignment target. (line {Peek().Line})");
            return new AssignExpr { Target = left, Value = value };
        }

        foreach (var (type, op) in CompoundAssignOps)
        {
            if (!Match(type)) continue;

            if (left is not (Identifier or MemberAccess or IndexAccess))
                throw new Exception($"EXScript parse: Invalid assignment target. (line {Peek().Line})");

            var line = Previous().Line;
            var rhs = ParseAssignment();
            return new AssignExpr
            {
                Target = left,
                Value = new BinaryOp { Operator = op, Left = left, Right = rhs, Line = line },
                Line = line
            };
        }

        return left;
    }

    EXExpr ParseLogicOr() => ParseBinaryLevel(ParseLogicAnd, EXTokenType.PipePipe);
    EXExpr ParseLogicAnd() => ParseBinaryLevel(ParseLogicEqual, EXTokenType.AmpAmp);
    EXExpr ParseLogicEqual() => ParseBinaryLevel(ParseLogicComparison, EXTokenType.EqualsEquals, EXTokenType.NotEquals);
    EXExpr ParseLogicComparison() => ParseBinaryLevel(ParseLogicIs, EXTokenType.Greater, EXTokenType.Less, EXTokenType.GreaterEquals, EXTokenType.LessEquals);
    EXExpr ParseLogicIs()
    {
        var left = ParseLogicTerm();
        if (Match(EXTokenType.Is))
        {
            bool negate = Match(EXTokenType.Not);
            var typeName = Expect(EXTokenType.Identifier, "a type name after 'is'").Text;
            var check = new BinaryOp { Operator = "is", Left = left, Right = new Identifier { Name = typeName } };
            return negate ? new UnaryOp { Operator = "!", Operand = check } : check;
        }
        return left;
    }
    EXExpr ParseLogicTerm() => ParseBinaryLevel(ParseLogicFactor, EXTokenType.Plus, EXTokenType.Minus);
    EXExpr ParseLogicFactor() => ParseBinaryLevel(ParseLogicUnary, EXTokenType.Star, EXTokenType.Slash, EXTokenType.Percent);
    EXExpr ParseLogicUnary()
    {
        if (MatchAny(new[] {EXTokenType.Bang, EXTokenType.Minus}, out var op))
        {
            var operand = ParseLogicUnary();
            return new UnaryOp { Operator = op, Operand = operand };
        }
        if (Match(EXTokenType.PlusPlus) || Match(EXTokenType.MinusMinus))
        {
            bool isIncrement = Previous().Type == EXTokenType.PlusPlus;
            var line = Previous().Line;
            var target = ParseLogicUnary();
            if (target is not (Identifier or MemberAccess or IndexAccess))
                throw new Exception($"EXScript parse: Invalid target for '{(isIncrement ? "++" : "--")}'. (line {line})");
            return new IncDecExpr { Target = target, IsIncrement = isIncrement, IsPrefix = true, Line = line };
        }
        return ParseCall();
    }
    EXExpr ParseIndex(EXExpr expr)
    {
        var index = ParseExpression();
        Expect(EXTokenType.CloseSquare,"to finish indexer");

        return new IndexAccess { Index = index, Target = expr };
    }
    EXExpr ParseCall()
    {
        var expr = ParsePrimary();

        while (true)
        {
            if (Match(EXTokenType.Dot))
            {
                var name = Expect(EXTokenType.Identifier, "after .").Text;
                if (Match(EXTokenType.OpenParen))
                {
                    var args = new List<EXExpr>();
                    if (!Check(EXTokenType.CloseParen))
                    {
                        do { args.Add(ParseExpression()); }
                        while (Match(EXTokenType.Comma));
                    }
                    Expect(EXTokenType.CloseParen, "to close call args");
                    expr = new Call { Receiver = expr, Name = name, Arguments = args, Line = expr.Line };
                }
                else
                {
                    expr = new MemberAccess { Receiver = expr, Name = name, Line = expr.Line };
                }
            }
            else if (Match(EXTokenType.OpenSquare))
            {
                expr = ParseIndex(expr);
            }
            else if (Match(EXTokenType.PlusPlus) || Match(EXTokenType.MinusMinus))
            {
                bool isIncrement = Previous().Type == EXTokenType.PlusPlus;
                var line = Previous().Line;
                if (expr is not (Identifier or MemberAccess or IndexAccess))
                    throw new Exception($"EXScript parse: Invalid target for '{(isIncrement ? "++" : "--")}'. (line {line})");
                expr = new IncDecExpr { Target = expr, IsIncrement = isIncrement, IsPrefix = false, Line = line };
                break;
            }
            else
            {
                break;
            }
        }
        return expr;
    }
    EXExpr ParsePrimary()
    {
        if (Match(EXTokenType.Number))
            return new NumberLiteral { Value = double.Parse(Previous().Text, CultureInfo.InvariantCulture), Line = Previous().Line };
        if (Match(EXTokenType.String))
            return new StringLiteral { Value = Previous().Text, Line = Previous().Line };
        if (Match(EXTokenType.Boolean))
            return new BooleanLiteral { Value = Previous().Text == "true", Line = Previous().Line };
        if (Match(EXTokenType.Null))
            return new NullLiteral();
        if (Match(EXTokenType.Identifier))
        {
            var name = Previous().Text;
            var line = Previous().Line;
            if (Match(EXTokenType.OpenParen))
            {
                var args = new List<EXExpr>();
                if (!Check(EXTokenType.CloseParen))
                {
                    do { args.Add(ParseExpression()); }
                    while (Match(EXTokenType.Comma));
                }
                Expect(EXTokenType.CloseParen, "to close call args");
                return new Call { Receiver = null, Name = name, Arguments = args, Line = line };
            }
            return new Identifier { Name = name, Line = line };
        }
        if (Match(EXTokenType.InterpolatedString))
            return ParseInterpolatedString(Previous().Text, Previous().Line);
        if (Match(EXTokenType.OpenParen))
        {
            var inner = ParseExpression();
            Expect(EXTokenType.CloseParen, "to close grouped expression");
            return inner;
        }

        throw new Exception($"EXScript parse: unexpected token '{Peek().Text}' (line: {Peek().Line}).");
    }

    EXExpr ParseInterpolatedString(string raw, int line)
    {
        var result = new InterpolatedString { Line = line };
        var literal = new StringBuilder();
        int i = 0;
        while (i < raw.Length)
        {
            if (raw[i] == '{')
            {
                result.Literals.Add(literal.ToString());
                literal.Clear();
                int depth = 1, start = ++i;
                while (i < raw.Length && depth > 0)
                {
                    if (raw[i] == '{') depth++;
                    else if (raw[i] == '}') depth--;
                    if (depth > 0) i++;
                }
                var innerTokens = new EXScriptTokenizer().Tokenize(raw[start..i]);
                result.Expressions.Add(new EXParser().ParseSingleExpression(innerTokens));
                i++; // skip closing '}'
            }
            else { literal.Append(raw[i++]); }
        }
        result.Literals.Add(literal.ToString());
        return result;
    }

    EXExpr ParseBinaryLevel(Func<EXExpr> nextLevel, params EXTokenType[] myOperators)
    {
        var line = Peek().Line;
        var left = nextLevel();
        while (MatchAny(myOperators, out var op))
        {
            var right = nextLevel();
            left = new BinaryOp { Operator = op, Left = left, Right = right, Line = line };
        }
        return left;
    }

    EXToken Expect(EXTokenType type, string context)
    {
        if (!Check(type))
            throw new Exception($"EXScript parse: expected {type} {context} (line: {Peek().Line}).");
        return Consume();
    }
    EXToken Previous() => tokens[pos - 1];
    EXToken Peek() => tokens[pos];
    EXToken PeekAhead(int offset) => tokens[pos + offset];
    EXToken Consume() => tokens[pos++];
    bool Check(EXTokenType type) => Peek().Type == type; 
    bool Match(EXTokenType type)
    {
        if (!Check(type)) return false;
        pos++; // consume it
        return true;
    }
    bool MatchAny(EXTokenType[] tokens, out string tokenVal)
    {
        tokenVal = null;
        foreach (var token in tokens)
        {
            var fulltoken = Peek();
            if(fulltoken.Type == token)
            {
                Consume();
                tokenVal = fulltoken.Text;
                return true;
            }
        }
        return false;
    }
}
