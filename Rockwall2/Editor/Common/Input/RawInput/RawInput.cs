using Avalonia;
using Silk.NET.SDL;
using System;

namespace Rockwall2.Editor.Common.Input.RawInput;
internal interface IRawMouseSource : IDisposable
{
    bool TryInitialize(nint nativeWindowHandle);
    void SetCaptureTarget(Avalonia.Point windowRelative);
    void SetRelativeMode(bool enabled);
    Vector ConsumeDelta();
}