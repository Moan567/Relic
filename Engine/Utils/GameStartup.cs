using Chisel;
using Engine.Compilation;
using Engine.Console;
using Microsoft.Xna.Framework.Content;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Engine.Utils;

public static class GameStartup
{
    public static string BaseDirectory => Path.GetDirectoryName(AppContext.BaseDirectory);

    public static void Run<TGame>(Assembly gameAssembly, string[] args) where TGame : MainEngine, new()
    {
        SetInvariantCulture();

        if (!PrepareEntityData(gameAssembly, args ?? []))
        {
            return;
        }

        HookUnhandledExceptions();

        using var game = new TGame();
        game.Run();
    }

    public static void SetInvariantCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
    }

    public static bool PrepareEntityData(Assembly gameAssembly, string[] args)
    {
        string path = BaseDirectory;
        bool compileRequested = args.Contains("-compile");

        if (compileRequested || !EntityDataExists(path))
        {
            CompileEntityData(gameAssembly, path);

            return !(compileRequested && args.Contains("-batch"));
        }

        EntityCompiler.ReadAllEntities(path);
        return true;
    }

    public static bool EntityDataExists(string path)
    {
        return File.Exists($"{path}/Data/entlid.edt")
            && File.Exists($"{path}/Data/entnme.edt")
            && File.Exists($"{path}/Data/entMETA.gff")
            && File.Exists($"{path}/Data/{EntityDataIndex.FileName}");
    }

    public static void CompileEntityData(Assembly gameAssembly, string path)
    {
        Assembly engineAssembly = typeof(MainEngine).Assembly;

        if (gameAssembly != engineAssembly)
        {
            EntityCompiler.CompileAllEntities(gameAssembly, path, false);
        }

        TryWrite(() => EntityCompiler.CompileAllEntities(engineAssembly, path, true), "entity data");
        TryWrite(() => EntityCompiler.WriteDefPaths(path), EntityDataIndex.FileName);
    }

    public static void HookUnhandledExceptions()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowFatalError((Exception)e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            ShowFatalError(e.Exception);
            e.SetObserved();
        };
    }

    static void TryWrite(Action write, string description)
    {
        try
        {
            write();
        }
        catch (Exception e) when (e is UnauthorizedAccessException || e is IOException)
        {
            Logger.AppendWarn($"Could not write {description} to {BaseDirectory}/Data: {e.Message}");
        }
    }

    static unsafe void ShowFatalError(Exception ex)
    {
        if (Debugger.IsAttached)
        {
            return;
        }

        Silk.NET.SDL.Sdl sdl = Silk.NET.SDL.Sdl.GetApi();

        string hint = GetContentHint(ex);
        string message = hint is null
            ? $"The program has encountered a fatal error and cannot continue.\n{ex}"
            : $"{hint}\n\nIf that doesn't help:\nThe program has encountered a fatal error and cannot continue.\n{ex}";

        sdl.ShowSimpleMessageBox((uint)Silk.NET.SDL.MessageBoxFlags.Error, "Engine Error", message, null);
        sdl.Dispose();
    }

    static string GetContentHint(Exception ex)
    {
        bool looksLikeContentIssue =
            ex is ContentLoadException ||
            ex is FileNotFoundException ||
            ex is DirectoryNotFoundException ||
            (ex.Message?.Contains("Content", StringComparison.OrdinalIgnoreCase) ?? false);

        if (!looksLikeContentIssue)
        {
            return null;
        }

        return "This looks like it might be a missing/renamed content file.\nIf you recently moved or deleted a file, try deleting Content/obj and Content/bin, clearing the build directory, then rebuild.";
    }
}