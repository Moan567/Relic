using Avalonia.Input;
using Microsoft.Xna.Framework;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Input;
using Rockwall2.Editor.Mapper.Utils;
using System.Linq;

namespace Rockwall2.Editor.Mapper.Tools;

public class RotateTool : Tool
{
    bool wasDragging = false;
    public override void OnDeselected() 
    {
        wasDragging = false; 
    }
    public override void OnSelected()
    {
        GizmoRotate.ResetPivot();
    }
    public override void OnRender(float delta)
    {
        MapperView.Instance.DrawRotationGizmo();
    }
    public override void OnUpdate(float delta)
    {
        bool isDragging = GizmoRotate.IsDragging;
        if (isDragging && !wasDragging)
            foreach (var obj in Toolbelt.SelectedObjects)
                obj.PrepareMove();
        if (!isDragging && wasDragging)
            Toolbelt.CreateUndoStateForAllSelectedObjects();

        wasDragging = isDragging;
        bool anchor = Toolbelt.SelectedObjects.Count > 0 && (Toolbelt.SelectedObjects[0] is not EntityMoveable || Toolbelt.SelectedObjects.Count > 1);
        GizmoRotate.UpdateUse(anchor);
    }
}