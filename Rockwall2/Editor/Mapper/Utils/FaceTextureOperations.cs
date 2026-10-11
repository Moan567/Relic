using DefaultUnDo;
using Rockwall;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Mapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Mapper.Utils;

/// <summary>
/// One-click texture application from the UI (context menu, side panel, hotkeys).
///
/// The viewport already has Ctrl+A "stamp the active texture", but that only reaches the
/// highlighted object and only ever applies Toolbelt.ActiveTexture. This is the same
/// operation driven from a picker, so the user chooses the texture instead of pre-picking it.
/// </summary>
public static class FaceTextureOperations
{
    /// <summary>
    /// Expands the current selection into concrete (brush, face) pairs.
    /// Brushes contribute every face; individual face selections contribute just themselves.
    /// </summary>
    public static List<(int brush, int face)> ResolveSelectionFaces()
    {
        var result = new List<(int brush, int face)>();
        var seen = new HashSet<(int, int)>();

        void Add(int brush, int face)
        {
            if (brush < 0 || brush >= MapTools.Brushes.Length) return;
            if (face < 0 || face >= MapTools.Brushes[brush].Faces.Length) return;
            if (seen.Add((brush, face))) result.Add((brush, face));
        }

        foreach (var obj in Toolbelt.SelectedObjects)
        {
            switch (obj)
            {
                case FaceMoveable f:
                    Add(f.brush, f.face);
                    break;

                case BrushMoveable b:
                    for (int i = 0; i < MapTools.Brushes[b.brush].Faces.Length; i++)
                        Add(b.brush, i);
                    break;
            }
        }

        return result;
    }

    /// <summary>Faces under the cursor when nothing is selected, so right-click works on a fresh click.</summary>
    public static List<(int brush, int face)> ResolveHoverFaces()
    {
        var result = new List<(int brush, int face)>();

        switch (Toolbelt.HighlightedObject)
        {
            case FaceMoveable f:
                if (f.brush >= 0 && f.brush < MapTools.Brushes.Length
                    && f.face >= 0 && f.face < MapTools.Brushes[f.brush].Faces.Length)
                    result.Add((f.brush, f.face));
                break;

            case BrushMoveable b:
                if (b.brush >= 0 && b.brush < MapTools.Brushes.Length)
                    for (int i = 0; i < MapTools.Brushes[b.brush].Faces.Length; i++)
                        result.Add((b.brush, i));
                break;
        }

        return result;
    }

    /// <summary>
    /// Opens the material picker and applies the chosen texture to the current selection.
    /// Falls back to the hovered face when nothing is selected.
    /// </summary>
    public static async Task ChangeTextureOfSelectionAsync()
    {
        var targets = ResolveSelectionFaces();
        if (targets.Count == 0) targets = ResolveHoverFaces();
        if (targets.Count == 0) return;

        int result = await MaterialPicker.PickAsync(Rockwall2.Views.MainWindow.Instance);
        if (result < 0 || result >= GlobalMapData.LoadedMaterials.Length) return;

        ApplyMaterial(targets, GlobalMapData.LoadedMaterials[result].Name);
    }

    /// <summary>
    /// Applies a material by name across the given faces as a single undoable step.
    /// </summary>
    public static void ApplyMaterial(IEnumerable<(int brush, int face)> targets, string materialName)
    {
        if (string.IsNullOrEmpty(materialName)) return;
        if (GlobalMapData.MaterialNameToIndex == null) return;

        var brushIds = new HashSet<int>();
        var restores = new List<Action>();

        foreach (var (brush, face) in targets)
        {
            if (brush < 0 || brush >= MapTools.Brushes.Length) continue;
            if (face < 0 || face >= MapTools.Brushes[brush].Faces.Length) continue;

            var faceRef = MapTools.Brushes[brush].Faces[face];

            restores.Add(SnapshotFaceForUndo(brush, face));

            // Leave tool faces (nodraw/clip/trigger/skybox) alone; they're behaviour flags,
            // not decoration, and silently texturing them breaks triggers and clips.
            if (IsToolMaterial(faceRef.MaterialName)) continue;

            faceRef.MaterialName = materialName;
            faceRef.Surface = GlobalMapData.MaterialNameToIndex.TryGetValue(materialName, out int surf) ? surf : faceRef.Surface;

            MapTools.Brushes[brush].Faces[face] = faceRef;
            brushIds.Add(brush);
        }

        foreach (int brush in brushIds)
            BrushOperations.RebuildBrush(ref MapTools.Brushes[brush]);

        Toolbelt.UndoManager.DoOnUndo(() =>
        {
            foreach (var restore in restores) restore();
        });
    }

    public static bool IsToolMaterial(string materialName)
    {
        return materialName != null
            && materialName.StartsWith("tool_", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Captures everything a texture change can disturb so undo restores the face exactly.
    /// Mirrors the private helpers in SelectionTool/TextureApplicationTool.
    /// </summary>
    public static Action SnapshotFaceForUndo(int brushIndex, int faceIndex)
    {
        var face = MapTools.Brushes[brushIndex].Faces[faceIndex];

        string name = face.MaterialName;
        int surface = face.Surface;
        float offX = face.TOffX, offY = face.TOffY;
        float scaleX = face.TScaleX, scaleY = face.TScaleY;
        float rotation = face.UvRotation;
        float luxel = face.LuxelScale;
        var projection = face.UvProjectionMode;

        return () =>
        {
            ref var target = ref MapTools.ActiveMap.Brushes[brushIndex].Faces[faceIndex];

            target.MaterialName = name;
            target.Surface = surface;
            target.TOffX = offX;
            target.TOffY = offY;
            target.TScaleX = scaleX;
            target.TScaleY = scaleY;
            target.UvRotation = rotation;
            target.LuxelScale = luxel;
            target.UvProjectionMode = projection;

            BrushOperations.RebuildBrush(ref MapTools.Brushes[brushIndex]);
        };
    }
}