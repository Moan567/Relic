using System;
using System.Diagnostics;
using System.Reflection;

namespace Engine.Console;
public static class Logger
{
    public delegate bool OnMessage(string message, ConsoleColor color, int skipFrames);
    public static event OnMessage? Message;

    public static void WriteInfo(object value, bool debugOnly = false)
    {
        AppendBasic(value?.ToString() ?? "null", debugOnly);
    }

    public static void AppendBasic(string message, bool debugOnly = false)
    {
#if !DEBUG
        if (debugOnly) { return; }
#endif

        AppendLog(null, message, ConsoleColor.White);
    }

    public static void AppendInfo(object value, bool debugOnly = false)
    {
        AppendInfo(value?.ToString() ?? "null", debugOnly);
    }

    public static void AppendInfo(string message, bool debugOnly = false)
    {
#if !DEBUG
        if (debugOnly) { return; }
#endif

        AppendLog("Info", message, ConsoleColor.Cyan);
    }

    public static void AppendWarn(object value, bool debugOnly = false)
    {
        AppendWarn(value?.ToString() ?? "null", debugOnly);
    }

    public static void AppendWarn(string message, bool debugOnly = false)
    {
#if !DEBUG
        if (debugOnly) { return; }
#endif

        AppendLog("Warn", message, ConsoleColor.Yellow);
    }

    public static void AppendError(object value, bool debugOnly = false)
    {
        AppendError(value?.ToString() ?? "null", debugOnly);
    }

    public static void AppendError(string message, bool debugOnly = false)
    {
#if !DEBUG
        if (debugOnly) { return; }
#endif

        AppendLog("Error", message, ConsoleColor.Red);
    }

    public static void AppendLog(string? header, string message, ConsoleColor color, int skipFrames = 2, bool debugOnly = false)
    {
#if !DEBUG
        if (debugOnly) { return; }
#endif

        OnMessage? msg = Message;

        if (msg != null)
        {
            if (msg.Invoke(message, color, skipFrames))
            {
                return;
            }
        }

        string txt = string.Empty;
        MethodBase stackFrame = new StackFrame(skipFrames).GetMethod();
        string time = $"{DateTime.Now:HH:mm:ss}";

        if (!string.IsNullOrEmpty(header))
        {
            txt = $"[{time}][{header}][{stackFrame?.DeclaringType?.Name}.{stackFrame?.Name}] {message}";
        }
        else
        {
            txt = $"[{time}] {message}";
        }

        if(MainEngine.Instance == null)
        {
            System.Console.ForegroundColor = color;
            System.Console.WriteLine(txt);
            System.Console.ResetColor();
        }
        else
        {
            MainEngine.Instance.Console.WriteDirect(txt, color switch
            {
                ConsoleColor.Yellow => GameConsole.LogLevel.Warning,
                ConsoleColor.Red => GameConsole.LogLevel.Error,
                _ => GameConsole.LogLevel.Message,
            });
        }
    }
}
