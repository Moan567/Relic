using Engine.Console;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Engine.Utils;

public class CVarType<T>
{
    private readonly CommandBinding convar;
    private readonly Func<string, T> parseFunc;
    private T internalValue;

    public string CommandName { get; private set; }

    public CVarType(string name, T initialValue, Func<string, T> parseFunc, Func<T,T>? noValue = null)
    {
        CommandName = name;
        this.convar = new CommandBinding(name, (arg) =>
        {
            if(arg == null || arg.Length == 0)
            {
                if (noValue == null)
                    MainEngine.Instance.Console.WriteDirect($"{name} is currently {internalValue}, default is {initialValue}.");
                else
                    internalValue = noValue(internalValue);
            }
            else
            {
                internalValue = parseFunc(arg[0]);
            }
        });

        this.parseFunc = parseFunc;
        this.internalValue = initialValue;
    }

    public T GetValue() => internalValue;
}

// These are just handy shorthands so that people dont have to manually use the CVarType with common types.
public class CVarFloat : CVarType<float>
{
    public CVarFloat(string name, float initialValue) : base(name, initialValue, (s)=>float.Parse(s, CultureInfo.InvariantCulture))
    {
    }
    public static implicit operator float(CVarFloat value)
    {
        return value.GetValue();
    }
}
public class CVarInt : CVarType<int>
{
    public CVarInt(string name, int initialValue) : base(name, initialValue, (s) => int.Parse(s))
    {
    }
    public static implicit operator int(CVarInt value)
    {
        return value.GetValue();
    }
}
public class CVarBool : CVarType<bool>
{
    public CVarBool(string name, bool initialValue) : base(name, initialValue, 
        (s) => { if (int.TryParse(s, out int a)) return a != 0; else return bool.Parse(s); },
        (b) => { MainEngine.Instance.Console.WriteDirect($"{name} is {!b}.");  return !b; })
    {
    }
    public static implicit operator bool(CVarBool value)
    {
        return value.GetValue();
    }
}