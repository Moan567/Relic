using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using static System.Formats.Asn1.AsnWriter;

namespace Chisel.EXScript;

public enum SignalKind { Wait, Break, Continue, Return }
public struct EXSignal
{
    public SignalKind Kind;
    public float WaitSeconds;
    public EXValue ReturnValue;
    public static EXSignal Wait(float s) => new() { Kind = SignalKind.Wait, WaitSeconds = s };
    public static EXSignal Break() => new() { Kind = SignalKind.Break };
    public static EXSignal Continue() => new() { Kind = SignalKind.Continue };
    public static EXSignal Return(EXValue v) => new() { Kind = SignalKind.Return, ReturnValue = v };
}

public class EXScriptHandle
{
    internal bool Cancelled;
    public void Stop() => Cancelled = true;

    readonly TaskCompletionSource<bool> completion = new();
    public Task Completion => completion.Task;
    internal void MarkComplete() => completion.TrySetResult(true);
}
public enum EXSetResult { Ok, NotFound, ConstViolation }
public class EXScope
{
    EXScope parent;
    Dictionary<string, EXValue> vars = new();
    HashSet<string> consts = new();

    public EXScope(EXScope parent = null) => this.parent = parent;

    public void Declare(string name, EXValue value, bool isConst = false)
    {
        vars[name] = value;
        if (isConst) consts.Add(name);
        else consts.Remove(name);
    }

    public bool TryGet(string name, out EXValue value)
    {
        for (var s = this; s != null; s = s.parent)
            if (s.vars.TryGetValue(name, out value)) return true;
        value = null;
        return false;
    }

    public EXSetResult TrySet(string name, EXValue value)
    {
        for (var s = this; s != null; s = s.parent)
        {
            if (s.vars.ContainsKey(name))
            {
                if (s.consts.Contains(name)) return EXSetResult.ConstViolation;
                s.vars[name] = value;
                return EXSetResult.Ok;
            }
        }
        return EXSetResult.NotFound;
    }
}
public class EXExecContext
{
    public IEXHost Host;
    public Dictionary<string, FunctionDecl> Functions;
    public EXScope GlobalConsts;
}
public interface IEXHost
{
    EXValue ResolveName(string name, EXScope scope);
    EXValue GetMember(EXValue receiver, string name, EXScope scope);
    EXValue Invoke(EXValue receiver, string name, EXValue[] args, EXScope scope);
    void AssignMember(EXValue receiver, string name, EXValue value);
    void ScheduleResume(Action resume, EXSignal signal);
    void ReportError(EXScriptRuntimeError error);
}

public static class EXRuntime
{
    public static EXScriptHandle Run(EXScript script, IEXHost host, EXScope rootScope = null)
    {
        var handle = new EXScriptHandle();

        var ctx = new EXExecContext { Host = host, Functions = script.Functions };

        var globalConsts = new EXScope(null);
        foreach (var decl in script.GlobalConsts)
            globalConsts.Declare(decl.Name, Evaluate(decl.Value, globalConsts, ctx), isConst: true);
        ctx.GlobalConsts = globalConsts;

        var scope = rootScope ?? new EXScope(globalConsts);

        var enumerator = ExecuteAll(script.Statements, scope, ctx).GetEnumerator();
        Advance(enumerator, ctx, handle);
        return handle;
    }

    static void Advance(IEnumerator<EXSignal> e, EXExecContext ctx, EXScriptHandle handle)
    {
        if (handle.Cancelled) return;
        try
        {
            if (e.MoveNext())
            {
                ctx.Host.ScheduleResume(() => Advance(e, ctx, handle), e.Current);
            }
            else
            {
                handle.MarkComplete();
            }
        }
        catch (EXScriptRuntimeError err)
        {
            ctx.Host.ReportError(err);
            handle.MarkComplete();
        }
    }

    static IEnumerable<EXSignal> ExecuteAll(List<EXStatement> stmts, EXScope scope, EXExecContext ctx)
    {
        foreach (var s in stmts)
            foreach (var y in Execute(s, scope, ctx))
                yield return y;
    }
    static IEnumerable<EXSignal> Execute(EXStatement stmt, EXScope scope, EXExecContext ctx)
    {
        switch (stmt)
        {
            case VarDecl v:
            {
                if (IsStatementLevelFunctionCall(v.Value, ctx, out var call))
                {
                    var box = new ValueBox<EXValue>();
                    foreach (var sig in RunStatementLevelCall(call, scope, ctx, box, v.Line))
                        yield return sig;
                    scope.Declare(v.Name, box.Value, v.IsConst);
                }
                else
                {
                    scope.Declare(v.Name, Evaluate(v.Value, scope, ctx), v.IsConst);
                }
                break;
            }
            case ExprStatement e:
            {
                if (IsStatementLevelFunctionCall(e.Expression, ctx, out var call))
                {
                    var discard = new ValueBox<EXValue>();
                    foreach (var sig in RunStatementLevelCall(call, scope, ctx, discard, e.Line))
                        yield return sig;
                }
                else
                {
                    Evaluate(e.Expression, scope, ctx);
                }
                break;
            }

            case WaitStatement w:
                yield return new EXSignal { Kind = SignalKind.Wait, WaitSeconds = (float)Evaluate(w.Duration, scope, ctx).Number };
                break;

            case IfStatement i:
            {
                var branch = RequireBool(Evaluate(i.Condition, scope, ctx)) ? i.Then : i.Else;
                if (branch != null)
                    foreach (var sig in Execute(branch, new EXScope(scope), ctx))
                        yield return sig; // if/else never catches anything, purely a pass-through
                break;
            }

            case WhileStatement w:
            {
                bool broken = false;
                while (!broken && RequireBool(Evaluate(w.Condition, scope, ctx)))
                {
                    foreach (var sig in Execute(w.Body, new EXScope(scope), ctx))
                    {
                        if (sig.Kind == SignalKind.Wait) { yield return sig; continue; }
                        if (sig.Kind == SignalKind.Continue) break;              // stop this iteration, recheck condition
                        if (sig.Kind == SignalKind.Break) { broken = true; break; }
                        yield return sig; yield break;                          // Return: not ours to catch, propagate immediately
                    }
                }
                break;
            }

            case Block b:
            {
                var blockScope = new EXScope(scope);
                foreach (var s in b.Statements)
                {
                    bool stop = false;
                    foreach (var sig in Execute(s, blockScope, ctx))
                    {
                        yield return sig;
                        if (sig.Kind != SignalKind.Wait) stop = true;
                    }
                    if (stop) yield break;
                }
                break;
            }

            case ForStatement f:
            {
                var forScope = new EXScope(scope);
                if (f.Init != null) foreach (var _ in Execute(f.Init, forScope, ctx)) { }

                bool broken = false;
                while (!broken && (f.Condition == null || RequireBool(Evaluate(f.Condition, forScope, ctx))))
                {
                    var iterScope = new EXScope(forScope);
                    foreach (var sig in Execute(f.Body, iterScope, ctx))
                    {
                        if (sig.Kind == SignalKind.Wait) { yield return sig; continue; }
                        if (sig.Kind == SignalKind.Continue) break;   // fall through to increment below
                        if (sig.Kind == SignalKind.Break) { broken = true; break; }
                        yield return sig; yield break;                // Return
                    }
                    if (broken) yield break;
                    if (f.Increment != null)
                        foreach (var _ in Execute(new ExprStatement { Expression = f.Increment }, forScope, ctx)) { }
                }
                break;
            }

            case BreakStatement: yield return EXSignal.Break(); yield break;
            case ContinueStatement: yield return EXSignal.Continue(); yield break;
            case ReturnStatement r:
                var rv = r.Value != null ? Evaluate(r.Value, scope, ctx) : EXValue.Null();
                yield return EXSignal.Return(rv);
                yield break;
        }
    }
    static bool IsStatementLevelFunctionCall(EXExpr expr, EXExecContext ctx, out Call call)
    {
        call = expr as Call;
        return call != null && call.Receiver == null && ctx.Functions.ContainsKey(call.Name);
    }
    static IEnumerable<EXSignal> RunStatementLevelCall(Call c, EXScope scope, EXExecContext ctx, ValueBox<EXValue> result, int callLine)
    {
        var args = new EXValue[c.Arguments.Count];
        for (int i = 0; i < args.Length; i++)
            args[i] = Evaluate(c.Arguments[i], scope, ctx);

        var fn = ctx.Functions[c.Name];
        foreach (var sig in CallFunction(fn, args, ctx, result, callLine))
            yield return sig;
    }
    static EXValue InvokeUserFunctionSync(FunctionDecl fn, EXValue[] args, EXExecContext ctx, int callLine)
    {
        var box = new ValueBox<EXValue>();
        foreach (var sig in CallFunction(fn, args, ctx, box, callLine))
        {
            if (sig.Kind == SignalKind.Wait)
                throw new EXScriptRuntimeError($"'{fn.Name}' called wait() from inside an expression; functions used this way must run synchronously.", callLine);
        }
        return box.Value;
    }
    static IEnumerable<EXSignal> CallFunction(FunctionDecl fn, EXValue[] args, EXExecContext ctx, ValueBox<EXValue> result, int callLine)
    {
        if (args.Length != fn.Parameters.Count)
            throw new EXScriptRuntimeError($"{fn.Name} called with wrong arg count", callLine);

        var calleeScope = new EXScope(ctx.GlobalConsts);
        for (int i = 0; i < fn.Parameters.Count; i++)
            calleeScope.Declare(fn.Parameters[i], i < args.Length ? args[i] : EXValue.Null());

        foreach (var sig in Execute(fn.Body, calleeScope, ctx))
        {
            if (sig.Kind == SignalKind.Wait) { yield return sig; continue; }
            if (sig.Kind == SignalKind.Return) { result.Value = sig.ReturnValue; yield break; }
            throw new EXScriptRuntimeError($"'{sig.Kind}' used outside of a loop.", callLine);
        }
        result.Value ??= EXValue.Null();
    }
    static EXValue Evaluate(EXExpr expr, EXScope scope, EXExecContext ctx)
    {
        switch (expr)
        {
            case NumberLiteral n: return EXValue.Of(n.Value);
            case StringLiteral s: return EXValue.Of(s.Value);
            case BooleanLiteral b: return EXValue.Of(b.Value);
            case NullLiteral: return EXValue.Null();
            case InterpolatedString s: return EvaluateInterpolated(s, scope, ctx);

            case Identifier id:
                if (scope.TryGet(id.Name, out var local)) return local;
                return ctx.Host.ResolveName(id.Name, scope);

            case MemberAccess m:
                return EvaluateMemberAccess(m, scope, ctx);

            case IncDecExpr id:
                {
                    var current = Evaluate(id.Target, scope, ctx);
                    var oldValue = RequireNumber(current, id.Line);
                    var newValue = EXValue.Of(oldValue + (id.IsIncrement ? 1 : -1));
                    AssignTo(id.Target, newValue, scope, ctx, id.Line);
                    return id.IsPrefix ? newValue : EXValue.Of(oldValue);
                }

            case IndexAccess ix:
                {
                    var targ = Evaluate(ix.Target, scope, ctx);
                    if(targ.Kind == EXKind.Vector3D)
                    {
                        var index = RequireNumber(Evaluate(ix.Index, scope, ctx));

                        switch ((int)index)
                        {
                            case 0: return EXValue.Of(targ.Vector3D.X);
                            case 1: return EXValue.Of(targ.Vector3D.Y);
                            case 2: return EXValue.Of(targ.Vector3D.Z);
                            default: throw new EXScriptRuntimeError($"wrong vector3D index {(int)index}", expr.Line);
                        }
                    }
                    if (targ.Kind == EXKind.Vector2D)
                    {
                        var index = RequireNumber(Evaluate(ix.Index, scope, ctx));

                        switch ((int)index)
                        {
                            case 0: return EXValue.Of(targ.Vector2D.X);
                            case 1: return EXValue.Of(targ.Vector2D.Y);
                            default: throw new EXScriptRuntimeError($"wrong vector2D index {(int)index}", expr.Line);
                        }
                    }
                    if (targ.Kind == EXKind.Object)
                    {
                        if (targ.Object.Type.TryGetIndexer(targ.Object.Instance, Evaluate(ix.Index, scope, ctx), out var arrval))
                            return arrval;

                        throw new EXScriptRuntimeError($"{targ.Object.Type.TypeName} has no get indexer", expr.Line);
                    }

                    var (arr, i) = ResolveIndex(ix, scope, ctx);
                    return arr.Array[i];
                }

            case UnaryOp u:
                {
                    var operand = Evaluate(u.Operand, scope, ctx);
                    return u.Operator switch
                    {
                        "!" => EXValue.Of(!RequireBool(operand)),
                        "-" => EXValue.Of(-RequireNumber(operand)),
                        _ => throw new EXScriptRuntimeError($"Unknown unary operator '{u.Operator}'", u.Line)
                    };
                }

            case BinaryOp b:
                return EvaluateBinary(b, scope, ctx);

            case AssignExpr a:
                return EvaluateAssign(a, scope, ctx);

            case Call c:
                return EvaluateCall(c, scope, ctx);

            default:
                throw new EXScriptRuntimeError($"No evaluator for expression node '{expr.GetType().Name}'.", expr.Line);
        }
    }
    static EXValue EvaluateMemberAccess(MemberAccess m, EXScope scope, EXExecContext ctx)
    {
        var receiver = Evaluate(m.Receiver, scope, ctx);

        if (receiver.Kind == EXKind.Vector3D)
        {
            return m.Name switch
            {
                "x" => EXValue.Of(receiver.Vector3D.X),
                "y" => EXValue.Of(receiver.Vector3D.Y),
                "z" => EXValue.Of(receiver.Vector3D.Z),
                _ => throw new EXScriptRuntimeError($"Vector has no member '{m.Name}'.", m.Line)
            };
        }
        if (receiver.Kind == EXKind.Vector2D)
        {
            return m.Name switch
            {
                "x" => EXValue.Of(receiver.Vector2D.X),
                "y" => EXValue.Of(receiver.Vector2D.Y),
                _ => throw new EXScriptRuntimeError($"Vector has no member '{m.Name}'.", m.Line)
            };
        }

        if (receiver.Kind == EXKind.Object)
        {
            if (receiver.Object.Type.TryGetMember(receiver.Object.Instance, m.Name, out var value))
                return value;
            throw new EXScriptRuntimeError(
                $"'{receiver.Object.Type.TypeName}' has no readable member '{m.Name}'.", m.Line);
        }

        if (receiver.Kind == EXKind.Null)
            throw new EXScriptRuntimeError($"Cannot access '.{m.Name}' on null.", m.Line);

        return ctx.Host.GetMember(receiver, m.Name, scope);
    }
    static EXValue EvaluateBinary(BinaryOp b, EXScope scope, EXExecContext ctx)
    {
        // Short-circuit operators
        if (b.Operator == "&&")
        {
            if (!RequireBool(Evaluate(b.Left, scope, ctx))) return EXValue.Of(false);
            return EXValue.Of(RequireBool(Evaluate(b.Right, scope, ctx)));
        }
        if (b.Operator == "||")
        {
            if (RequireBool(Evaluate(b.Left, scope, ctx))) return EXValue.Of(true);
            return EXValue.Of(RequireBool(Evaluate(b.Right, scope, ctx)));
        }
        if (b.Operator == "is")
        {
            var operand = Evaluate(b.Left, scope, ctx);
            if (b.Right is not Identifier typeIdent)
                throw new EXScriptRuntimeError("Expected a type name after 'is'.", b.Line);

            return EXValue.Of(typeIdent.Name switch
            {
                "number" => operand.Kind == EXKind.Number,
                "string" => operand.Kind == EXKind.String,
                "bool" => operand.Kind == EXKind.Bool,
                "null" => operand.Kind == EXKind.Null,
                "vec3" => operand.Kind == EXKind.Vector3D,
                "vec2" => operand.Kind == EXKind.Vector2D,
                "array" => operand.Kind == EXKind.Array,
                _ when operand.Kind == EXKind.Object => operand.Object.Type.TypeName == typeIdent.Name,
                _ => throw new EXScriptRuntimeError($"Unknown type name '{typeIdent.Name}' in 'is' check.", b.Line)
            });
        }

        var l = Evaluate(b.Left, scope, ctx);
        var r = Evaluate(b.Right, scope, ctx);

        if (l.Kind == EXKind.Vector3D || r.Kind == EXKind.Vector3D)
            return EvaluateVector3DBinary(b.Operator, l, r, b.Line);
        if (l.Kind == EXKind.Vector2D || r.Kind == EXKind.Vector2D)
            return EvaluateVector2DBinary(b.Operator, l, r, b.Line);

        return b.Operator switch
        {
            "+" => EXValue.Of(RequireNumber(l) + RequireNumber(r)),
            "-" => EXValue.Of(RequireNumber(l) - RequireNumber(r)),
            "*" => EXValue.Of(RequireNumber(l) * RequireNumber(r)),
            "/" => EXValue.Of(RequireNumber(l) / RequireNumber(r)),
            "%" => EXValue.Of(RequireNumber(l) % RequireNumber(r)),
            "==" => EXValue.Of(ValuesEqual(l, r)),
            "!=" => EXValue.Of(!ValuesEqual(l, r)),
            "<" => EXValue.Of(RequireNumber(l) < RequireNumber(r)),
            ">" => EXValue.Of(RequireNumber(l) > RequireNumber(r)),
            "<=" => EXValue.Of(RequireNumber(l) <= RequireNumber(r)),
            ">=" => EXValue.Of(RequireNumber(l) >= RequireNumber(r)),
            _ => throw new EXScriptRuntimeError($"Unknown binary operator '{b.Operator}'", b.Line)
        };
    }
    static EXValue EvaluateVector3DBinary(string op, EXValue l, EXValue r, int line)
    {
        if (op == "+" && l.Kind == EXKind.Vector3D && r.Kind == EXKind.Vector3D)
            return EXValue.Of(new EXVec3D(l.Vector3D.X + r.Vector3D.X, l.Vector3D.Y + r.Vector3D.Y, l.Vector3D.Z + r.Vector3D.Z));

        if (op == "-" && l.Kind == EXKind.Vector3D && r.Kind == EXKind.Vector3D)
            return EXValue.Of(new EXVec3D(l.Vector3D.X - r.Vector3D.X, l.Vector3D.Y - r.Vector3D.Y, l.Vector3D.Z - r.Vector3D.Z));

        if (op == "*" && l.Kind == EXKind.Vector3D && r.Kind == EXKind.Number)
            return EXValue.Of(new EXVec3D(l.Vector3D.X * r.Number, l.Vector3D.Y * r.Number, l.Vector3D.Z * r.Number));

        if (op == "*" && l.Kind == EXKind.Number && r.Kind == EXKind.Vector3D)
            return EXValue.Of(new EXVec3D(r.Vector3D.X * l.Number, r.Vector3D.Y * l.Number, r.Vector3D.Z * l.Number));

        if (op == "/" && l.Kind == EXKind.Vector3D && r.Kind == EXKind.Number)
            return EXValue.Of(new EXVec3D(l.Vector3D.X / r.Number, l.Vector3D.Y / r.Number, l.Vector3D.Z / r.Number));

        throw new EXScriptRuntimeError($"Operator '{op}' is not defined for {l.Kind} and {r.Kind}.", line);
    }
    static EXValue EvaluateVector2DBinary(string op, EXValue l, EXValue r, int line)
    {
        if (op == "+" && l.Kind == EXKind.Vector2D && r.Kind == EXKind.Vector2D)
            return EXValue.Of(new EXVec2D(l.Vector2D.X + r.Vector2D.X, l.Vector2D.Y + r.Vector2D.Y));

        if (op == "-" && l.Kind == EXKind.Vector2D && r.Kind == EXKind.Vector2D)
            return EXValue.Of(new EXVec2D(l.Vector2D.X - r.Vector2D.X, l.Vector2D.Y - r.Vector2D.Y));

        if (op == "*" && l.Kind == EXKind.Vector2D && r.Kind == EXKind.Number)
            return EXValue.Of(new EXVec2D(l.Vector2D.X * r.Number, l.Vector2D.Y * r.Number));

        if (op == "*" && l.Kind == EXKind.Number && r.Kind == EXKind.Vector2D)
            return EXValue.Of(new EXVec2D(r.Vector2D.X * l.Number, r.Vector2D.Y * l.Number));

        if (op == "/" && l.Kind == EXKind.Vector2D && r.Kind == EXKind.Number)
            return EXValue.Of(new EXVec2D(l.Vector2D.X / r.Number, l.Vector2D.Y / r.Number));

        throw new EXScriptRuntimeError($"Operator '{op}' is not defined for {l.Kind} and {r.Kind}.", line);
    }
    static EXValue EvaluateAssign(AssignExpr a, EXScope scope, EXExecContext ctx)
    {
        var value = Evaluate(a.Value, scope, ctx);
        AssignTo(a.Target, value, scope, ctx, a.Line);
        return value;
    }
    static void AssignTo(EXExpr target, EXValue value, EXScope scope, EXExecContext ctx, int line)
    {
        switch (target)
        {
            case Identifier id:
                var result = scope.TrySet(id.Name, value);
                if (result == EXSetResult.NotFound)
                    throw new EXScriptRuntimeError($"Assignment to undeclared variable '{id.Name}'. Did you forget 'var'?", line);
                if (result == EXSetResult.ConstViolation)
                    throw new EXScriptRuntimeError($"Cannot assign to '{id.Name}'; it was declared with 'const'.", line);
                return;

            case MemberAccess m:
                var receiver = Evaluate(m.Receiver, scope, ctx);

                if (receiver.Kind == EXKind.Object)
                {
                    if (!receiver.Object.Type.TrySetMember(receiver.Object.Instance, m.Name, value))
                        throw new EXScriptRuntimeError($"'{receiver.Object.Type.TypeName}' has no settable member '{m.Name}'.", line);
                    return;
                }

                if (receiver.Kind == EXKind.Vector3D)
                {
                    var v = receiver.Vector3D;
                    var updated = m.Name switch
                    {
                        "x" => new EXVec3D(RequireNumber(value), v.Y, v.Z),
                        "y" => new EXVec3D(v.X, RequireNumber(value), v.Z),
                        "z" => new EXVec3D(v.X, v.Y, RequireNumber(value)),
                        _ => throw new EXScriptRuntimeError($"Vector3D has no settable member '{m.Name}'.", line)
                    };
                    AssignTo(m.Receiver, EXValue.Of(updated), scope, ctx, line); // write the WHOLE vector back
                    return;
                }

                if (receiver.Kind == EXKind.Vector2D)
                {
                    var v = receiver.Vector2D;
                    var updated = m.Name switch
                    {
                        "x" => new EXVec2D(RequireNumber(value), v.Y),
                        "y" => new EXVec2D(v.X, RequireNumber(value)),
                        _ => throw new EXScriptRuntimeError($"Vector2D has no settable member '{m.Name}'.", line)
                    };
                    AssignTo(m.Receiver, EXValue.Of(updated), scope, ctx, line);
                    return;
                }

                ctx.Host.AssignMember(receiver, m.Name, value);
                return;

            case IndexAccess ix:
                {
                    var targ = Evaluate(ix.Target, scope, ctx);
                    if (targ.Kind == EXKind.Vector3D)
                    {
                        var index = RequireNumber(Evaluate(ix.Index, scope, ctx));
                        var v = targ.Vector3D;
                        var updated = (int)index switch
                        {
                            0 => new EXVec3D(RequireNumber(value), v.Y, v.Z),
                            1 => new EXVec3D(v.X, RequireNumber(value), v.Z),
                            2 => new EXVec3D(v.X, v.Y, RequireNumber(value)),
                            _ => throw new EXScriptRuntimeError($"wrong vector3D index {(int)index}", line)
                        };
                        AssignTo(ix.Target, EXValue.Of(updated), scope, ctx, line);
                        return;
                    }
                    if (targ.Kind == EXKind.Vector2D)
                    {
                        var index = RequireNumber(Evaluate(ix.Index, scope, ctx));
                        var v = targ.Vector2D;
                        var updated = (int)index switch
                        {
                            0 => new EXVec2D(RequireNumber(value), v.Y),
                            1 => new EXVec2D(v.X, RequireNumber(value)),
                            _ => throw new EXScriptRuntimeError($"wrong vector2D index {(int)index}", line)
                        };
                        AssignTo(ix.Target, EXValue.Of(updated), scope, ctx, line);
                        return;
                    }
                    if (targ.Kind == EXKind.Object)
                    {
                        if (targ.Object.Type.TrySetIndexer(targ.Object.Instance, Evaluate(ix.Index, scope, ctx), value))
                            return;

                        throw new EXScriptRuntimeError($"{targ.Object.Type.TypeName} has no set indexer", line);
                    }

                    var (arr, i) = ResolveIndex(ix, scope, ctx);
                    arr.Array[i] = value;
                    return;
                }

            default:
                throw new EXScriptRuntimeError("Invalid assignment target.", line);
        }
    }
    static EXValue EvaluateCall(Call c, EXScope scope, EXExecContext ctx)
    {
        var args = new EXValue[c.Arguments.Count];
        for (int i = 0; i < args.Length; i++)
            args[i] = Evaluate(c.Arguments[i], scope, ctx);

        if (c.Receiver == null)
        {
            if(EXStdLib.TryInvoke(c.Name, args, out var stdResult, c.Line)) return stdResult;
            if (ctx.Functions.TryGetValue(c.Name, out var fn)) return InvokeUserFunctionSync(fn, args, ctx, c.Line);
        }

        EXValue receiver = null;
        if (c.Receiver != null)
        {
            receiver = Evaluate(c.Receiver, scope, ctx);
            if (receiver.Kind == EXKind.Null)
                throw new EXScriptRuntimeError($"Cannot call '{c.Name}' on null; the receiver expression resolved to nothing.", c.Line);
        }
        return ctx.Host.Invoke(receiver, c.Name, args, scope);
    }

    static EXValue EvaluateInterpolated(InterpolatedString s, EXScope scope, EXExecContext ctx)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Literals.Count; i++)
        {
            sb.Append(s.Literals[i]);
            if (i < s.Expressions.Count)
                sb.Append(Stringify(Evaluate(s.Expressions[i], scope, ctx)));
        }
        return EXValue.Of(sb.ToString());
    }

    static (EXValue arr, int index) ResolveIndex(IndexAccess ix, EXScope scope, EXExecContext ctx)
    {
        var arr = Evaluate(ix.Target, scope, ctx);
        if (arr.Kind != EXKind.Array)
            throw new EXScriptRuntimeError($"Cannot index into {arr.Kind}.", ix.Line);

        var idxVal = Evaluate(ix.Index, scope, ctx);
        if (idxVal.Kind != EXKind.Number)
            throw new EXScriptRuntimeError($"Array index must be a number, got {idxVal.Kind}.", ix.Line);

        int i = (int)idxVal.Number;
        if (i < 0 || i >= arr.Array.Length)
            throw new EXScriptRuntimeError($"Index {i} out of range (array has {arr.Array.Length} elements).", ix.Line);

        return (arr, i);
    }

    public static bool ValuesEqual(EXValue a, EXValue b)
    {
        if (a.Kind != b.Kind) return false; // cross-kind comparisons are just false, not an error
        return a.Kind switch
        {
            EXKind.Number => a.Number == b.Number,
            EXKind.String => a.String == b.String,
            EXKind.Bool => a.Bool == b.Bool,
            EXKind.Null => true,
            EXKind.Vector3D => a.Vector3D.X == b.Vector3D.X && a.Vector3D.Y == b.Vector3D.Y && a.Vector3D.Z == b.Vector3D.Z,
            EXKind.Vector2D => a.Vector2D.X == b.Vector2D.X && a.Vector2D.Y == b.Vector2D.Y,
            EXKind.Object => ReferenceEquals(a.Object.Instance, b.Object.Instance),
            EXKind.Array => (a.Array == b.Array),
            _ => false
        };
    }

    public static string Stringify(EXValue v) => v.Kind switch
    {
        EXKind.Number => v.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        EXKind.String => v.String,
        EXKind.Bool => v.Bool ? "true" : "false",
        EXKind.Null => "null",
        EXKind.Vector3D => $"{v.Vector3D.X},{v.Vector3D.Y},{v.Vector3D.Z}",
        EXKind.Vector2D => $"{v.Vector2D.X},{v.Vector2D.Y}",
        EXKind.Object => v.Object.Instance?.ToString() ?? "null",
        _ => ""
    };

    public static bool RequireBool(EXValue v, int line = 0) =>
        v.Kind == EXKind.Bool ? v.Bool : throw new EXScriptRuntimeError($"Expected a bool, got {v.Kind} (of value {Stringify(v)})", line);

    public static double RequireNumber(EXValue v, int line = 0) =>
        v.Kind == EXKind.Number ? v.Number : throw new EXScriptRuntimeError($"Expected a number, got {v.Kind} (of value {Stringify(v)})", line);

    public static EXValue ParseFromString(string s)
    {
        if (s == null) return EXValue.Null();
        if (s == "null") return EXValue.Null();
        if (double.TryParse(s, out var n)) return EXValue.Of(n);
        if (s == "true" || s == "false") return EXValue.Of(s == "true");

        var parts = s.Split(',');
        if (parts.Length == 3 && parts.All(p => double.TryParse(p, out _)))
            return EXValue.Of(new EXVec3D(double.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture), double.Parse(parts[2], CultureInfo.InvariantCulture)));
        if (parts.Length == 2 && parts.All(p => double.TryParse(p, out _)))
            return EXValue.Of(new EXVec2D(double.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture)));

        return EXValue.Of(s);
    }
}

public class EXScriptRuntimeError : Exception
{
    public EXScriptRuntimeError(string name, int line)
        : base(line >= 0
            ? $"EXScript Runtime Error: {name}, at line {line}"
            : $"EXScript Runtime Error: {name}")
    { }
}