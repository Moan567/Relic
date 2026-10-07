using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Scripting.ValueScript;
public enum TokenType
{
    Identifier, 
    Variable,
    String, 
    Number,
    Boolean,
    OpenBrace, 
    CloseBrace, 
    OpenParen, 
    CloseParen,
    Comma,
    Equals, 
    Colon, 
    Plus,
    Minus,
    Star,
    Slash,
    Eof
}
public struct Token
{
    public TokenType Type;
    public string Text;
    public int Line;
}
public enum ScriptValueKind { String, Identifier, Variable, Number, Boolean, FunctionCall, Block, BinaryOp }

public class ScriptValue
{
    public ScriptValueKind Kind;
    public string StringValue;
    public double NumberValue;
    public bool BooleanValue;
    public ScriptFunctionCall FunctionCall;
    public ScriptBlock Block;

    public string Operator;
    public ScriptValue Left, Right;
}

public class ScriptFunctionCall
{
    public string Name;
    public List<ScriptValue> Arguments = new();
    public ScriptBlock TrailingBlock;   // null if none
}

public class ScriptEntry
{
    public string Key;      // null for bare array-style elements
    public ScriptValue Value;
    public int Line;
}

public class ScriptBlock
{
    public string BaseRef;  // the ": SomeOther.Def" reference, null if none
    public List<ScriptEntry> Entries = new();

    public IEnumerable<ScriptEntry> Find(string key) =>
        Entries.Where(e => e.Key != null && e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public ScriptEntry FindFirst(string key) => Find(key).FirstOrDefault();
}