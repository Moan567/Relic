using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace Chisel.EXScript;
public enum EXTokenType
{
    Identifier,
    String,
    Number,
    Boolean,
    OpenBrace,
    CloseBrace,
    OpenParen,
    CloseParen,
    Comma,
    Equals,
    Plus,
    Minus,
    Star,
    Slash,
    Eof,
    Comment,

    // new to EX
    Semicolon,
    Dot,
    EqualsEquals,
    NotEquals,
    Bang,
    Less,
    Greater,
    LessEquals,
    GreaterEquals,
    AmpAmp,
    PipePipe,
    Percent,
    InterpolatedString,
    Var,
    If,
    Else,
    While,
    For,
    Wait,
    Is,
    Not,
    Null,
    Const,
    Break,
    Continue,
    Return,
    OpenSquare,
    CloseSquare,
    Function,
    PlusEquals,
    MinusEquals,
    PlusPlus,
    MinusMinus,
    StarEquals,
    SlashEquals,
    PercentEquals,
}
public struct EXToken
{
    public EXTokenType Type;
    public string Text;
    public int Line;
    public int Offset;
    public int Length;
}

/*
 * EXPRESSIONS!!!
 * This is sort of the thing that makes EXScript different from ValueScript. 
 * EXScript is a full expression & executable language (hence EX), while 
 * ValueScript is more of a data format with some scripting capabilities.
 */

public abstract class EXExpr { public int Line; }

public class NumberLiteral : EXExpr { public double Value; }
public class StringLiteral : EXExpr { public string Value; }
public class BooleanLiteral : EXExpr { public bool Value; }
public class NullLiteral : EXExpr { }

public class InterpolatedString : EXExpr
{
    public List<string> Literals = new List<string>();
    public List<EXExpr> Expressions = new List<EXExpr>();
}

public class Identifier : EXExpr { public string Name; }

public class UnaryOp : EXExpr { public string Operator; public EXExpr Operand; }
public class BinaryOp : EXExpr { public string Operator; public EXExpr Left, Right; }

public class MemberAccess : EXExpr { public EXExpr Receiver; public string Name; }
public class AssignExpr : EXExpr { public EXExpr Target; public EXExpr Value; }
public class IndexAccess : EXExpr { public EXExpr Target; public EXExpr Index; }
public class IncDecExpr : EXExpr { public EXExpr Target; public bool IsIncrement; public bool IsPrefix; }
public class Call : EXExpr
{
    public EXExpr Receiver;
    public string Name;
    public List<EXExpr> Arguments = new List<EXExpr>();
}

// Statements
public abstract class EXStatement { public int Line; }
public class VarDecl : EXStatement { public string Name; public EXExpr Value; public bool IsConst; }
public class ExprStatement : EXStatement { public EXExpr Expression; }
public class IfStatement : EXStatement { public EXExpr Condition; public EXStatement Then, Else; }
public class WhileStatement : EXStatement { public EXExpr Condition; public EXStatement Body; }
public class ForStatement : EXStatement { public EXStatement Init; public EXExpr Condition; public EXExpr Increment; public EXStatement Body; }
public class WaitStatement : EXStatement { public EXExpr Duration; }
public class BreakStatement : EXStatement { }
public class ContinueStatement : EXStatement { }
public class ReturnStatement : EXStatement { public EXExpr Value; }
public class Block : EXStatement
{
    public List<EXStatement> Statements = new();
}
public class EXScript
{
    public List<EXStatement> Statements = new();
    public Dictionary<string, FunctionDecl> Functions = new();
    public List<VarDecl> GlobalConsts = new();
}
public class ValueBox<T> { public T Value; }

public class FunctionDecl { public string Name; public List<string> Parameters = new(); public Block Body; }

// "runtime" data

public struct EXVec3D { public double X, Y, Z; public EXVec3D(double x, double y, double z) { X = x; Y = y; Z = z; } }
public struct EXVec2D { public double X, Y; public EXVec2D(double x, double y) { X = x; Y = y; } }

public interface IEXObjectType
{
    string TypeName { get; }
    bool TryGetMember(object instance, string name, out EXValue value);
    bool TrySetMember(object instance, string name, EXValue value);
    bool TryGetIndexer(object instance, EXValue index, out EXValue value);
    bool TrySetIndexer(object instance, EXValue index, EXValue value);

    bool TryGetPersistToken(object instance, out string token);
    bool TryResolveFromToken(string token, out object instance);
}
public class EXObject
{
    public IEXObjectType Type;
    public object Instance;
}

public enum EXKind { Number, String, Bool, Array, Null, Object, Vector3D, Vector2D }
public class EXValue
{
    public EXKind Kind;
    public double Number;
    public string String;
    public bool Bool;
    public EXVec3D Vector3D;
    public EXVec2D Vector2D;
    public EXObject Object;
    public EXValue[] Array;

    internal static EXValue Null()
    {
        return new EXValue
        {
            Kind = EXKind.Null,
        };
    }

    internal static EXValue Of(bool value)
    {
        return new EXValue
        {
            Kind = EXKind.Bool,
            Bool = value
        };
    }

    internal static EXValue Of(string value)
    {
        return new EXValue
        {
            Kind = EXKind.String,
            String = value
        };
    }

    internal static EXValue Of(double value)
    {
        return new EXValue
        {
            Kind = EXKind.Number,
            Number = value
        };
    }

    internal static EXValue Of(EXVec3D value)
    {
        return new EXValue
        {
            Kind = EXKind.Vector3D,
            Vector3D = value
        };
    }
    internal static EXValue Of(EXVec2D value)
    {
        return new EXValue
        {
            Kind = EXKind.Vector2D,
            Vector2D = value
        };
    }
    internal static EXValue Of(EXObject value)
    {
        return new EXValue
        {
            Kind = EXKind.Object,
            Object = value
        };
    }
    internal static EXValue Of(EXValue[] value)
    {
        return new EXValue
        {
            Kind = EXKind.Array,
            Array = value
        };
    }
}