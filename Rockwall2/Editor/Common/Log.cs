using System;

namespace Rockwall2.Editor.Common;

/// <summary>
/// Minimal warning sink for the material preview scene.
///
/// The project's real Logger lives in Engine.Console, and Rockwall2 deliberately has no
/// project reference to Engine, so the preview gets its own tiny shim rather than pulling
/// in a dependency that would drag the whole engine into the editor.
/// </summary>
static class Log
{
    public static void Warn(string message) => Console.WriteLine("[MaterialPreview] " + message);
}