using CommunityToolkit.Mvvm.Input;
using MsBox.Avalonia;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Toolbar;
using Rockwall2.Editor.Common.Utils;
using Rockwall2.Editor.Mapper.Utils;
using Rockwall2.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Mapper.Toolbar;

public static class ToolbarDefinitions
{
    public static IReadOnlyList<ToolCategory> BuildLeftToolbar(ToolIconAtlas atlas) => new[]
    {
        ToolCategory.Of("General",
            ToolButtonDef.Action("select_tool", "Select/Manipulate Tool", atlas.Get(0, 0), new RelayCommand(() =>
            {
                Toolbelt.SwitchTool(Toolbelt.SelectionTool);
                (Toolbelt.SelectionTool as SelectionTool).Bonus3DTool = SelectionTool.Bonus3DToolMode.None;
            })),
            ToolButtonDef.Action("move_tool", "Move Tool", atlas.Get(1, 0), new RelayCommand(() =>
            {
                Toolbelt.SwitchTool(Toolbelt.SelectionTool);
                (Toolbelt.SelectionTool as SelectionTool).Bonus3DTool = SelectionTool.Bonus3DToolMode.Move;
            })),
            ToolButtonDef.Action("rotate_tool", "Rotate Tool", atlas.Get(14, 1), new RelayCommand(() =>
            {
                Toolbelt.SwitchTool(Toolbelt.SelectionTool);
                (Toolbelt.SelectionTool as SelectionTool).Bonus3DTool = SelectionTool.Bonus3DToolMode.Rotate;
            })),
            ToolButtonDef.Action("vertex_edit", "Vertex Tool", atlas.Get(14, 0), new RelayCommand(() =>
            {
                Toolbelt.SwitchTool(Toolbelt.VertexTool);
            }))
        ),
        ToolCategory.Of("Geometry",
            ToolButtonDef.Action("brush_tool", "Brush Box Tool", atlas.Get(2, 0), new RelayCommand(() =>
            {
                Toolbelt.SwitchTool(Toolbelt.BrushTool);
                (Toolbelt.BrushTool as BrushTool).SetPrimitiveMode(BrushTool.PrimitiveShape.Box);
            })),
            ToolButtonDef.Action("cylinder_tool", "Brush Cylinder Tool", atlas.Get(15, 1), new RelayCommand(() =>
            {
                Toolbelt.SwitchTool(Toolbelt.BrushTool);
                (Toolbelt.BrushTool as BrushTool).SetPrimitiveMode(BrushTool.PrimitiveShape.Cylinder);
            })),
            ToolButtonDef.Action("sphere_tool", "Brush Sphere Tool", atlas.Get(0, 2), new RelayCommand(() =>
            {
                Toolbelt.SwitchTool(Toolbelt.BrushTool);
                (Toolbelt.BrushTool as BrushTool).SetPrimitiveMode(BrushTool.PrimitiveShape.Sphere);
            })),
            ToolButtonDef.Action("sphere_tool", "Brush Cone Tool", atlas.Get(1, 2), new RelayCommand(() =>
            {
                Toolbelt.SwitchTool(Toolbelt.BrushTool);
                (Toolbelt.BrushTool as BrushTool).SetPrimitiveMode(BrushTool.PrimitiveShape.Cone);
            })),
            ToolButtonDef.Action("face_tool", "Face Edit Tool", atlas.Get(5,0), new RelayCommand(() => Toolbelt.SwitchTool(Toolbelt.TextureTool)))
        ),
        ToolCategory.Of("Construct",
            ToolButtonDef.Action("clip_tool", "CSG Cut Tool", atlas.Get(15, 0), new RelayCommand(() => Toolbelt.SwitchTool(Toolbelt.ClipTool))),
            ToolButtonDef.Action("subtract_tool", "CSG Subtract", atlas.Get(9, 1), new RelayCommand(() =>
            {
                Toolbelt.QuickCSGSubtract();
                Toolbelt.SelectedObjects.Clear();
            })),
            ToolButtonDef.Action("intersect_tool", "CSG Intersect", atlas.Get(10, 1), new RelayCommand(() =>
            {
                Toolbelt.QuickCSGIntersect();
                Toolbelt.SelectedObjects.Clear();
            })),
            ToolButtonDef.Action("merge_tool", "CSG Merge", atlas.Get(11, 1), new RelayCommand(() =>
            {
                Toolbelt.QuickCSGMerge();
                Toolbelt.SelectedObjects.Clear();
            })),
            ToolButtonDef.Action("stair_build", "Stair Builder", atlas.Get(3, 2), new RelayCommand(() =>
            {
                var faces = Toolbelt.SelectedObjects.OfType<FaceMoveable>()
                    .Where(f => f.brush != -1 && f.face != -1)
                    .ToList();

                if (faces.Count != 1)
                {
                    MessageBoxManager.GetMessageBoxStandard("Stair Builder", "Select exactly 1 face.");
                    return;
                }

                Toolbelt.AddBuilder(new StairBuilder(faces[0]));
            }))
            //ToolButtonDef.Action("arch_build", "Arch Builder", atlas.Get(4, 2), new RelayCommand(() =>
            //{
            //    var faces = Toolbelt.SelectedObjects.OfType<FaceMoveable>()
            //        .Where(f => f.brush != -1 && f.face != -1)
            //        .ToList();

            //    if (faces.Count != 2)
            //    {
            //        MessageBoxManager.GetMessageBoxStandard("Arch Builder", "Select exactly 2 faces (from, to).");
            //        return;
            //    }

            //    Toolbelt.AddBuilder(new ArchBuilder(faces[0], faces[1]));
            //}))
        ),
        ToolCategory.Of("Entities",
            ToolButtonDef.Action("entity_tool", "Entity Tool", atlas.Get(3, 0), new RelayCommand(() => Toolbelt.SwitchTool(Toolbelt.EntityTool))),
            ToolButtonDef.Action("hint_tool", "Hint Tool", atlas.Get(4, 0), new RelayCommand(() => Toolbelt.SwitchTool(Toolbelt.HintTool)))
        ),
    };

    public static IReadOnlyList<ToolCategory> BuildTopToolbar(ToolIconAtlas atlas) => new[]
    {
        ToolCategory.Of(
            ToolButtonDef.Action("new_file", "New File", atlas.Get(1, 1), 
                new RelayCommand(() => MapTools.NewMap())),

            ToolButtonDef.Action("open_file", "Open File", atlas.Get(2, 1), 
                new RelayCommand(() => FileHandler.OpenMap())),

            ToolButtonDef.Action("save_file", "Save File", atlas.Get(3, 1), 
                new RelayCommand(() => FileHandler.SaveCurrentMap()))
        ),
        ToolCategory.Of(
            ToolButtonDef.Toggle("brush_groups", "Treat Brush Entities as Groups", atlas.Get(7, 0), atlas.Get(6, 0),
                new RelayCommand(() => Toolbelt.TreatBrushEntitiesAsGroups = !Toolbelt.TreatBrushEntitiesAsGroups), initialChecked: true),

            ToolButtonDef.Toggle("enable_groups", "Enable Groups", atlas.Get(8, 0), atlas.Get(9, 0),
                new RelayCommand(() => Toolbelt.EnableGroups = !Toolbelt.EnableGroups), initialChecked: true),

            ToolButtonDef.Action("group_selected", "Group Selected Objects", atlas.Get(10, 0), 
                new RelayCommand(() => { MapTools.AddGroup(Toolbelt.CreateGroupFromSelected()); })),

            ToolButtonDef.Action("remove_from_groups", "Remove Selected Objects From Groups", atlas.Get(11, 0), 
                new RelayCommand(() => { Toolbelt.RemoveSelectedFromGroups(); })),

            ToolButtonDef.Action("break_groups", "Break Selected Groups", atlas.Get(6, 1), 
                new RelayCommand(() => { Toolbelt.BreakSelectedGroups(); }))
        ),
        ToolCategory.Of(
            ToolButtonDef.Toggle("show_tool_faces", "Show Tool Faces", atlas.Get(8,1), atlas.Get(7,1), 
                new RelayCommand(()=>{ Toolbelt.HideToolFaces = !Toolbelt.HideToolFaces; }), true),
            ToolButtonDef.Toggle("show_fog", "Show Fog", atlas.Get(13,1), atlas.Get(12,1),
                new RelayCommand(()=>{ Toolbelt.ShowFog = !Toolbelt.ShowFog; }), true)
        )
    };
    public static void ApplyDefaultHotbarAssignments()
    {
        Hotbar.AssignSlot(1, "select_tool");
        Hotbar.AssignSlot(2, "clip_tool");
        Hotbar.AssignSlot(3, "brush_tool");
        Hotbar.AssignSlot(4, "entity_tool");
        Hotbar.AssignSlot(5, "hint_tool");
        Hotbar.AssignSlot(6, "face_tool");
    }
}