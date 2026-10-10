using Rockwall;
using Rockwall2.Editor.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Mapper.Utils;

public static class TextureClipboard
{
    public static int SourceBrush { get; private set; } = -1;
    public static int SourceFace { get; private set; } = -1;
    public static bool HasSource => SourceBrush != -1 && SourceFace != -1;
    public static string MaterialName { get; private set; }
    public static int Surface { get; private set; }
    public static float TOffX { get; private set; }
    public static float TOffY { get; private set; }
    public static float TScaleX { get; private set; }
    public static float TScaleY { get; private set; }
    public static float UvRotation { get; private set; }
    public static float LuxelScale { get; private set; }
    public static UVProjectionMode UvProjectionMode { get; private set; }

    public static void LiftFromFace(int brushIndex, int faceIndex)
    {
        SourceBrush = brushIndex;
        SourceFace = faceIndex;

        var face = MapTools.ActiveMap.Brushes[brushIndex].Faces[faceIndex];
        MaterialName = face.MaterialName;
        Surface = face.Surface;
        TOffX = face.TOffX;
        TOffY = face.TOffY;
        TScaleX = face.TScaleX;
        TScaleY = face.TScaleY;
        UvRotation = face.UvRotation;
        LuxelScale = face.LuxelScale;
        UvProjectionMode = face.UvProjectionMode;

        TextureSettingsWindow.Instance?.SyncFromClipboard();
    }

    public static void StampOntoFace(int brushIndex, int faceIndex, bool includeMaterial = true)
    {
        if (!HasSource) return;

        ref var face = ref MapTools.ActiveMap.Brushes[brushIndex].Faces[faceIndex];
        if (includeMaterial)
        {
            face.MaterialName = MaterialName;
            face.Surface = Surface;
        }
        face.TOffX = TOffX;
        face.TOffY = TOffY;
        face.TScaleX = TScaleX;
        face.TScaleY = TScaleY;
        face.UvRotation = UvRotation;
        face.LuxelScale = LuxelScale;
        face.UvProjectionMode = UvProjectionMode;
    }

    public static void SyncFromWindow(
        float offX, float offY,
        float scaleX, float scaleY,
        float rotation, float luxelScale,
        UVProjectionMode projMode)
    {
        TOffX = offX;
        TOffY = offY;
        TScaleX = scaleX;
        TScaleY = scaleY;
        UvRotation = rotation;
        LuxelScale = luxelScale;
        UvProjectionMode = projMode;
    }
}